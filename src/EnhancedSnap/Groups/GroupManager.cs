using System.Runtime.InteropServices;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Displays;
using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Core.Interop;
using EnhancedSnap.Core.Layouts;
using EnhancedSnap.Core.Settings;
using EnhancedSnap.Engine.Native;
using EnhancedSnap.Engine.Overlay;
using EnhancedSnap.Engine.Platform;
using EnhancedSnap.Engine.Snapping;
using EnhancedSnap.Engine.Ui;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Groups;

/// <summary>
/// Snap groups: windows snapped into the same layout on the same monitor. Owns each group's base (frame), its
/// dividers (moving one resizes every adjoining window, between small windows too, within each app's minimum size
/// and the display's aspect-ratio limits), its taskbar previews, raising a group as a unit, and member lifecycle
/// (closed, minimized, maximized, cloaked, moved away).
/// Event driven: a global foreground/minimize WinEvent hook plus hooks scoped to member processes only.
/// </summary>
internal sealed unsafe class GroupManager : IDividerOwner, ITaskbarGroupsHost, IDisposable
{
    private const long DragFrameMs = 16;

    private static GroupManager? _instance;

    private readonly CompositionHost _composition;
    private readonly MonitorModel _monitors;
    private readonly SnapTracker _tracker;
    private readonly SystemState _system;
    private readonly Func<AppSettings> _settings;
    private readonly GroupMaterials _materials;
    private readonly TaskbarGroups _taskbar;
    private readonly List<SnapGroup> _groups = [];
    private readonly Dictionary<Guid, GroupFrame> _frames = [];
    private readonly Dictionary<Guid, DividerLayer> _layers = [];
    private readonly Dictionary<uint, ProcessHook> _processHooks = [];
    private nint _foregroundHook;
    private nint _minimizeHook;
    private long _ignoreMovesUntil;

    private SnapGroup? _dragGroup;
    private Divider _dragDivider;
    private (double Low, double High) _dragRange;
    private nint _dragLive;
    private List<nint> _dragOthers = [];
    private PointI _pendingPoint;
    private bool _hasPending;
    private long _lastApplied;

    public GroupManager(CompositionHost composition, MonitorModel monitors, SnapTracker tracker, SystemState system, Func<AppSettings> settings)
    {
        _composition = composition;
        _monitors = monitors;
        _tracker = tracker;
        _system = system;
        _settings = settings;
        _materials = new GroupMaterials(composition.Compositor);
        _taskbar = new TaskbarGroups(this);
        Theme = FluentTheme.Create(system, settings());
    }

    public IReadOnlyList<SnapGroup> Groups => _groups;

    public FluentTheme Theme { get; private set; }

    /// <summary>Window currently being resized by the user (its moves are expected, not "moved away").</summary>
    public nint ActiveResize { get; set; }

    public void Start()
    {
        _instance = this;
        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, &OnEvent, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        _minimizeHook = SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, 0, &OnEvent, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    // UPDATE: Remember groups across restarts (zone fractions + window identities, re-attached by process and title).
    public SnapGroup? Of(nint hwnd) => _groups.FirstOrDefault(g => g.Members.ContainsKey(hwnd));

    public SnapGroup? Find(Guid id) => _groups.FirstOrDefault(g => g.Id == id);

    /// <summary>
    /// Add a window to the group that owns this layout on the monitor: the most recently used group where the
    /// zones are free; otherwise the most recent one, displacing the windows that covered those zones; otherwise
    /// a new group.
    /// </summary>
    public SnapGroup Join(MonitorZones monitor, LayoutDefinition layout, int[] zones, nint hwnd, SnapGroup? prefer = null)
    {
        Leave(hwnd);
        var candidates = _groups
            .Where(g => g.MonitorId == monitor.Id && g.Layout.Id == layout.Id && g.Zones.Length == layout.Zones.Count)
            .OrderByDescending(g => g.LastUsed)
            .ToList();

        var target = prefer is not null && candidates.Contains(prefer) && !prefer.OccupiedZones.Intersect(zones).Any()
            ? prefer
            : candidates.FirstOrDefault(g => !g.OccupiedZones.Intersect(zones).Any());
        if (target is null && candidates.FirstOrDefault() is { } recent)
        {
            target = recent;
            foreach (var displaced in recent.Members.Where(m => m.Value.Intersect(zones).Any()).Select(m => m.Key).ToList())
            {
                RemoveMember(recent, displaced);
                _tracker.SetGroup(displaced, Guid.Empty);
            }
        }

        if (target is null)
        {
            target = new SnapGroup(monitor.Id, layout, [.. layout.Zones]);
            _groups.Add(target);
        }

        foreach (int zone in zones.Where(z => z < target.Zones.Length))
        {
            var free = GroupGeometry.FreeSpace(target.Zones, zone);
            if (free.W > 0 && free.H > 0)
            {
                target.Zones[zone] = free; // the window gets the whole space, also what moved dividers left behind
            }
        }

        target.Members[hwnd] = zones;
        target.Touch(hwnd);
        AddProcessHook(hwnd);
        SyncTaskbar();
        return target;
    }

    /// <summary>Remove a window from its group (it stays where it is). Dissolves groups left empty.</summary>
    public void Leave(nint hwnd)
    {
        var group = Of(hwnd);
        if (group is null)
        {
            return;
        }

        RemoveMember(group, hwnd);
        if (group.Members.Count == 0)
        {
            Dissolve(group);
        }
        else
        {
            Refresh(group);
            SyncTaskbar();
        }
    }

    /// <summary>Ungroup: every member becomes an independent snapped window.</summary>
    public void Dissolve(SnapGroup group)
    {
        foreach (var hwnd in group.Members.Keys.ToList())
        {
            RemoveMember(group, hwnd);
            _tracker.SetGroup(hwnd, Guid.Empty);
        }

        _groups.Remove(group);
        if (_frames.Remove(group.Id, out var frame))
        {
            frame.Dispose();
        }

        if (_layers.Remove(group.Id, out var layer))
        {
            layer.Dispose();
        }

        SyncTaskbar();
    }

    public RectI[] ZoneRects(SnapGroup group)
    {
        var monitor = _monitors.FindById(group.MonitorId);
        if (monitor is null)
        {
            return [];
        }

        var s = _settings();
        return ZoneMath.ComputeZones(group.Zones, monitor.Display.WorkArea, monitor.Display.Scale, s.Gap, s.Margin);
    }

    public RectI MemberRect(SnapGroup group, int[] zones)
    {
        var rects = ZoneRects(group);
        return rects.Length == 0 ? default : ZoneMath.UnionOf(rects, zones.Where(i => i < rects.Length).ToArray());
    }

    public RectI WorkArea(SnapGroup group) => _monitors.FindById(group.MonitorId)?.Display.WorkArea ?? default;

    public float DisplayScale(SnapGroup group) => (float)(_monitors.FindById(group.MonitorId)?.Display.Scale ?? 1);

    /// <summary>
    /// The space an empty zone really has on screen (<see cref="GroupGeometry.FreeSpace"/>): with moved dividers it is
    /// larger or smaller than the zone itself. Empty when members cover it.
    /// </summary>
    public RectI EmptyRect(SnapGroup group, int zone)
    {
        var monitor = _monitors.FindById(group.MonitorId);
        var free = GroupGeometry.FreeSpace(group.Zones, zone);
        if (monitor is null || free.W <= 0 || free.H <= 0)
        {
            return default;
        }

        var s = _settings();
        return ZoneMath.ComputeZones([free], monitor.Display.WorkArea, monitor.Display.Scale, s.Gap, s.Margin)[0];
    }

    /// <summary>
    /// The empty space of a shown group under the pointer — Windows Snap's "drag a window into the gap of a group":
    /// the space left by a closed or dragged-out window, or a zone nobody picked yet.
    /// </summary>
    public (SnapGroup Group, int Zone, RectI Rect)? EmptyZoneAt(PointI point, string monitorId)
    {
        foreach (var group in _groups
            .Where(g => g.MonitorId == monitorId && g.Members.Count > 0 && g.Members.Keys.All(IsShownMember))
            .OrderByDescending(g => g.LastUsed))
        {
            foreach (int zone in group.EmptyZones())
            {
                var rect = EmptyRect(group, zone);
                if (!rect.IsEmpty && rect.Contains(point))
                {
                    return (group, zone, rect);
                }
            }
        }

        return null;
    }

    /// <summary>Test probes: show (or hide, -1) a divider's hover bar.</summary>
    public void ShowBar(SnapGroup group, int index)
    {
        if (_layers.TryGetValue(group.Id, out var layer))
        {
            layer.ShowBar(index);
        }
    }

    /// <summary>Test probes: finish a drag started without a pointer.</summary>
    public void EndHeldDrag()
    {
        if (_dragGroup is { } group)
        {
            EndDividerDrag(group);
        }
    }

    /// <summary>Test probes: the group's taskbar preview (or its full-size peek) as a BMP file.</summary>
    public bool SavePreview(SnapGroup group, string path, bool peek = false) => _taskbar.SavePreview(group, path, 480, 300, peek);

    public string TaskbarSummary(SnapGroup group) => _taskbar.Summary(group);

    /// <summary>The group proxy standing for a window in Alt + Tab and Task View, or 0 (input thread).</summary>
    public nint SwitcherProxy(nint hwnd) => _taskbar.SwitcherProxy(hwnd);

    public void BeginSwitch(nint proxy, bool altTab) => _taskbar.BeginSwitch(proxy, altTab);

    public void EndSwitch() => _taskbar.EndSwitch();

    /// <summary>
    /// Close (X) on the group in the taskbar, Alt + Tab or Task View: every window of the group is asked to close, as if
    /// its own close button was clicked (apps may still ask to save; the group keeps whatever stays open).
    /// </summary>
    public void CloseWindows(SnapGroup group)
    {
        var members = group.Members.Keys.ToList();
        Log.Info($"Closing the {members.Count} windows of {group}");
        foreach (nint hwnd in members)
        {
            Win32.PostMessage(hwnd, WM_SYSCOMMAND, SC_CLOSE, 0);
        }
    }

    /// <summary>Theme, settings or display changed.</summary>
    public void RefreshAll()
    {
        Theme = FluentTheme.Create(_system, _settings());
        foreach (var group in _groups.ToList())
        {
            if (_monitors.FindById(group.MonitorId) is null)
            {
                Dissolve(group);
                continue;
            }

            Refresh(group);
        }

        SyncTaskbar();
    }

    /// <summary>Show, hide or redraw the base and dividers of a group.</summary>
    public void Refresh(SnapGroup group, nint raiseWith = 0)
    {
        var monitor = _monitors.FindById(group.MonitorId);
        bool eligible = monitor is not null && group.Members.Count >= 2 && group.Members.Keys.All(IsShownMember);
        if (!eligible)
        {
            HideSurfaces(group); // e.g. a member minimized or moved to another desktop: never leave the base alone
            return;
        }

        var display = monitor!.Display;
        var s = _settings();
        float scale = (float)display.Scale;
        var (inner, gap) = ZoneMath.Inner(display.WorkArea, display.Scale, s.Gap, s.Margin);
        var (bounds, radius) = FrameGeometry(group, display, gap, scale);

        bool raise = raiseWith != 0;
        if (s.GroupFrame)
        {
            if (!_frames.TryGetValue(group.Id, out var frame))
            {
                frame = new GroupFrame(_composition, _materials);
                _frames[group.Id] = frame;
            }

            frame.Update(bounds, radius, Theme, scale);
            raise |= !frame.IsShown;
        }
        else if (_frames.Remove(group.Id, out var unused))
        {
            unused.Dispose();
        }

        if (!_layers.TryGetValue(group.Id, out var layer))
        {
            layer = new DividerLayer(_composition, _materials, this, group);
            _layers[group.Id] = layer;
        }

        layer.Update(bounds, Handles(group, inner, gap, scale), Theme, scale);
        raise |= !layer.IsShown;

        if (raise)
        {
            Raise(group, raiseWith != 0 ? raiseWith : group.RecentOrder.FirstOrDefault(group.Members.ContainsKey));
        }
    }

    /// <summary>Bring the whole group forward as one unit: <paramref name="top"/> first, the dividers above it, the base behind.</summary>
    public void Raise(SnapGroup group, nint top)
    {
        if (top == 0 || !group.Members.ContainsKey(top))
        {
            top = group.Members.Keys.First();
        }

        group.Touch(top);
        var order = group.RecentOrder.Where(h => group.Members.ContainsKey(h) && IsWindow(h) && !IsHungAppWindow(h)).ToList();
        nint batch = BeginDeferWindowPos(order.Count + 1);
        nint previous = 0;
        foreach (nint hwnd in order)
        {
            if (previous != 0 && batch != 0)
            {
                batch = DeferWindowPos(batch, hwnd, previous, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            }

            previous = hwnd;
        }

        if (batch != 0)
        {
            EndDeferWindowPos(batch);
        }

        bool visible = group.Members.Count >= 2 && group.Members.Keys.All(IsShownMember);
        if (visible && previous != 0 && _frames.TryGetValue(group.Id, out var frame))
        {
            frame.Show(previous); // directly behind the bottom-most member
        }

        if (visible && order.Count > 0 && _layers.TryGetValue(group.Id, out var layer) && !layer.IsDragging)
        {
            layer.PlaceAbove(order[0]);
        }

        _taskbar.Invalidate(group);
    }

    /// <summary>Restore and raise a group (taskbar preview, Alt + Tab).</summary>
    public void Activate(SnapGroup group)
    {
        foreach (nint hwnd in group.Members.Keys)
        {
            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, SW_RESTORE);
            }
        }

        nint top = group.RecentOrder.FirstOrDefault(group.Members.ContainsKey);
        Win32.SetForegroundWindow(top); // first, so the rest is stacked right behind it
        Refresh(group, raiseWith: top);
    }

    /// <summary>
    /// After a window joins: move dividers so every member gets at least its app's minimum size and the aspect-ratio
    /// floor (when that is possible at all), re-placing the other members. <paramref name="except"/> is placed by the caller.
    /// </summary>
    public void FitMinimums(SnapGroup group, nint except)
    {
        var monitor = _monitors.FindById(group.MonitorId);
        if (monitor is null || group.Members.Count < 2)
        {
            return;
        }

        bool changed = false;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            bool moved = false;
            foreach (var divider in group.MemberDividers())
            {
                var (low, high) = DividerRange(group, divider, monitor);
                if (low > high || (divider.Position >= low - 1e-4 && divider.Position <= high + 1e-4))
                {
                    continue; // fits, or cannot fit whatever we do
                }

                group.Zones = GroupGeometry.MoveDivider(group.Zones, divider, Math.Clamp(divider.Position, low, high), ZoneFloor(divider, monitor));
                moved = changed = true;
                break; // dividers changed: look again
            }

            if (!moved)
            {
                break;
            }
        }

        if (changed)
        {
            foreach (var (hwnd, zones) in group.Members.ToList())
            {
                if (hwnd != except)
                {
                    PlaceMember(group, hwnd, zones, final: true);
                }
            }

            Log.Info($"Adjusted {group} so every window fits its minimum size");
        }
    }

    public void Dispose()
    {
        if (_foregroundHook != 0)
        {
            UnhookWinEvent(_foregroundHook);
        }

        if (_minimizeHook != 0)
        {
            UnhookWinEvent(_minimizeHook);
        }

        foreach (var hook in _processHooks.Values)
        {
            hook.Unhook();
        }

        _processHooks.Clear();
        foreach (var frame in _frames.Values)
        {
            frame.Dispose();
        }

        foreach (var layer in _layers.Values)
        {
            layer.Dispose();
        }

        _frames.Clear();
        _layers.Clear();
        _taskbar.Dispose();
        _instance = null;
    }

    // ------------------------------------------------------------------ divider drag (IDividerOwner)

    public void BeginDividerDrag(SnapGroup group, DividerHandle handle, PointI point)
    {
        var monitor = _monitors.FindById(group.MonitorId);
        if (monitor is null || !_layers.TryGetValue(group.Id, out var layer))
        {
            return;
        }

        _dragGroup = group;
        _dragDivider = handle.Divider;
        _dragRange = DividerRange(group, handle.Divider, monitor);
        _hasPending = false;

        // Like Windows Snap: the window in front that borders the divider resizes live, the others become
        // placeholders until release (no flurry of apps re-laying out at once, no overlap while they catch up).
        var touching = MembersTouching(group, handle.Divider);
        _dragLive = group.RecentOrder.FirstOrDefault(h => touching.Contains(h) && IsWindow(h) && !IsHungAppWindow(h));
        _dragOthers = group.Members.Keys.Where(h => h != _dragLive).ToList();

        var s = _settings();
        var (_, gap) = ZoneMath.Inner(monitor.Display.WorkArea, monitor.Display.Scale, s.Gap, s.Margin);
        var (bounds, radius) = FrameGeometry(group, monitor.Display, gap, (float)monitor.Display.Scale);
        layer.BeginDrag(_dragLive, bounds, radius, _dragOthers.Select(h => MemberRect(group, group.Members[h])).ToList());
        layer.MarkDragging(handle.Divider);
        EfficiencyMode.SetCurrentThreadHighQos(true);
        SetTimer(layer.InputHwnd, DividerLayer.DragTimerId, (uint)DragFrameMs, 0);
        Log.Debug($"Divider drag {handle.Divider.Orientation} at {handle.Divider.Position:0.###}, range {_dragRange.Low:0.###}–{_dragRange.High:0.###}");
    }

    public void DragDivider(SnapGroup group, PointI point)
    {
        if (_dragGroup != group)
        {
            return;
        }

        _pendingPoint = point;
        _hasPending = true;
        if (Environment.TickCount64 - _lastApplied >= DragFrameMs)
        {
            ApplyDrag(final: false);
        }
    }

    public void DividerTimer(SnapGroup group)
    {
        if (_dragGroup == group && _hasPending)
        {
            ApplyDrag(final: false);
        }
    }

    public void EndDividerDrag(SnapGroup group)
    {
        if (_dragGroup != group)
        {
            return;
        }

        if (_layers.TryGetValue(group.Id, out var layer))
        {
            KillTimer(layer.InputHwnd, DividerLayer.DragTimerId);
        }

        ApplyDrag(final: true);
        _dragGroup = null;
        _dragLive = 0;
        _dragOthers = [];
        EfficiencyMode.SetCurrentThreadHighQos(false);
    }

    /// <summary>Move a divider to follow an edge the user is dragging on one member (joint resize).</summary>
    public void FollowEdge(SnapGroup group, nint resizing, Divider divider, int edgePixel)
    {
        var monitor = _monitors.FindById(group.MonitorId);
        if (monitor is null)
        {
            return;
        }

        var s = _settings();
        var (inner, gap) = ZoneMath.Inner(monitor.Display.WorkArea, monitor.Display.Scale, s.Gap, s.Margin);
        bool vertical = divider.Orientation == DividerOrientation.Vertical;
        double fraction = ZoneMath.PixelToFraction(edgePixel, vertical ? inner.Left : inner.Top, vertical ? inner.Width : inner.Height, gap);
        var (low, high) = DividerRange(group, divider, monitor);
        if (low > high)
        {
            return;
        }

        MoveDividerTo(group, divider, Math.Clamp(fraction, low, high), monitor, exclude: resizing, final: false);
        Refresh(group);
    }

    public void FinishResize(SnapGroup group)
    {
        foreach (var (hwnd, zones) in group.Members.ToList())
        {
            PlaceMember(group, hwnd, zones, final: true);
        }

        Refresh(group);
    }

    private void ApplyDrag(bool final)
    {
        var group = _dragGroup;
        if (group is null)
        {
            return;
        }

        var monitor = _monitors.FindById(group.MonitorId);
        if (monitor is null)
        {
            return;
        }

        _lastApplied = Environment.TickCount64;
        var s = _settings();
        var (inner, gap) = ZoneMath.Inner(monitor.Display.WorkArea, monitor.Display.Scale, s.Gap, s.Margin);
        bool vertical = _dragDivider.Orientation == DividerOrientation.Vertical;
        if (_hasPending && _dragRange.Low <= _dragRange.High)
        {
            int pixel = vertical ? _pendingPoint.X : _pendingPoint.Y;
            double fraction = ZoneMath.PixelToFraction(pixel, vertical ? inner.Left : inner.Top, vertical ? inner.Width : inner.Height, gap);
            fraction = Math.Clamp(fraction, _dragRange.Low, _dragRange.High); // no window ever goes below its minimum
            _hasPending = false;
            MoveDividerTo(group, _dragDivider, fraction, monitor, exclude: 0, final: false, liveOnly: true);
            if (_layers.TryGetValue(group.Id, out var layer))
            {
                var handle = Handles(group, inner, gap, (float)monitor.Display.Scale).FirstOrDefault(h => h.Divider == _dragDivider);
                layer.UpdateDrag(_dragOthers.Select(h => MemberRect(group, group.Members[h])).ToList(), handle);
            }
        }

        _hasPending = false;
        if (!final)
        {
            return;
        }

        foreach (var (hwnd, zones) in group.Members.ToList())
        {
            PlaceMember(group, hwnd, zones, final: true);
        }

        if (_layers.TryGetValue(group.Id, out var done))
        {
            done.EndDrag();
        }

        Refresh(group);
        Raise(group, group.RecentOrder.FirstOrDefault(group.Members.ContainsKey));
    }

    private void MoveDividerTo(SnapGroup group, Divider divider, double fraction, MonitorZones monitor, nint exclude, bool final, bool liveOnly = false)
    {
        group.Zones = GroupGeometry.MoveDivider(group.Zones, divider, fraction, ZoneFloor(divider, monitor));
        double perpendicular = (divider.Start + divider.End) / 2;
        var memberFractions = group.Members.Values.Select(group.MemberFraction).ToList();
        if (GroupGeometry.Find(memberFractions, divider.Orientation, Math.Clamp(fraction, 0, 1), perpendicular) is { } moved && _dragGroup == group)
        {
            _dragDivider = moved;
        }

        foreach (var (hwnd, zones) in group.Members.ToList())
        {
            if (hwnd != exclude && (!liveOnly || hwnd == _dragLive))
            {
                PlaceMember(group, hwnd, zones, final);
            }
        }
    }

    /// <summary>Where a divider may go: every member touching it keeps its app's minimum and the aspect-ratio floor.</summary>
    private (double Low, double High) DividerRange(SnapGroup group, Divider divider, MonitorZones monitor)
    {
        var s = _settings();
        var area = monitor.Display.WorkArea;
        var (inner, gap) = ZoneMath.Inner(area, monitor.Display.Scale, s.Gap, s.Margin);
        bool vertical = divider.Orientation == DividerOrientation.Vertical;
        int innerExtent = vertical ? inner.Width : inner.Height;
        var members = group.Members.ToList();
        var rects = members.Select(m => group.MemberFraction(m.Value)).ToList();
        var minimums = members.Select(m =>
        {
            var (width, height) = WindowPlacer.GetMinimumFrameSize(m.Key);
            int pixels = GroupGeometry.MinimumExtent(vertical ? width : height, area);
            return GroupGeometry.ExtentToFraction(pixels, innerExtent, gap);
        }).ToList();
        return GroupGeometry.DividerRange(rects, divider, minimums);
    }

    /// <summary>The aspect-ratio floor for zones (including empty ones, e.g. Snap Assist's space) along a divider's axis.</summary>
    private double ZoneFloor(Divider divider, MonitorZones monitor)
    {
        var s = _settings();
        var area = monitor.Display.WorkArea;
        var (inner, gap) = ZoneMath.Inner(area, monitor.Display.Scale, s.Gap, s.Margin);
        bool vertical = divider.Orientation == DividerOrientation.Vertical;
        return GroupGeometry.ExtentToFraction(GroupGeometry.MinimumExtent(0, area), vertical ? inner.Width : inner.Height, gap);
    }

    private static HashSet<nint> MembersTouching(SnapGroup group, Divider divider)
    {
        bool vertical = divider.Orientation == DividerOrientation.Vertical;
        var touching = new HashSet<nint>();
        foreach (var (hwnd, zones) in group.Members)
        {
            var f = group.MemberFraction(zones);
            double start = vertical ? f.Y : f.X;
            double end = vertical ? f.Bottom : f.Right;
            bool onLine = vertical
                ? Math.Abs(f.Right - divider.Position) < 1e-3 || Math.Abs(f.X - divider.Position) < 1e-3
                : Math.Abs(f.Bottom - divider.Position) < 1e-3 || Math.Abs(f.Y - divider.Position) < 1e-3;
            if (onLine && Math.Min(end, divider.End) - Math.Max(start, divider.Start) > 1e-4)
            {
                touching.Add(hwnd);
            }
        }

        return touching;
    }

    private void PlaceMember(SnapGroup group, nint hwnd, int[] zones, bool final)
    {
        var target = MemberRect(group, zones);
        if (target.IsEmpty)
        {
            return;
        }

        _ignoreMovesUntil = Environment.TickCount64 + 700;
        if (final)
        {
            WindowPlacer.Place(hwnd, target, _settings().Corners);
        }
        else
        {
            WindowPlacer.PlaceAsync(hwnd, target);
        }

        _tracker.UpdateFrame(hwnd, final ? WindowPlacer.GetFrame(hwnd) ?? target : target);
    }

    /// <summary>The base wraps the windows with the same spacing that separates them, clipped to the work area.</summary>
    private (RectI Bounds, float Radius) FrameGeometry(SnapGroup group, DisplayInfo display, int gap, float scale)
    {
        var rects = ZoneRects(group);
        var union = ZoneMath.UnionOf(rects, group.OccupiedZones.Where(i => i < rects.Length).Distinct().ToArray());
        int padding = Math.Max(gap, (int)MathF.Round(6 * scale));
        var bounds = union.Inflate(padding, padding, padding, padding).Intersect(display.WorkArea);
        return (bounds, (8 * scale) + padding); // concentric with the windows' 8 px corners
    }

    private static List<DividerHandle> Handles(SnapGroup group, RectI inner, int gap, float scale)
    {
        var handles = new List<DividerHandle>();
        int hitHalf = Math.Max((gap + 1) / 2, (int)MathF.Round(3 * scale)); // the gap itself (at least 6 epx)
        int barHalf = Math.Max((gap / 2) + (int)MathF.Round(4 * scale), (int)MathF.Round(8 * scale)); // a little over the seam
        foreach (var d in group.MemberDividers())
        {
            if (d.Orientation == DividerOrientation.Vertical)
            {
                int x = (int)Math.Round(ZoneMath.FractionToGapCenter(d.Position, inner.Left, inner.Width, gap));
                int y0 = inner.Top + (int)Math.Round(d.Start * (inner.Height + gap));
                int y1 = inner.Top + (int)Math.Round(d.End * (inner.Height + gap)) - gap;
                handles.Add(new DividerHandle(d, new RectI(x - hitHalf, y0, x + hitHalf, y1), new RectI(x - barHalf, y0, x + barHalf, y1)));
            }
            else
            {
                int y = (int)Math.Round(ZoneMath.FractionToGapCenter(d.Position, inner.Top, inner.Height, gap));
                int x0 = inner.Left + (int)Math.Round(d.Start * (inner.Width + gap));
                int x1 = inner.Left + (int)Math.Round(d.End * (inner.Width + gap)) - gap;
                handles.Add(new DividerHandle(d, new RectI(x0, y - hitHalf, x1, y + hitHalf), new RectI(x0, y - barHalf, x1, y + barHalf)));
            }
        }

        return handles;
    }

    private void HideSurfaces(SnapGroup group)
    {
        if (_frames.TryGetValue(group.Id, out var frame))
        {
            frame.Hide();
        }

        if (_layers.TryGetValue(group.Id, out var layer))
        {
            layer.Hide();
        }
    }

    private void SyncTaskbar() => _taskbar.Sync(_groups, _settings().GroupsInTaskbar && _settings().Enabled);

    private static bool IsShownMember(nint hwnd) => IsWindow(hwnd) && IsWindowVisible(hwnd) && !IsIconic(hwnd) && !IsZoomed(hwnd) && !IsCloaked(hwnd);

    private void RemoveMember(SnapGroup group, nint hwnd)
    {
        if (group.Members.Remove(hwnd))
        {
            group.RecentOrder.Remove(hwnd);
            RemoveProcessHook(hwnd);
        }
    }

    // ------------------------------------------------------------------ member lifecycle

    private void MemberGone(nint hwnd, bool untrack)
    {
        Leave(hwnd);
        if (untrack)
        {
            _tracker.Remove(hwnd);
        }
        else
        {
            _tracker.SetGroup(hwnd, Guid.Empty);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        var self = _instance;
        if (self is null || hwnd == 0 || idObject != OBJID_WINDOW || idChild != CHILDID_SELF)
        {
            return;
        }

        try
        {
            self.Handle(eventType, hwnd);
        }
        catch (Exception ex)
        {
            Log.Error($"Group event 0x{eventType:X} failed", ex);
        }
    }

    private void Handle(uint eventType, nint hwnd)
    {
        var group = Of(hwnd);
        if (group is null)
        {
            return;
        }

        switch (eventType)
        {
            case EVENT_SYSTEM_FOREGROUND:
                if (_layers.TryGetValue(group.Id, out var layer) && layer.IsShown && !layer.IsDragging)
                {
                    Raise(group, hwnd);
                }
                else
                {
                    group.Touch(hwnd);
                }

                break;
            case EVENT_SYSTEM_MINIMIZESTART:
            case EVENT_OBJECT_CLOAKED:
                Refresh(group);
                break;
            case EVENT_SYSTEM_MINIMIZEEND:
                Refresh(group, raiseWith: hwnd);
                break;
            case EVENT_OBJECT_UNCLOAKED:
                Refresh(group);
                break;
            case EVENT_OBJECT_DESTROY:
            case EVENT_OBJECT_HIDE:
                if (!IsWindow(hwnd) || !IsWindowVisible(hwnd))
                {
                    MemberGone(hwnd, untrack: !IsWindow(hwnd));
                }

                break;
            case EVENT_OBJECT_LOCATIONCHANGE:
                if (hwnd == ActiveResize || _dragGroup is not null || Environment.TickCount64 < _ignoreMovesUntil || IsIconic(hwnd))
                {
                    return;
                }

                if (IsZoomed(hwnd))
                {
                    MemberGone(hwnd, untrack: true); // maximized: no longer part of the arrangement
                }
                else if (_tracker.TryGet(hwnd, out var record) && WindowPlacer.GetFrame(hwnd) is { } now && !now.NearlyEquals(record.Frame, 12))
                {
                    MemberGone(hwnd, untrack: true); // the app (or another tool) moved it away
                }

                break;
        }
    }

    private void AddProcessHook(nint hwnd)
    {
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (_processHooks.TryGetValue(pid, out var existing))
        {
            existing.Count++;
            return;
        }

        _processHooks[pid] = new ProcessHook(
            SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_HIDE, 0, &OnEvent, pid, 0, WINEVENT_OUTOFCONTEXT),
            SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, 0, &OnEvent, pid, 0, WINEVENT_OUTOFCONTEXT),
            SetWinEventHook(EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED, 0, &OnEvent, pid, 0, WINEVENT_OUTOFCONTEXT));
    }

    private void RemoveProcessHook(nint hwnd)
    {
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (_processHooks.TryGetValue(pid, out var hook) && --hook.Count <= 0)
        {
            hook.Unhook();
            _processHooks.Remove(pid);
        }
    }

    private sealed class ProcessHook(nint lifetime, nint location, nint cloak)
    {
        public int Count { get; set; } = 1;

        public void Unhook()
        {
            foreach (nint hook in (ReadOnlySpan<nint>)[lifetime, location, cloak])
            {
                if (hook != 0)
                {
                    UnhookWinEvent(hook);
                }
            }
        }
    }
}
