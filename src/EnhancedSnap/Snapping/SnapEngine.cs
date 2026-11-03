using System.Runtime.InteropServices;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Core.Interop;
using EnhancedSnap.Core.Layouts;
using EnhancedSnap.Core.Localization;
using EnhancedSnap.Core.Settings;
using EnhancedSnap.Engine.Groups;
using EnhancedSnap.Engine.Input;
using EnhancedSnap.Engine.Native;
using EnhancedSnap.Engine.Overlay;
using EnhancedSnap.Engine.Platform;
using EnhancedSnap.Engine.Ui;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Snapping;

/// <summary>
/// The snapping brain. Replaces Windows Snap end to end: drag to edges/corners, the snap bar, the snap
/// layouts flyout (maximize-button hover and Win + Z), Snap Assist, snap groups with an Edge-style frame and
/// shared dividers, Win + Arrow, and Shift + drag zones. Runs on the engine's UI thread, entirely event driven:
/// per-drag hooks/timers exist only while a window is being dragged or a flyout is open.
/// </summary>
internal sealed unsafe class SnapEngine : IInputPolicy, IDisposable
{
    public const nuint DragTimerId = 11;
    public const nuint FlyoutTimerId = 12;
    public const nuint SurfacesTimerId = 14;
    private const uint DragTimerMs = 33;
    private const uint SurfacesIdleMs = 60_000; // GPU devices of the text popups are kept this long after the last one closes

    private static SnapEngine? _instance;
    private static readonly nint[] ResizeCursors =
    [
        LoadCursor(0, IDC_SIZENWSE), LoadCursor(0, IDC_SIZENESW), LoadCursor(0, IDC_SIZEWE), LoadCursor(0, IDC_SIZENS),
    ];

    private readonly nint _host;
    private readonly SystemState _system;
    private readonly CompositionHost _composition;
    private readonly MonitorModel _monitors = new();
    private readonly SnapTracker _tracker = new();
    private readonly OverlayManager _overlays;
    private readonly GroupManager _groups;
    private readonly DrawingSurfaces _surfaces;
    private readonly Action<string> _notifyElevated;
    private readonly HashSet<string> _elevatedNotified = new(StringComparer.OrdinalIgnoreCase);
    private AppSettings _settings;
    private nint _moveSizeHook;
    private nint _locationHook;
    private DragSession? _drag;
    private ResizeSession? _resize;
    private nint _resizing;
    private LayoutFlyout? _flyout;
    private bool _flyoutSticky;
    private int _flyoutAwayTicks;
    private SnapAssist? _assist;
    private (SnapGroup Group, int Zone)? _assistTarget;
    private volatile bool _popupOpen;

    public SnapEngine(nint host, AppSettings settings, SystemState system, CompositionHost composition, Action<string> notifyElevated)
    {
        _host = host;
        _settings = settings;
        _system = system;
        _composition = composition;
        _overlays = new OverlayManager(composition);
        _groups = new GroupManager(composition, _monitors, _tracker, system, () => _settings);
        _surfaces = new DrawingSurfaces(composition.Compositor);
        _notifyElevated = notifyElevated;
    }

    // ------------------------------------------------------------------ IInputPolicy (called on the hook thread)

    public bool HandleWinZ { get { var s = _settings; return s.Enabled && s.ReplaceWindowsSnap && s.LayoutsOnMaximizeHover; } }

    public bool ShouldHandleWinZ(nint hwnd) => HandleWinZ && WindowRules.IsSnappable(hwnd, Volatile.Read(ref _settings).ExcludedApps);

    public bool WatchMaximizeHover { get { var s = _settings; return s.Enabled && s.ReplaceWindowsSnap && s.LayoutsOnMaximizeHover; } }

    public bool PopupOpen => _popupOpen;

    public nint SwitcherProxy(nint hwnd) => _groups.SwitcherProxy(hwnd);

    /// <summary>Which hooks the input thread needs for the current settings.</summary>
    public (bool Keyboard, bool Mouse) InputNeeds(AppSettings s) => (
        s.Enabled && (s.OverrideWinArrows || s.ReplaceWindowsSnap || s.GroupsInTaskbar),
        s.Enabled && s.ReplaceWindowsSnap && (s.LayoutsOnMaximizeHover || s.SnapAssist));

    /// <summary>Alt + Tab (or Win + Tab) pressed in a group window; the input thread held the Tab back.</summary>
    public void OnSwitcher(bool altTab, nint proxy) => _groups.BeginSwitch(proxy, altTab);

    public void OnSwitcherEnd() => _groups.EndSwitch();

    public void Start()
    {
        _instance = this;
        _monitors.Rebuild(_settings);
        _overlays.Sync(_monitors.Monitors, Palette());
        _groups.Start();
        UpdateHooks();
        Log.Info("Displays: " + string.Join("; ", _monitors.Monitors.Select(m =>
            $"{m.Display.GdiDeviceName} work={m.Display.WorkArea} dpi={m.Display.Dpi} layout={m.Layout.Id}")));
    }

    // ------------------------------------------------------------------ settings & system changes

    public void ApplySettings(AppSettings settings)
    {
        var previous = _settings;
        Volatile.Write(ref _settings, settings);
        UpdateHooks();
        _monitors.Rebuild(settings);
        _overlays.Sync(_monitors.Monitors, Palette());
        if (!settings.Enabled)
        {
            HidePopups();
        }

        if (previous.Corners != settings.Corners)
        {
            foreach (var record in _tracker.Records)
            {
                if (settings.Corners == CornerStyle.System)
                {
                    SetDwmInt(record.Hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_DEFAULT);
                }
                else
                {
                    WindowPlacer.ApplyCorners(record.Hwnd, settings.Corners);
                }
            }
        }

        if (settings.KeepWindowsInZones)
        {
            ReapplyAll(requireInPlace: true); // live: moving the gap slider re-flows snapped windows
        }

        _groups.RefreshAll();
    }

    public void OnDisplayChanged()
    {
        CancelDrag();
        HidePopups();
        _monitors.Rebuild(_settings);
        _overlays.Sync(_monitors.Monitors, Palette());
        if (_settings.KeepWindowsInZones)
        {
            ReapplyAll(requireInPlace: false);
        }

        _groups.RefreshAll();
    }

    public void OnVisualStateChanged()
    {
        _overlays.Sync(_monitors.Monitors, Palette());
        _groups.RefreshAll();
    }

    /// <summary>Convert windows Windows Snap had arranged into Enhanced Snap groups (rounded, spaced).</summary>
    public void AdoptArrangedWindows()
    {
        var arranged = WindowIcons.SnappableWindows(_settings.ExcludedApps, h => IsWindowArranged(h) && !IsIconic(h));
        foreach (var byMonitor in arranged.GroupBy(h => _monitors.FromWindow(h)?.Id ?? string.Empty))
        {
            var monitor = _monitors.FindById(byMonitor.Key);
            if (monitor is null)
            {
                continue;
            }

            var windows = byMonitor.Where(h => WindowPlacer.GetFrame(h) is not null).ToList();
            var fractions = windows.Select(h => GroupGeometry.ToFraction(WindowPlacer.GetFrame(h)!.Value, monitor.Display.WorkArea, 0)).ToArray();
            var layout = new LayoutDefinition("adopted-" + Guid.NewGuid().ToString("N")[..8], fractions, isCustom: true);
            for (int i = 0; i < windows.Count; i++)
            {
                SnapInto(windows[i], monitor, layout, [i], isFill: false, previousZones: null, assist: false);
            }

            Log.Info($"Adopted {windows.Count} window(s) arranged by Windows Snap on {monitor.Display.GdiDeviceName}");
        }
    }

    public void Preview(nint monitorHandle)
    {
        if (_drag is not null || !_settings.Enabled)
        {
            return;
        }

        MonitorZones? only = null;
        if (monitorHandle != 0)
        {
            only = _monitors.FromHandle(monitorHandle);
            if (only is null)
            {
                _monitors.Rebuild(_settings);
                _overlays.Sync(_monitors.Monitors, Palette());
                only = _monitors.FromHandle(monitorHandle);
            }
        }

        _overlays.Preview(only, TimeSpan.FromMilliseconds(1400));
    }

    public void OnTimer(nuint id)
    {
        switch (id)
        {
            case DragTimerId:
                UpdateDrag();
                break;
            case FlyoutTimerId:
                CheckFlyoutHover();
                break;
            case SurfacesTimerId:
                KillTimer(_host, SurfacesTimerId);
                if (_assist?.IsVisible != true && _flyout?.IsVisible != true)
                {
                    _surfaces.Release();
                }

                break;
        }
    }

    /// <summary>A text popup closed: one-shot timer (re-armed by every close) before the GPU devices are freed.</summary>
    private void ScheduleSurfaceRelease()
    {
        if (_surfaces.HasDevices)
        {
            SetTimer(_host, SurfacesTimerId, SurfacesIdleMs, 0);
        }
    }

    private OverlayPalette Palette() => OverlayPalette.Create(_system, _settings);

    private bool Replacing => _settings.Enabled && _settings.ReplaceWindowsSnap;

    private void UpdateHooks()
    {
        bool wanted = _settings.Enabled;
        if (wanted && _moveSizeHook == 0)
        {
            _moveSizeHook = SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND, 0, &OnWinEvent, 0, 0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            if (_moveSizeHook == 0)
            {
                Log.Error("SetWinEventHook(MOVESIZE) failed");
            }
        }
        else if (!wanted && _moveSizeHook != 0)
        {
            UnhookWinEvent(_moveSizeHook);
            _moveSizeHook = 0;
            CancelDrag();
        }
    }

    // ------------------------------------------------------------------ drag & drop

    [UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        var self = _instance;
        if (self is null || hwnd == 0 || idObject != OBJID_WINDOW || idChild != CHILDID_SELF)
        {
            return;
        }

        try
        {
            switch (eventType)
            {
                case EVENT_SYSTEM_MOVESIZESTART:
                    self.OnMoveSizeStart(hwnd);
                    break;
                case EVENT_SYSTEM_MOVESIZEEND:
                    self.OnMoveSizeEnd(hwnd);
                    break;
                case EVENT_OBJECT_LOCATIONCHANGE when self._drag?.Hwnd == hwnd:
                    self.UpdateDrag();
                    break;
                case EVENT_OBJECT_LOCATIONCHANGE when self._resize?.Hwnd == hwnd:
                    self.UpdateJointResize();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"WinEvent 0x{eventType:X} failed", ex);
        }
    }

    private void OnMoveSizeStart(nint hwnd)
    {
        var settings = _settings;
        if (!settings.Enabled || !WindowRules.IsSnappable(hwnd, settings.ExcludedApps))
        {
            Log.Debug($"MoveSizeStart 0x{hwnd:X} ignored (not snappable)");
            return;
        }

        CancelDrag();
        HidePopups();
        var frame = WindowPlacer.GetFrame(hwnd) ?? default;
        var group = _groups.Of(hwnd);

        if (IsResizeCursor())
        {
            if (group is not null && group.Members.Count >= 2)
            {
                // Joint resize: neighbours follow the edge being dragged (Windows "resize adjacent windows").
                _resize = new ResizeSession(hwnd, group, frame);
                _groups.ActiveResize = hwnd;
                InstallLocationHook(hwnd);
                EfficiencyMode.SetCurrentThreadHighQos(true);
            }
            else
            {
                _resizing = hwnd;
            }

            return;
        }

        Log.Debug($"MoveSizeStart 0x{hwnd:X} frame={frame}");
        Win32.GetCursorPos(out var cursor);
        var session = new DragSession(hwnd, frame, new PointI(cursor.X, cursor.Y));

        if (group is not null)
        {
            _groups.Leave(hwnd); // the frame closes around the remaining windows right away
        }

        // Unsnap: give the window its floating size back right away, like Windows Snap does.
        if (settings.RestoreSizeOnUnsnap && !IsZoomed(hwnd)
            && _tracker.TryGet(hwnd, out var record) && SnapTracker.IsInPlace(record)
            && _tracker.TryGetFloating(hwnd, out var floating))
        {
            var (width, height) = ScaleFloating(hwnd, floating);
            WindowPlacer.RestoreFloatingSize(hwnd, width, height, session.StartCursor);
            session.RestoredSize = (width, height);
        }

        _drag = session;
        InstallLocationHook(hwnd);
        SetTimer(_host, DragTimerId, DragTimerMs, 0);
        EfficiencyMode.SetCurrentThreadHighQos(true);
        UpdateDrag();
    }

    private void InstallLocationHook(nint hwnd)
    {
        uint thread = Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        _locationHook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, 0, &OnWinEvent, pid, thread, WINEVENT_OUTOFCONTEXT);
    }

    private void UpdateDrag()
    {
        var drag = _drag;
        if (drag is null)
        {
            return;
        }

        if (!IsWindow(drag.Hwnd))
        {
            CancelDrag();
            return;
        }

        var settings = _settings;
        Win32.GetCursorPos(out var p);
        var cursor = new PointI(p.X, p.Y);
        bool shift = IsKeyDown(VK_SHIFT);
        bool ctrl = IsKeyDown(VK_CONTROL);
        bool zonesActive = settings.Activation == ActivationMode.HoldShift ? shift : !shift;
        var monitor = _monitors.FromPoint(cursor);
        if (monitor is null || monitor.Zones.Length == 0)
        {
            return;
        }

        // UPDATE: Non-primary mouse button / middle button activation and mouse-wheel layout switching (FancyZones parity).
        if (zonesActive)
        {
            drag.Edge = null;
            if (_flyout is { IsVisible: true, DragMode: true })
            {
                HideFlyout();
            }

            UpdateZones(drag, monitor, cursor, ctrl, settings);
            return;
        }

        if (drag.ZonesActive)
        {
            drag.ZonesActive = false;
            drag.Selection = [];
            drag.SpanAnchor = -1;
            _overlays.HideAll(animate: true);
        }

        if (!Replacing)
        {
            return; // Windows Snap still owns edges and the snap bar
        }

        // Snap bar: dragging to the top centre opens the layouts flyout; the dragged pointer drives it.
        float scale = (float)monitor.Display.Scale;
        var bounds = monitor.Display.Bounds;
        if (_flyout is { IsVisible: true, DragMode: true } flyout)
        {
            flyout.UpdatePointer(cursor); // ZoneHovered previews the hovered zone on screen
            bool near = cursor.Y <= flyout.Bounds.Bottom + (int)(56 * scale)
                && cursor.X >= flyout.Bounds.Left - (int)(56 * scale) && cursor.X <= flyout.Bounds.Right + (int)(56 * scale);
            drag.Edge = null;
            if (near)
            {
                return;
            }

            HideFlyout();
        }

        if (settings.SnapBar && cursor.Y <= bounds.Top + (int)(4 * scale)
            && Math.Abs(cursor.X - bounds.CenterX) <= bounds.Width * 0.18)
        {
            _overlays.HideAll(animate: false);
            ShowFlyout(drag.Hwnd, monitor, cursor, dragMode: true, sticky: false);
            return;
        }

        // Screen edges first (sides → halves, corners → quarters, top → fill), then the empty space of a group
        // under the pointer (Windows Snap fills that gap when the window is dropped there).
        var target = settings.EdgeSnapping ? EdgeTarget(monitor, cursor) : null;
        if (target is null && _groups.EmptyZoneAt(cursor, monitor.Id) is { } gap)
        {
            target = new EdgeSnap(monitor, gap.Group.Layout, gap.Zone, gap.Rect, gap.Group);
        }

        if (target?.Rect != drag.Edge?.Rect)
        {
            drag.Edge = target;
            _overlays.ShowTarget(monitor, target?.Rect, drag.Hwnd);
        }
    }

    private void UpdateZones(DragSession drag, MonitorZones monitor, PointI cursor, bool ctrl, AppSettings settings)
    {
        if (!drag.ZonesActive || drag.Monitor?.Id != monitor.Id)
        {
            if (drag.Monitor?.Id != monitor.Id)
            {
                drag.SpanAnchor = -1;
                drag.Selection = [];
            }

            drag.ZonesActive = true;
            drag.Monitor = monitor;
            _overlays.ShowFor(monitor, settings.ShowZonesOnAllMonitors, drag.Hwnd);
        }

        int zone = ZoneMath.HitTest(monitor.Zones, cursor);
        int[] selection;
        if (ctrl && settings.SpanWithCtrl && zone >= 0)
        {
            if (drag.SpanAnchor < 0)
            {
                drag.SpanAnchor = drag.Selection.Length == 1 ? drag.Selection[0] : zone;
            }

            selection = ZoneMath.SpanSelection(monitor.Zones, drag.SpanAnchor, zone);
        }
        else
        {
            drag.SpanAnchor = -1;
            selection = zone >= 0 ? [zone] : [];
        }

        if (!selection.AsSpan().SequenceEqual(drag.Selection))
        {
            Log.Debug($"Drag target {monitor.Display.GdiDeviceName} zones=[{string.Join(',', selection)}] cursor={cursor}");
            drag.Selection = selection;
            _overlays.SetTarget(monitor, selection.Length > 0 ? ZoneMath.UnionOf(monitor.Zones, selection) : null);
        }
    }

    /// <summary>Windows-style edge snapping: sides → halves, corners → quarters, top → rounded fill.</summary>
    private EdgeSnap? EdgeTarget(MonitorZones monitor, PointI cursor)
    {
        float scale = (float)monitor.Display.Scale;
        var b = monitor.Display.Bounds;
        int reach = Math.Max(2, (int)(6 * scale));
        int corner = Math.Max((int)(96 * scale), b.Height / 8);
        bool left = cursor.X <= b.Left + reach && !HasMonitorAt(new PointI(b.Left - 2, cursor.Y));
        bool right = cursor.X >= b.Right - 1 - reach && !HasMonitorAt(new PointI(b.Right + 1, cursor.Y));
        bool top = cursor.Y <= b.Top + reach && !HasMonitorAt(new PointI(cursor.X, b.Top - 2));
        bool topCorner = cursor.Y <= b.Top + corner;
        bool bottomCorner = cursor.Y >= b.Bottom - corner;

        LayoutDefinition? layout = null;
        int zone = -1;
        if (left || right)
        {
            if (topCorner || bottomCorner)
            {
                layout = LayoutCatalog.FindBuiltIn(LayoutCatalog.Quarters);
                zone = (topCorner ? 0 : 2) + (right ? 1 : 0);
            }
            else
            {
                layout = LayoutCatalog.FindBuiltIn(LayoutCatalog.Halves);
                zone = right ? 1 : 0;
            }
        }
        else if (top)
        {
            layout = LayoutCatalog.FindBuiltIn(LayoutCatalog.Full);
            zone = 0;
        }

        return layout is null ? null : new EdgeSnap(monitor, layout, zone, TargetRect(monitor, layout, [zone]));
    }

    private bool HasMonitorAt(PointI point) =>
        Win32.MonitorFromPoint(new POINT(point.X, point.Y), Win32.MONITOR_DEFAULTTONULL) != 0;

    /// <summary>Where a zone of a layout lands on a monitor: an existing group's (resized) zone, else the template.</summary>
    private RectI TargetRect(MonitorZones monitor, LayoutDefinition layout, int[] zones)
    {
        var group = _groups.Groups.Where(g => g.MonitorId == monitor.Id && g.Layout.Id == layout.Id).OrderByDescending(g => g.LastUsed).FirstOrDefault();
        if (group is not null)
        {
            return _groups.MemberRect(group, zones);
        }

        var s = _settings;
        var rects = ZoneMath.ComputeZones(layout, monitor.Display.WorkArea, monitor.Display.Scale, s.Gap, s.Margin);
        return ZoneMath.UnionOf(rects, zones);
    }

    private void OnMoveSizeEnd(nint hwnd)
    {
        if (_resize is { } resize && resize.Hwnd == hwnd)
        {
            FinishJointResize(resize);
            return;
        }

        if (_resizing == hwnd)
        {
            _resizing = 0;
            if (_tracker.TryGet(hwnd, out var resized) && !SnapTracker.IsInPlace(resized))
            {
                _tracker.Remove(hwnd);
            }

            return;
        }

        var drag = _drag;
        if (drag is null || drag.Hwnd != hwnd)
        {
            return;
        }

        UpdateDrag(); // final modifier/cursor state
        var flyoutTarget = _flyout is { IsVisible: true, DragMode: true } f ? f.Hovered : null;
        var flyoutMonitor = _flyout?.IsVisible == true ? _monitors.FromPoint(_flyout.Bounds.Center) : null;
        EndDragSession();
        HideFlyout();
        _overlays.HideAll(animate: true);

        Win32.GetCursorPos(out var p);
        var cursor = new PointI(p.X, p.Y);
        var frame = WindowPlacer.GetFrame(hwnd) ?? default;
        long dx = cursor.X - drag.StartCursor.X;
        long dy = cursor.Y - drag.StartCursor.Y;
        bool cancelled = (dx * dx) + (dy * dy) > 64 && frame.NearlyEquals(drag.StartFrame, 1);
        Log.Debug($"MoveSizeEnd 0x{hwnd:X} zonesActive={drag.ZonesActive} zones=[{string.Join(',', drag.Selection)}] edge={drag.Edge?.Layout.Id} cancelled={cancelled}");
        if (cancelled)
        {
            return;
        }

        if (drag.ZonesActive && drag.Selection.Length > 0 && drag.Monitor is not null)
        {
            SnapInto(hwnd, drag.Monitor, drag.Monitor.Layout, drag.Selection, isFill: false, previousZones: null, assist: true);
            return;
        }

        if (flyoutTarget is { } target && flyoutMonitor is not null)
        {
            SnapInto(hwnd, flyoutMonitor, target.Layout, [target.Zone], isFill: false, previousZones: null, assist: true);
            return;
        }

        if (drag.Edge is { } edge)
        {
            bool fill = edge.Layout.Id == LayoutCatalog.Full;
            SnapInto(hwnd, edge.Monitor, edge.Layout, [edge.Zone], isFill: fill, previousZones: null, assist: !fill, into: edge.Group);
            return;
        }

        if (_tracker.TryGet(hwnd, out _))
        {
            // Dropped outside any target: floating again. Apply the size restore now if the move loop overrode it.
            if (drag.RestoredSize is { } size && (Math.Abs(frame.Width - size.Width) > 2 || Math.Abs(frame.Height - size.Height) > 2))
            {
                WindowPlacer.RestoreFloatingSize(hwnd, size.Width, size.Height, cursor);
            }

            _tracker.Remove(hwnd);
        }
    }

    private void UpdateJointResize()
    {
        if (_resize is not { } resize || !_groups.Groups.Contains(resize.Group))
        {
            return;
        }

        var frame = WindowPlacer.GetFrame(resize.Hwnd);
        if (frame is null || !resize.Group.Members.TryGetValue(resize.Hwnd, out var zones))
        {
            return;
        }

        var f = frame.Value;
        var member = resize.Group.MemberFraction(zones);
        foreach (var divider in resize.Group.MemberDividers())
        {
            bool vertical = divider.Orientation == DividerOrientation.Vertical;
            bool overlaps = vertical
                ? Math.Min(member.Bottom, divider.End) - Math.Max(member.Y, divider.Start) > 1e-3
                : Math.Min(member.Right, divider.End) - Math.Max(member.X, divider.Start) > 1e-3;
            if (!overlaps)
            {
                continue;
            }

            // Which of the window's edges sits on this divider, and where is it now?
            int? edge = null;
            if (vertical && Math.Abs(member.Right - divider.Position) < 1e-3 && f.Right != resize.StartFrame.Right)
            {
                edge = f.Right + (Gap(resize.Group) / 2);
            }
            else if (vertical && Math.Abs(member.X - divider.Position) < 1e-3 && f.Left != resize.StartFrame.Left)
            {
                edge = f.Left - (Gap(resize.Group) / 2);
            }
            else if (!vertical && Math.Abs(member.Bottom - divider.Position) < 1e-3 && f.Bottom != resize.StartFrame.Bottom)
            {
                edge = f.Bottom + (Gap(resize.Group) / 2);
            }
            else if (!vertical && Math.Abs(member.Y - divider.Position) < 1e-3 && f.Top != resize.StartFrame.Top)
            {
                edge = f.Top - (Gap(resize.Group) / 2);
            }

            if (edge is { } pixel)
            {
                _groups.FollowEdge(resize.Group, resize.Hwnd, divider, pixel);
                return;
            }
        }
    }

    private int Gap(SnapGroup group)
    {
        var monitor = _monitors.FindById(group.MonitorId);
        return monitor is null ? 0 : ZoneMath.Inner(monitor.Display.WorkArea, monitor.Display.Scale, _settings.Gap, _settings.Margin).Gap;
    }

    private void FinishJointResize(ResizeSession resize)
    {
        _resize = null;
        _groups.ActiveResize = 0;
        if (_locationHook != 0)
        {
            UnhookWinEvent(_locationHook);
            _locationHook = 0;
        }

        EfficiencyMode.SetCurrentThreadHighQos(false);
        if (_groups.Groups.Contains(resize.Group))
        {
            _groups.FinishResize(resize.Group); // snap every member, including the resized one, onto the grid
        }
    }

    private void CancelDrag()
    {
        if (_drag is null)
        {
            return;
        }

        EndDragSession();
        HideFlyout();
        _overlays.HideAll(animate: false);
    }

    private void EndDragSession()
    {
        _drag = null;
        if (_locationHook != 0)
        {
            UnhookWinEvent(_locationHook);
            _locationHook = 0;
        }

        KillTimer(_host, DragTimerId);
        EfficiencyMode.SetCurrentThreadHighQos(false);
    }

    private static bool IsResizeCursor()
    {
        var info = new CURSORINFO { cbSize = (uint)sizeof(CURSORINFO) };
        return GetCursorInfo(ref info) && Array.IndexOf(ResizeCursors, info.hCursor) >= 0;
    }

    // ------------------------------------------------------------------ snap layouts flyout

    /// <summary>The pointer rested on a window's maximize button (from the input thread).</summary>
    public void OnMaximizeHover(nint hwnd)
    {
        if (!WatchMaximizeHover || _drag is not null || _flyout?.IsVisible == true || _assist?.IsVisible == true
            || !WindowRules.IsSnappable(hwnd, _settings.ExcludedApps))
        {
            return;
        }

        var monitor = _monitors.FromWindow(hwnd);
        if (monitor is null)
        {
            return;
        }

        Win32.GetCursorPos(out var p);
        ShowFlyout(hwnd, monitor, new PointI(p.X, p.Y), dragMode: false, sticky: false);
    }

    /// <summary>Win + Z: snap layouts for the active window, anchored at its maximize button.</summary>
    public void OnWinZ(nint hwnd)
    {
        if (!HandleWinZ || !WindowRules.IsSnappable(hwnd, _settings.ExcludedApps))
        {
            return;
        }

        var monitor = _monitors.FromWindow(hwnd);
        var frame = WindowPlacer.GetFrame(hwnd);
        if (monitor is null || frame is null)
        {
            return;
        }

        float scale = (float)monitor.Display.Scale;
        var anchor = new PointI(frame.Value.Right - (int)(69 * scale), frame.Value.Top + (int)(16 * scale));
        ShowFlyout(hwnd, monitor, anchor, dragMode: false, sticky: true);
    }

    private void ShowFlyout(nint hwnd, MonitorZones monitor, PointI anchor, bool dragMode, bool sticky)
    {
        if (_flyout is null)
        {
            _flyout = new LayoutFlyout(_composition, _surfaces);
            _flyout.ZoneHovered += OnFlyoutZoneHovered;
            _flyout.ZoneChosen += OnFlyoutZoneChosen;
            _flyout.SuggestionChosen += OnFlyoutSuggestionChosen;
        }

        HideAssist();
        _flyoutSticky = sticky;
        _flyoutAwayTicks = 0;
        var recent = dragMode ? [] : WindowIcons.SnappableWindows(_settings.ExcludedApps, h => h != hwnd && !IsIconic(h));
        _flyout.ShowFor(hwnd, monitor, anchor, _groups.Theme, dragMode, recent);
        _popupOpen = sticky; // hover flyouts dismiss by pointer; Win + Z ones by Esc / outside click
        if (!dragMode)
        {
            SetTimer(_host, FlyoutTimerId, 120, 0);
        }
    }

    private void HideFlyout()
    {
        if (_flyout?.IsVisible == true)
        {
            _flyout.Hide();
            _overlays.HideAll(animate: false);
        }

        KillTimer(_host, FlyoutTimerId);
        _popupOpen = _assist?.IsVisible == true;
        ScheduleSurfaceRelease(); // suggestion icons
    }

    private void CheckFlyoutHover()
    {
        if (_flyout is not { IsVisible: true } flyout || flyout.DragMode)
        {
            KillTimer(_host, FlyoutTimerId);
            return;
        }

        if (_flyoutSticky)
        {
            return;
        }

        Win32.GetCursorPos(out var p);
        var cursor = new PointI(p.X, p.Y);
        float scale = (float)(_monitors.FromPoint(cursor)?.Display.Scale ?? 1);
        long dx = cursor.X - flyout.Anchor.X;
        long dy = cursor.Y - flyout.Anchor.Y;
        bool nearAnchor = (dx * dx) + (dy * dy) <= (long)(48 * scale * 48 * scale);
        bool bridge = cursor.Y >= flyout.Anchor.Y && cursor.Y <= flyout.Bounds.Top + 4 && cursor.X >= flyout.Bounds.Left && cursor.X <= flyout.Bounds.Right;
        _flyoutAwayTicks = flyout.Contains(cursor) || nearAnchor || bridge ? 0 : _flyoutAwayTicks + 1;
        if (_flyoutAwayTicks >= 3)
        {
            HideFlyout();
        }
    }

    private void OnFlyoutZoneHovered(MonitorZones monitor, LayoutDefinition? layout, int zone)
    {
        if (layout is null || zone < 0)
        {
            _overlays.ShowTarget(monitor, null, 0);
            return;
        }

        nint below = _flyout?.Hwnd ?? 0;
        _overlays.ShowTarget(monitor, TargetRect(monitor, layout, [zone]), below);
        if (_drag is { } drag)
        {
            drag.Edge = null;
        }
    }

    private void OnFlyoutZoneChosen(nint hwnd, MonitorZones monitor, LayoutDefinition layout, int zone)
    {
        HideFlyout();
        if (IsWindow(hwnd))
        {
            SnapInto(hwnd, monitor, layout, [zone], isFill: false, previousZones: null, assist: true);
        }
    }

    /// <summary>A snap suggestion: the window and the suggested apps snap together as one group, the window in front.</summary>
    private void OnFlyoutSuggestionChosen(MonitorZones monitor, SnapSuggestion suggestion)
    {
        HideFlyout();
        nint hwnd = suggestion.Windows[0];
        if (!IsWindow(hwnd) || !SnapInto(hwnd, monitor, suggestion.Layout, [0], isFill: false, previousZones: null, assist: false))
        {
            return;
        }

        var group = _groups.Of(hwnd);
        for (int zone = 1; zone < suggestion.Windows.Length; zone++)
        {
            nint other = suggestion.Windows[zone];
            if (!IsWindow(other))
            {
                continue;
            }

            if (IsIconic(other))
            {
                ShowWindow(other, SW_RESTORE);
            }

            SnapInto(other, monitor, suggestion.Layout, [zone], isFill: false, previousZones: null, assist: false, into: group);
        }

        if (_groups.Of(hwnd) is { } formed)
        {
            _groups.Raise(formed, hwnd);
        }

        Win32.SetForegroundWindow(hwnd);
    }

    // ------------------------------------------------------------------ Snap Assist

    private void StartAssist(SnapGroup group)
    {
        if (!Replacing || !_settings.SnapAssist || !_groups.Groups.Contains(group))
        {
            return;
        }

        int[] empty = [.. group.EmptyZones().Where(z => !_groups.EmptyRect(group, z).IsEmpty)];
        var monitor = _monitors.FindById(group.MonitorId);
        if (empty.Length == 0 || monitor is null)
        {
            HideAssist();
            return;
        }

        var candidates = WindowIcons.SnappableWindows(_settings.ExcludedApps, h => !group.Members.ContainsKey(h));
        if (candidates.Count == 0)
        {
            HideAssist();
            return;
        }

        if (_assist is null)
        {
            _assist = new SnapAssist(_composition, _surfaces);
            _assist.Chosen += OnAssistChosen;
            _assist.Dismissed += HideAssist;
        }

        _assistTarget = (group, empty[0]);
        _assist.ShowIn(_groups.EmptyRect(group, empty[0]), candidates, _groups.Theme, (float)monitor.Display.Scale);
        _popupOpen = true;
    }

    private void OnAssistChosen(nint hwnd)
    {
        if (_assistTarget is not { } target)
        {
            HideAssist();
            return;
        }

        var monitor = _monitors.FindById(target.Group.MonitorId);
        _assist?.Hide();
        _popupOpen = false;
        ScheduleSurfaceRelease();
        if (monitor is null || !IsWindow(hwnd))
        {
            return;
        }

        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
        }

        SnapInto(hwnd, monitor, target.Group.Layout, [target.Zone], isFill: false, previousZones: null, assist: true);
        Win32.SetForegroundWindow(hwnd);
    }

    private void HideAssist()
    {
        _assist?.Hide();
        _assistTarget = null;
        _popupOpen = _flyout?.IsVisible == true && _flyoutSticky;
        ScheduleSurfaceRelease();
    }

    /// <summary>Esc / arrows / Enter while a popup is open (from the input thread).</summary>
    public void OnPopupKey(int vk, bool shift)
    {
        if (_assist?.IsVisible == true)
        {
            _assist.HandleKey(vk, shift);
        }
        else if (_flyout?.IsVisible == true && vk == VK_ESCAPE)
        {
            HideFlyout();
        }
    }

    /// <summary>A mouse button went down anywhere while a popup was open: dismiss if outside it.</summary>
    public void OnPointerDown(PointI point)
    {
        if (_assist?.IsVisible == true && !_assist.Contains(point))
        {
            HideAssist();
        }

        if (_flyout?.IsVisible == true && !_flyout.Contains(point))
        {
            HideFlyout();
        }
    }

    private void HidePopups()
    {
        HideFlyout();
        HideAssist();
    }

    // ------------------------------------------------------------------ keyboard (Win + Arrow)

    /// <summary>Called on the input thread: decide synchronously whether Win + Arrow belongs to us.</summary>
    public bool ShouldHandleWinArrow(nint hwnd, int vk)
    {
        var settings = Volatile.Read(ref _settings);
        if (!settings.Enabled || !(settings.OverrideWinArrows || settings.ReplaceWindowsSnap) || !WindowRules.IsSnappable(hwnd, settings.ExcludedApps))
        {
            return false;
        }

        var state = _tracker.GetState(hwnd);
        return vk switch
        {
            VK_LEFT or VK_RIGHT => true,
            VK_UP => state == WindowSnapState.Zoned,
            VK_DOWN => state is WindowSnapState.Zoned or WindowSnapState.Fill,
            _ => false,
        };
    }

    public void HandleWinArrow(int vk, nint hwnd)
    {
        var settings = _settings;
        if (!settings.Enabled || _drag is not null || !WindowRules.IsSnappable(hwnd, settings.ExcludedApps))
        {
            return;
        }

        HidePopups();
        var direction = vk switch
        {
            VK_LEFT => Direction.Left,
            VK_RIGHT => Direction.Right,
            VK_UP => Direction.Up,
            _ => Direction.Down,
        };

        var state = _tracker.GetState(hwnd);
        _tracker.TryGet(hwnd, out var record);
        var group = _groups.Of(hwnd);
        var monitor = (record is not null && state is WindowSnapState.Zoned or WindowSnapState.Fill
            ? _monitors.FindById(record.MonitorId)
            : null) ?? _monitors.FromWindow(hwnd);
        if (monitor is null || monitor.Zones.Length == 0)
        {
            return;
        }

        var frame = WindowPlacer.GetFrame(hwnd) ?? monitor.Display.WorkArea;

        if (state is WindowSnapState.Floating or WindowSnapState.Maximized || record is null)
        {
            if (direction is Direction.Left or Direction.Right)
            {
                int edge = ZoneMath.FindEdgeZone(monitor.Zones, direction, frame.CenterY);
                if (edge >= 0)
                {
                    SnapInto(hwnd, monitor, monitor.Layout, [edge], isFill: false, previousZones: null, assist: Replacing);
                }
            }

            return;
        }

        if (state == WindowSnapState.Fill)
        {
            switch (direction)
            {
                case Direction.Down when record.PreviousZones is { Length: > 0 } previous && previous.All(i => i < monitor.Zones.Length):
                    SnapInto(hwnd, monitor, monitor.Layout, previous, isFill: false, previousZones: null, assist: false);
                    break;
                case Direction.Down:
                    Unsnap(hwnd, record);
                    break;
                case Direction.Left or Direction.Right:
                    int edge = ZoneMath.FindEdgeZone(monitor.Zones, direction, frame.CenterY);
                    if (edge >= 0)
                    {
                        SnapInto(hwnd, monitor, monitor.Layout, [edge], isFill: false, previousZones: null, assist: false);
                    }

                    break;
                default:
                    ShowWindow(hwnd, SW_SHOWMAXIMIZED);
                    break;
            }

            return;
        }

        // Navigate inside the window's own group layout (with its resized dividers) when it has one.
        var layout = group?.Layout ?? monitor.Layout;
        var rects = group is not null ? _groups.ZoneRects(group) : monitor.Zones;
        int next = ZoneMath.FindNeighbor(rects, record.Frame, direction);
        if (next >= 0)
        {
            // Occupied by another member of the group? Swap places.
            var occupant = group?.Members.FirstOrDefault(m => m.Key != hwnd && m.Value.Length == 1 && m.Value[0] == next).Key ?? 0;
            int[] mine = group?.Members.GetValueOrDefault(hwnd) ?? record.Zones;
            SnapInto(hwnd, monitor, layout, [next], isFill: false, previousZones: null, assist: false);
            if (occupant != 0 && mine.Length > 0)
            {
                SnapInto(occupant, monitor, layout, mine, isFill: false, previousZones: null, assist: false);
            }

            return;
        }

        switch (direction)
        {
            case Direction.Left or Direction.Right when settings.MoveAcrossMonitors:
                var other = _monitors.Neighbor(monitor, direction);
                if (other is { Zones.Length: > 0 })
                {
                    var entryEdge = direction == Direction.Left ? Direction.Right : Direction.Left;
                    int entry = ZoneMath.FindEdgeZone(other.Zones, entryEdge, record.Frame.CenterY);
                    if (entry >= 0)
                    {
                        SnapInto(hwnd, other, other.Layout, [entry], isFill: false, previousZones: null, assist: false);
                    }
                }

                break;
            case Direction.Up:
                // "Rounded maximize": fill the work area inside the margins and keep the corners.
                SnapInto(hwnd, monitor, LayoutCatalog.FindBuiltIn(LayoutCatalog.Full)!, [0], isFill: true, previousZones: record.Zones, assist: false);
                break;
            case Direction.Down:
                Unsnap(hwnd, record);
                break;
        }
    }

    // ------------------------------------------------------------------ placement & bookkeeping

    /// <summary>
    /// Snap a window into zones of a layout on a monitor. The window joins (or forms) that layout's snap group,
    /// so its position follows the group's dividers; optionally continues with Snap Assist.
    /// </summary>
    private bool SnapInto(nint hwnd, MonitorZones monitor, LayoutDefinition layout, int[] zones, bool isFill, int[]? previousZones, bool assist, SnapGroup? into = null)
    {
        if (zones.Length == 0 || zones.Any(z => z < 0 || z >= layout.Zones.Count))
        {
            return false;
        }

        if (!_tracker.TryGetFloating(hwnd, out _))
        {
            var (width, height) = WindowPlacer.GetRestoredFrameSize(hwnd);
            _tracker.RememberFloating(hwnd, new FloatingSize(width, height, MonitorDpi(hwnd)));
        }

        var group = _groups.Join(monitor, layout, zones, hwnd, into);
        _groups.FitMinimums(group, except: hwnd); // make room when an app can't shrink to its zone
        var target = _groups.MemberRect(group, zones);
        var result = WindowPlacer.Place(hwnd, target, _settings.Corners);
        if (result == PlacementResult.Ok)
        {
            var frame = WindowPlacer.GetFrame(hwnd) ?? target;
            _tracker.Set(new SnapRecord(hwnd, monitor.Id, layout.Id, zones, frame, isFill, previousZones, group.Id));
            _groups.Refresh(group, raiseWith: hwnd);
            Log.Debug($"Placed 0x{hwnd:X} into {group} zones=[{string.Join(',', zones)}] target={target} frame={frame}");
            if (assist)
            {
                StartAssist(group);
            }

            return true;
        }

        _groups.Leave(hwnd);
        _tracker.Remove(hwnd);
        if (result == PlacementResult.AccessDenied)
        {
            string name = WindowRules.GetProcessFileName(hwnd) ?? "?";
            Log.Info($"Cannot move elevated window of {name}");
            if (_elevatedNotified.Add(name))
            {
                _notifyElevated(name);
            }
        }
        else
        {
            Log.Warn($"Placement of 0x{hwnd:X} failed: {result}");
        }

        return false;
    }

    private void Unsnap(nint hwnd, SnapRecord record)
    {
        _groups.Leave(hwnd);
        if (_tracker.TryGetFloating(hwnd, out var floating))
        {
            var (width, height) = ScaleFloating(hwnd, floating);
            WindowPlacer.MoveFloating(hwnd, record.Frame, width, height);
        }

        _tracker.Remove(hwnd);
    }

    private void ReapplyAll(bool requireInPlace)
    {
        _tracker.Prune();
        foreach (var record in _tracker.Records.ToList())
        {
            if (IsIconic(record.Hwnd) || IsZoomed(record.Hwnd))
            {
                continue;
            }

            if (requireInPlace && !SnapTracker.IsInPlace(record))
            {
                _groups.Leave(record.Hwnd);
                _tracker.Remove(record.Hwnd); // the user moved it themselves; it is no longer ours to manage
                continue;
            }

            var group = _groups.Of(record.Hwnd);
            if (group is not null)
            {
                var groupTarget = _groups.MemberRect(group, group.Members[record.Hwnd]);
                if (!groupTarget.IsEmpty && !groupTarget.NearlyEquals(record.Frame, 0))
                {
                    WindowPlacer.Place(record.Hwnd, groupTarget, _settings.Corners);
                    _tracker.UpdateFrame(record.Hwnd, WindowPlacer.GetFrame(record.Hwnd) ?? groupTarget);
                }

                continue;
            }

            var monitor = _monitors.FindById(record.MonitorId);
            if (monitor is null || monitor.Zones.Length == 0)
            {
                _tracker.Remove(record.Hwnd);
                continue;
            }

            int[] zones = record.IsFill ? [.. Enumerable.Range(0, monitor.Zones.Length)] : MapZones(record, monitor);
            if (zones.Length == 0)
            {
                _tracker.Remove(record.Hwnd);
                continue;
            }

            var target = record.IsFill ? ZoneMath.Bounds(monitor.Zones) : ZoneMath.UnionOf(monitor.Zones, zones);
            if (target.NearlyEquals(record.Frame, 0) && SnapTracker.IsInPlace(record))
            {
                continue;
            }

            if (WindowPlacer.Place(record.Hwnd, target, _settings.Corners) == PlacementResult.Ok)
            {
                _tracker.UpdateFrame(record.Hwnd, WindowPlacer.GetFrame(record.Hwnd) ?? target);
            }
        }
    }

    /// <summary>Same layout → keep zone indices (gap change). Different layout → zone with the largest overlap.</summary>
    private static int[] MapZones(SnapRecord record, MonitorZones monitor)
    {
        if (record.LayoutId == monitor.Layout.Id && record.Zones.All(i => i >= 0 && i < monitor.Zones.Length))
        {
            return record.Zones;
        }

        int best = -1;
        long bestArea = 0;
        for (int i = 0; i < monitor.Zones.Length; i++)
        {
            long area = monitor.Zones[i].Intersect(record.Frame).Area;
            if (area > bestArea)
            {
                best = i;
                bestArea = area;
            }
        }

        return best >= 0 ? [best] : [];
    }

    private int MonitorDpi(nint hwnd) => _monitors.FromWindow(hwnd)?.Display.Dpi ?? 96;

    private (int Width, int Height) ScaleFloating(nint hwnd, FloatingSize floating)
    {
        double factor = MonitorDpi(hwnd) / (double)Math.Max(1, floating.Dpi);
        return ((int)Math.Round(floating.Width * factor), (int)Math.Round(floating.Height * factor));
    }

    // ------------------------------------------------------------------ test-mode hooks

    /// <summary>
    /// Test mode only (ENHANCEDSNAP_TEST_WINDOW_TITLE): drive placement, groups and popups without injecting
    /// real input on a live desktop. Ignored in normal installs.
    /// </summary>
    public void DebugCommand(uint command, int argument, nint hwnd)
    {
        if (WindowRules.TestWindowTitle is null || !IsWindow(hwnd))
        {
            return;
        }

        var monitor = _monitors.FromWindow(hwnd);
        if (monitor is null)
        {
            return;
        }

        switch (command)
        {
            case 0: // snap into zone of the monitor layout (0xFFFF = fill)
                if (argument == 0xFFFF)
                {
                    SnapInto(hwnd, monitor, LayoutCatalog.FindBuiltIn(LayoutCatalog.Full)!, [0], isFill: true, previousZones: null, assist: false);
                }
                else if (argument >= 0 && argument < monitor.Zones.Length)
                {
                    SnapInto(hwnd, monitor, monitor.Layout, [argument], isFill: false, previousZones: null, assist: false);
                }

                break;
            case 1: // highlight a zone under the window
                if (argument >= 0 && argument < monitor.Zones.Length)
                {
                    _overlays.Preview(monitor, TimeSpan.FromMilliseconds(1600), insertAfter: hwnd, target: monitor.Zones[argument]);
                }

                break;
            case 2: // Win + Arrow (argument = virtual key)
                HandleWinArrow(argument, hwnd);
                break;
            case 3: // move divider (argument / 1000 % 100) of the group to (argument % 1000) / 1000 — a simulated drag;
                    // + 100000: keep the drag open (placeholders visible) until command 8; + 200000: only show its hover bar
                if (_groups.Of(hwnd) is { } group && group.MemberDividers().ElementAtOrDefault(argument / 1000 % 100) is { } divider && divider != default)
                {
                    int mode = argument / 100000;
                    int index = argument / 1000 % 100;
                    if (mode == 2)
                    {
                        _groups.ShowBar(group, index);
                        break;
                    }

                    var (inner, gap) = ZoneMath.Inner(monitor.Display.WorkArea, monitor.Display.Scale, _settings.Gap, _settings.Margin);
                    bool vertical = divider.Orientation == DividerOrientation.Vertical;
                    double center = ZoneMath.FractionToGapCenter(argument % 1000 / 1000.0, vertical ? inner.Left : inner.Top, vertical ? inner.Width : inner.Height, gap);
                    var point = vertical ? new PointI((int)center, inner.Center.Y) : new PointI(inner.Center.X, (int)center);
                    _groups.BeginDividerDrag(group, new DividerHandle(divider, default, default), point);
                    _groups.DragDivider(group, point);
                    if (mode == 1)
                    {
                        _groups.ShowBar(group, index);
                    }
                    else
                    {
                        _groups.EndDividerDrag(group);
                    }
                }

                break;
            case 4: // snap layouts flyout for the window
                OnWinZ(hwnd);
                break;
            case 5: // Snap Assist for the window's group
                if (_groups.Of(hwnd) is { } assistGroup)
                {
                    StartAssist(assistGroup);
                }

                break;
            case 6: // the group's taskbar preview, written to the data folder (showing it needs a hover on the taskbar)
                if (_groups.Of(hwnd) is { } previewGroup)
                {
                    string path = Path.Combine(EnhancedSnap.Core.AppInfo.DataDirectory, "taskbar-preview.bmp");
                    Log.Info($"Taskbar preview of {previewGroup}: {(_groups.SavePreview(previewGroup, path) ? path : "failed")}; {_groups.TaskbarSummary(previewGroup)}");
                }

                break;
            case 11: // the group's peek (full-size live preview), written to the data folder like command 6
                if (_groups.Of(hwnd) is { } peekGroup)
                {
                    string path = Path.Combine(EnhancedSnap.Core.AppInfo.DataDirectory, "taskbar-peek.bmp");
                    Log.Info($"Peek of {peekGroup}: {(_groups.SavePreview(peekGroup, path, peek: true) ? path : "failed")}");
                }

                break;
            case 12: // the flyout's first snap suggestion for the window, chosen (validates suggestions without a click)
                if (_flyout?.IsVisible == true && _flyout.Window == hwnd && _flyout.FirstSuggestion is { } first)
                {
                    OnFlyoutSuggestionChosen(monitor, argument == 1 && _flyout.SecondSuggestion is { } second ? second : first);
                }

                break;
            case 7: // snap into a quarters zone (forms groups of small windows)
                SnapInto(hwnd, monitor, LayoutCatalog.FindBuiltIn(LayoutCatalog.Quarters)!, [argument], isFill: false, previousZones: null, assist: false);
                break;
            case 10: // a group's empty space (gap filling): argument 0 = show the drop preview there, 1 = drop the window into it
                var gapGroup = _groups.Groups
                    .Where(g => g.MonitorId == monitor.Id && !g.Members.ContainsKey(hwnd) && g.EmptyZones().Length > 0)
                    .OrderByDescending(g => g.LastUsed)
                    .FirstOrDefault();
                if (gapGroup is not null && _groups.EmptyZoneAt(_groups.EmptyRect(gapGroup, gapGroup.EmptyZones()[0]).Center, monitor.Id) is { } space)
                {
                    if (argument == 0)
                    {
                        _overlays.ShowTarget(monitor, space.Rect, hwnd);
                    }
                    else
                    {
                        _overlays.HideAll(animate: true);
                        SnapInto(hwnd, monitor, space.Group.Layout, [space.Zone], isFill: false, previousZones: null, assist: false, into: space.Group);
                    }
                }

                break;
            case 8: // hide popups and previews, end a held divider drag, hide hover bars
                HidePopups();
                _overlays.HideAll(animate: false);
                _groups.EndHeldDrag();
                if (_groups.Of(hwnd) is { } shown)
                {
                    _groups.ShowBar(shown, -1);
                }

                break;
            case 9: // maximize-button hit test at the window's caption (validates hover detection without moving the pointer)
                if (WindowPlacer.GetFrame(hwnd) is { } caption)
                {
                    float s = (float)monitor.Display.Scale;
                    var max = new POINT(caption.Right - (int)(69 * s), caption.Top + (int)(16 * s));
                    var close = new POINT(caption.Right - (int)(23 * s), caption.Top + (int)(16 * s));
                    Log.Info($"Hit test: maximize button={InputHook.HitsMaximizeButton(hwnd, max)} close button={InputHook.HitsMaximizeButton(hwnd, close)}");
                }

                break;
        }
    }

    public void Dispose()
    {
        CancelDrag();
        HidePopups();
        if (_moveSizeHook != 0)
        {
            UnhookWinEvent(_moveSizeHook);
            _moveSizeHook = 0;
        }

        _flyout?.Dispose();
        _assist?.Dispose();
        _groups.Dispose();
        _overlays.Dispose();
        _surfaces.Dispose();
        _instance = null;
    }

    /// <summary>A Windows Snap drop target: a screen edge, or (with <see cref="Group"/>) a group's empty space.</summary>
    private sealed record EdgeSnap(MonitorZones Monitor, LayoutDefinition Layout, int Zone, RectI Rect, SnapGroup? Group = null);

    private sealed record ResizeSession(nint Hwnd, SnapGroup Group, RectI StartFrame);

    private sealed class DragSession(nint hwnd, RectI startFrame, PointI startCursor)
    {
        public nint Hwnd { get; } = hwnd;

        public RectI StartFrame { get; } = startFrame;

        public PointI StartCursor { get; } = startCursor;

        public (int Width, int Height)? RestoredSize { get; set; }

        public bool ZonesActive { get; set; }

        public MonitorZones? Monitor { get; set; }

        public int[] Selection { get; set; } = [];

        public int SpanAnchor { get; set; } = -1;

        public EdgeSnap? Edge { get; set; }
    }
}
