using System.Numerics;
using System.Runtime.InteropServices;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Core.Layouts;
using EnhancedSnap.Engine.Overlay;
using EnhancedSnap.Engine.Ui;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using EnhancedSnap.Engine.Native;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Groups;

internal interface IDividerOwner
{
    void BeginDividerDrag(SnapGroup group, DividerHandle handle, PointI point);

    void DragDivider(SnapGroup group, PointI point);

    void EndDividerDrag(SnapGroup group);

    void DividerTimer(SnapGroup group);
}

/// <summary>
/// Resizing a snap group the way Windows Snap does it: the gaps between the windows are dividers. Nothing shows until
/// the pointer is over a gap; then a Fluent acrylic bar with a grip appears and the cursor becomes a resize arrow.
/// Dragging shows the other windows as wallpaper placeholders, resizes the window in front live, and places
/// everything on release.
/// Two windows: an input window whose region is exactly the gaps (so it can never take a click meant for an app),
/// and a click-through overlay just below it for the bar and the placeholders.
/// </summary>
internal sealed unsafe class DividerLayer : IDisposable
{
    public const nuint DragTimerId = 1;
    private const string InputClass = "EnhancedSnap.GroupDividers";
    private const string OverlayClass = "EnhancedSnap.GroupOverlay";
    private static readonly Dictionary<nint, DividerLayer> Instances = [];
    private static readonly nint CursorWE = LoadCursor(0, IDC_SIZEWE);
    private static readonly nint CursorNS = LoadCursor(0, IDC_SIZENS);
    private static readonly nint CursorArrow = LoadCursor(0, IDC_ARROW);
    private static bool _classesRegistered;

    private readonly IDividerOwner _owner;
    private readonly Compositor _c;
    private readonly GroupMaterials _materials;
    private readonly DesktopWindowTarget _target;
    private readonly ContainerVisual _root;
    private readonly ContainerVisual _dragRoot;
    private readonly ContainerVisual _barsRoot;
    private readonly List<(DividerHandle Handle, ContainerVisual Bar)> _bars = [];
    private readonly List<ContainerVisual> _placeholders = [];
    private RectI _bounds;
    private FluentTheme? _theme;
    private float _scale = 1;
    private int _hovered = -1;
    private int _dragging = -1;
    private bool _pointerless;
    private bool _tracking;

    public DividerLayer(CompositionHost host, GroupMaterials materials, IDividerOwner owner, SnapGroup group)
    {
        EnsureClasses();
        _owner = owner;
        _materials = materials;
        Group = group;
        _c = host.Compositor;
        nint module = GetModuleHandle(null);
        // No redirection bitmap and no content: nothing is ever drawn, but the region (the gaps) is hit-tested.
        InputHwnd = CreateWindowEx(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP, InputClass, "Enhanced Snap dividers", WS_POPUP, 0, 0, 1, 1, 0, 0, module, 0);
        OverlayHwnd = CreateWindowEx(
            WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP | WS_EX_LAYERED | WS_EX_TRANSPARENT,
            OverlayClass,
            "Enhanced Snap group overlay",
            WS_POPUP,
            0,
            0,
            1,
            1,
            0,
            0,
            module,
            0);
        if (InputHwnd == 0 || OverlayHwnd == 0)
        {
            throw new InvalidOperationException("CreateWindowEx failed for group dividers");
        }

        Instances[InputHwnd] = this;
        SetLayeredWindowAttributes(OverlayHwnd, 0, 255, LWA_ALPHA);
        foreach (nint hwnd in (ReadOnlySpan<nint>)[InputHwnd, OverlayHwnd])
        {
            SetDwmInt(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_DONOTROUND);
            SetDwmInt(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, 1); // hides during peek with the windows, like the base
        }

        SetDwmInt(OverlayHwnd, DWMWA_USE_HOSTBACKDROPBRUSH, 1);
        _target = host.CreateDesktopWindowTarget(OverlayHwnd, isTopmost: false);
        _root = _c.CreateContainerVisual();
        _dragRoot = _c.CreateContainerVisual();
        _barsRoot = _c.CreateContainerVisual();
        _root.Children.InsertAtTop(_dragRoot);
        _root.Children.InsertAtTop(_barsRoot);
        _target.Root = _root;
    }

    public nint InputHwnd { get; }

    public nint OverlayHwnd { get; }

    public SnapGroup Group { get; }

    public bool IsShown { get; private set; }

    public bool IsDragging => _dragging >= 0;

    /// <summary>New dividers, rectangle or theme. Bars are rebuilt (hidden); the input region becomes the gaps.</summary>
    public void Update(RectI bounds, IReadOnlyList<DividerHandle> handles, FluentTheme theme, float scale)
    {
        if (_dragging >= 0)
        {
            return; // the drag owns the visuals; the final refresh comes after it ends
        }

        if (bounds != _bounds)
        {
            SetWindowPos(InputHwnd, 0, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOACTIVATE | SWP_NOZORDER);
            SetWindowPos(OverlayHwnd, 0, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOACTIVATE | SWP_NOZORDER);
        }

        bool same = bounds == _bounds && ReferenceEquals(theme, _theme) && handles.Count == _bars.Count;
        for (int i = 0; same && i < handles.Count; i++)
        {
            same = handles[i] == _bars[i].Handle;
        }

        if (same)
        {
            return;
        }

        _bounds = bounds;
        _theme = theme;
        _scale = scale;
        _root.Size = new Vector2(bounds.Width, bounds.Height);

        nint region = CreateRectRgn(0, 0, 0, 0);
        foreach (var handle in handles)
        {
            var r = handle.HitRect.Offset(-bounds.Left, -bounds.Top);
            nint part = CreateRectRgn(r.Left, r.Top, r.Right, r.Bottom);
            CombineRgn(region, region, part, RGN_OR);
            DeleteObject(part);
        }

        SetWindowRgn(InputHwnd, region, true); // the window owns the region from here on

        _barsRoot.Children.RemoveAll();
        _bars.Clear();
        foreach (var handle in handles)
        {
            var bar = CreateBar(handle, theme, scale);
            _barsRoot.Children.InsertAtTop(bar);
            _bars.Add((handle, bar));
        }

        _hovered = -1;
    }

    /// <summary>Directly above the group's top window: [input][overlay][top member]…</summary>
    public void PlaceAbove(nint topMember)
    {
        nint above = GetWindow(topMember, GW_HWNDPREV);
        while (above == InputHwnd || above == OverlayHwnd)
        {
            above = GetWindow(above, GW_HWNDPREV);
        }

        nint insertAfter = above != 0 ? above : (GetExStyle(topMember) & WS_EX_TOPMOST) != 0 ? HWND_TOPMOST : 0;
        const uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW;
        SetWindowPos(InputHwnd, insertAfter, 0, 0, 0, 0, flags);
        SetWindowPos(OverlayHwnd, InputHwnd, 0, 0, 0, 0, flags);
        IsShown = true;
    }

    public void Hide()
    {
        if (!IsShown)
        {
            return;
        }

        if (_dragging >= 0)
        {
            ReleaseCapture();
        }

        ShowWindow(InputHwnd, SW_HIDE);
        ShowWindow(OverlayHwnd, SW_HIDE);
        IsShown = false;
        SetHover(-1);
    }

    // ------------------------------------------------------------------ drag visuals

    /// <summary>
    /// Covers the group with its frame material and shows the other windows as placeholders; the overlay moves
    /// directly below <paramref name="live"/> (resized live) so everything else is hidden until release.
    /// </summary>
    public void BeginDrag(nint live, RectI union, float frameRadius, IReadOnlyList<RectI> placeholders)
    {
        if (_theme is null)
        {
            return;
        }

        if (live != 0)
        {
            SetWindowPos(OverlayHwnd, live, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        _dragRoot.Children.RemoveAll();
        _placeholders.Clear();
        _dragRoot.Opacity = 1f;
        var backdrop = union.Offset(-_bounds.Left, -_bounds.Top);
        _dragRoot.Children.InsertAtTop(_materials.Fill(_theme.Frame, new Vector2(backdrop.Left, backdrop.Top), new Vector2(backdrop.Width, backdrop.Height), frameRadius));
        float radius = 8 * _scale;
        foreach (var rect in placeholders)
        {
            var r = rect.Offset(-_bounds.Left, -_bounds.Top);
            var placeholder = _materials.Fill(_theme.Placeholder, new Vector2(r.Left, r.Top), new Vector2(r.Width, r.Height), radius);
            var (stroke, _, _) = Shapes.RoundedRect(_c, Vector2.Zero, new Vector2(r.Width, r.Height), radius, null, _theme.FrameStroke, MathF.Max(1, MathF.Round(_scale)));
            placeholder.Children.InsertAtTop(stroke);
            _dragRoot.Children.InsertAtTop(placeholder);
            _placeholders.Add(placeholder);
        }
    }

    /// <summary>Move the placeholders and the dragged bar (no animation: they follow the pointer).</summary>
    public void UpdateDrag(IReadOnlyList<RectI> placeholders, DividerHandle bar)
    {
        for (int i = 0; i < placeholders.Count && i < _placeholders.Count; i++)
        {
            var r = placeholders[i].Offset(-_bounds.Left, -_bounds.Top);
            var visual = _placeholders[i];
            visual.Offset = new Vector3(r.Left, r.Top, 0);
            var size = new Vector2(r.Width, r.Height);
            visual.Size = size;
            if (visual.Clip is CompositionGeometricClip { Geometry: CompositionRoundedRectangleGeometry geometry })
            {
                geometry.Size = size;
            }

            if (visual.Children.FirstOrDefault(v => v is ShapeVisual) is ShapeVisual stroke)
            {
                stroke.Size = size;
                if (stroke.Shapes.FirstOrDefault() is CompositionSpriteShape { Geometry: CompositionRoundedRectangleGeometry outline })
                {
                    float inset = outline.Offset.X;
                    outline.Size = Vector2.Max(Vector2.Zero, size - new Vector2(inset * 2));
                }
            }
        }

        if (_dragging >= 0 && _dragging < _bars.Count)
        {
            var r = bar.Bar.Offset(-_bounds.Left, -_bounds.Top);
            _bars[_dragging].Bar.Offset = new Vector3(r.Left, r.Top, 0);
        }
    }

    /// <summary>Fade the placeholders away once the windows had a moment to paint at their new size.</summary>
    public void EndDrag()
    {
        if (_pointerless)
        {
            _pointerless = false;
            int dragged = _dragging;
            _dragging = -1;
            _hovered = dragged;
            SetHover(-1);
        }

        if (_dragRoot.Children.Count == 0)
        {
            return;
        }

        if (_theme?.Animate == true)
        {
            var fade = Shapes.Fade(_c, 0f, FluentMotion.Exit, decelerate: false);
            fade.DelayTime = TimeSpan.FromMilliseconds(120);
            _dragRoot.StartAnimation("Opacity", fade);
        }
        else
        {
            _dragRoot.Opacity = 0f;
        }

        _placeholders.Clear();
    }

    public void Dispose()
    {
        Instances.Remove(InputHwnd);
        try
        {
            _target.Root = null;
            _target.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn($"Divider layer dispose: {ex.Message}");
        }

        DestroyWindow(InputHwnd);
        DestroyWindow(OverlayHwnd);
    }

    /// <summary>Test probes: show a divider's hover bar without a pointer (-1 hides it).</summary>
    public void ShowBar(int index) => SetHover(index);

    /// <summary>A drag that did not start with the pointer on the bar (probes): show and move that divider's bar too.</summary>
    public void MarkDragging(Divider divider)
    {
        if (_dragging >= 0)
        {
            return;
        }

        int index = _bars.FindIndex(b => b.Handle.Divider == divider);
        if (index >= 0)
        {
            SetHover(index);
            _dragging = index;
            _pointerless = true;
        }
    }

    // ------------------------------------------------------------------ visuals

    private ContainerVisual CreateBar(DividerHandle handle, FluentTheme theme, float scale)
    {
        var r = handle.Bar.Offset(-_bounds.Left, -_bounds.Top);
        var size = new Vector2(r.Width, r.Height);
        bool vertical = handle.Divider.Orientation == DividerOrientation.Vertical;
        float thickness = vertical ? size.X : size.Y;
        float radius = MathF.Min(thickness / 2f, 4 * scale);

        var bar = _materials.Fill(theme.Divider, new Vector2(r.Left, r.Top), size, radius);
        var (stroke, _, _) = Shapes.RoundedRect(_c, Vector2.Zero, size, radius, null, theme.DividerStroke, MathF.Max(1, MathF.Round(scale)));
        bar.Children.InsertAtTop(stroke);

        // The grip: a short rounded line in the middle of the bar, along it (Windows' snap divider).
        float gripLength = MathF.Round(36 * scale);
        float gripThickness = MathF.Max(3, MathF.Round(4 * scale));
        var gripSize = vertical ? new Vector2(gripThickness, gripLength) : new Vector2(gripLength, gripThickness);
        var (grip, _, _) = Shapes.RoundedRect(_c, (size - gripSize) / 2f, gripSize, gripThickness / 2f, theme.Grip, null);
        bar.Children.InsertAtTop(grip);
        bar.Opacity = 0f;
        return bar;
    }

    private void SetHover(int index)
    {
        if (index == _hovered || _dragging >= 0)
        {
            return;
        }

        if (_hovered >= 0 && _hovered < _bars.Count)
        {
            Animate(_bars[_hovered].Bar, 0f);
        }

        _hovered = index;
        if (index >= 0 && index < _bars.Count)
        {
            Animate(_bars[index].Bar, 1f);
        }
    }

    private void Animate(Visual visual, float opacity)
    {
        if (_theme?.Animate == true)
        {
            visual.StartAnimation("Opacity", Shapes.Fade(_c, opacity, FluentMotion.Exit));
        }
        else
        {
            visual.Opacity = opacity;
        }
    }

    private int HitTest(PointI screen)
    {
        for (int i = 0; i < _bars.Count; i++)
        {
            if (_bars[i].Handle.HitRect.Contains(screen))
            {
                return i;
            }
        }

        return -1;
    }

    private PointI ToScreen(nint lParam) => new(_bounds.Left + (short)(lParam & 0xFFFF), _bounds.Top + (short)((lParam >> 16) & 0xFFFF));

    // ------------------------------------------------------------------ input window

    private static void EnsureClasses()
    {
        if (_classesRegistered)
        {
            return;
        }

        fixed (char* input = InputClass)
        fixed (char* overlay = OverlayClass)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &InputProc,
                hInstance = GetModuleHandle(null),
                lpszClassName = input,
            };
            RegisterClassExW(&wc);
            wc.lpfnWndProc = &OverlayProc;
            wc.lpszClassName = overlay;
            RegisterClassExW(&wc);
        }

        _classesRegistered = true;
    }

    [UnmanagedCallersOnly]
    private static nint OverlayProc(nint hwnd, uint msg, nuint wParam, nint lParam) => msg switch
    {
        WM_MOUSEACTIVATE => MA_NOACTIVATE,
        WM_NCHITTEST => HTTRANSPARENT,
        WM_ERASEBKGND => 1,
        WM_DPICHANGED => 0,
        _ => DefWindowProc(hwnd, msg, wParam, lParam),
    };

    [UnmanagedCallersOnly]
    private static nint InputProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (!Instances.TryGetValue(hwnd, out var layer))
        {
            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        try
        {
            switch (msg)
            {
                case WM_MOUSEACTIVATE:
                    return MA_NOACTIVATE;
                case WM_NCHITTEST:
                    return HTCLIENT; // the window region already limits this to the gaps
                case WM_ERASEBKGND:
                    return 1;
                case WM_DPICHANGED:
                    return 0;
                case WM_SETCURSOR:
                    int index = layer._dragging >= 0 ? layer._dragging : layer._hovered;
                    SetCursor(index < 0 || index >= layer._bars.Count ? CursorArrow
                        : layer._bars[index].Handle.Divider.Orientation == DividerOrientation.Vertical ? CursorWE : CursorNS);
                    return 1;
                case WM_MOUSEMOVE:
                    if (!layer._tracking)
                    {
                        var track = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = TME_LEAVE, hwndTrack = hwnd };
                        TrackMouseEvent(&track);
                        layer._tracking = true;
                    }

                    var point = layer.ToScreen(lParam);
                    if (layer._dragging >= 0)
                    {
                        layer._owner.DragDivider(layer.Group, point);
                    }
                    else
                    {
                        layer.SetHover(layer.HitTest(point));
                    }

                    return 0;
                case WM_MOUSELEAVE:
                    layer._tracking = false;
                    layer.SetHover(-1);
                    return 0;
                case WM_LBUTTONDOWN:
                    var down = layer.ToScreen(lParam);
                    int hit = layer.HitTest(down);
                    if (hit >= 0)
                    {
                        layer.SetHover(hit);
                        layer._dragging = hit;
                        SetCapture(hwnd);
                        layer._owner.BeginDividerDrag(layer.Group, layer._bars[hit].Handle, down);
                    }

                    return 0;
                case WM_LBUTTONUP:
                    if (layer._dragging >= 0)
                    {
                        ReleaseCapture(); // WM_CAPTURECHANGED ends the drag
                    }

                    return 0;
                case WM_CAPTURECHANGED:
                    if (layer._dragging >= 0)
                    {
                        layer._dragging = -1;
                        layer._owner.EndDividerDrag(layer.Group); // the refresh rebuilds the bars, hidden until hovered again
                    }

                    return 0;
                case WM_TIMER when wParam == DragTimerId:
                    layer._owner.DividerTimer(layer.Group);
                    return 0;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Divider message 0x{msg:X} failed", ex);
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }
}
