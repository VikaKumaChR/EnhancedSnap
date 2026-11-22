using System.Numerics;
using System.Runtime.InteropServices;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Engine.Native;
using EnhancedSnap.Engine.Overlay;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Ui;

/// <summary>
/// Base for Enhanced Snap's transient Fluent surfaces (snap layouts flyout, Snap Assist, groups flyout):
/// a non-activating topmost popup whose corners and shadow are drawn by DWM (DWMWCP_ROUND + CS_DROPSHADOW),
/// with an acrylic (host backdrop + tint) or solid panel rendered by the compositor.
/// <para>
/// A canvas popup (<c>canvas: true</c>) is a transparent, unrounded, shadowless window instead: the subclass draws its
/// own panel, shadow included, and can move it inside the window (the snap bar's pop down slides in from the top).
/// </para>
/// </summary>
internal abstract unsafe class PopupWindow : IDisposable
{
    private const string ClassName = "EnhancedSnap.Popup";
    private const string CanvasClassName = "EnhancedSnap.PopupCanvas";
    private static readonly Dictionary<nint, PopupWindow> Instances = [];
    private static bool _classRegistered;
    private static bool _canvasClassRegistered;

    private readonly DesktopWindowTarget _target;
    private readonly List<CompositionDrawingSurface> _surfaces = [];
    private readonly bool _canvas;
    private bool _trackingLeave;

    protected PopupWindow(CompositionHost host, bool canvas = false)
    {
        _canvas = canvas;
        EnsureClass(canvas);
        C = host.Compositor;
        // A canvas takes no input (the snap bar's pop down is driven by the dragged pointer, which the drag captures), so
        // it is click-through like the zones overlay: its transparent margins and its exit never catch a click.
        Hwnd = CreateWindowEx(
            WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP | (canvas ? WS_EX_LAYERED | WS_EX_TRANSPARENT : 0),
            canvas ? CanvasClassName : ClassName,
            "Enhanced Snap",
            WS_POPUP,
            0,
            0,
            1,
            1,
            0,
            0,
            GetModuleHandle(null),
            0);
        if (Hwnd == 0)
        {
            throw new InvalidOperationException("CreateWindowEx failed for a popup");
        }

        Instances[Hwnd] = this;
        if (canvas)
        {
            SetLayeredWindowAttributes(Hwnd, 0, 255, LWA_ALPHA);
        }

        SetDwmInt(Hwnd, DWMWA_USE_HOSTBACKDROPBRUSH, 1);
        SetDwmInt(Hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, 1);
        SetDwmInt(Hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, canvas ? DWMWCP_DONOTROUND : DWMWCP_ROUND);
        SetDwmInt(Hwnd, DWMWA_EXCLUDED_FROM_PEEK, 1);

        _target = host.CreateDesktopWindowTarget(Hwnd, isTopmost: false);
        Root = C.CreateContainerVisual();
        _target.Root = Root;
    }

    public nint Hwnd { get; }

    public bool IsVisible { get; private set; }

    public RectI Bounds { get; private set; }

    protected Compositor C { get; }

    protected ContainerVisual Root { get; }

    protected FluentTheme Theme { get; private set; } = null!;

    protected float Scale { get; private set; } = 1f;

    /// <summary>Show at <paramref name="bounds"/> (screen pixels). Content is (re)built by <see cref="Build"/>.</summary>
    protected void ShowAt(RectI bounds, FluentTheme theme, float scale)
    {
        Theme = theme;
        Scale = scale;
        Bounds = bounds;
        SetDwmInt(Hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, theme.Dark ? 1 : 0);
        SetWindowPos(Hwnd, HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOACTIVATE);

        ClearContent();
        Root.Size = new Vector2(bounds.Width, bounds.Height);
        if (!_canvas)
        {
            BuildPanel(bounds.Width, bounds.Height);
        }

        Build(bounds.Width, bounds.Height);

        if (_canvas)
        {
            Root.Opacity = 1f; // the subclass moves and fades its own panel
            Root.Offset = Vector3.Zero;
        }
        else if (theme.Animate && !IsVisible)
        {
            Root.Opacity = 0f;
            Root.Offset = new Vector3(0, 8 * scale, 0);
            var slide = C.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(1f, Vector3.Zero, C.CreateCubicBezierEasingFunction(FluentMotion.Decelerate.C1, FluentMotion.Decelerate.C2));
            slide.Duration = FluentMotion.Enter;
            Root.StartAnimation("Offset", slide);
            Root.StartAnimation("Opacity", Shapes.Fade(C, 1f, FluentMotion.Enter));
        }
        else
        {
            Root.Opacity = 1f;
            Root.Offset = Vector3.Zero;
        }

        ShowWindow(Hwnd, SW_SHOWNOACTIVATE);
        IsVisible = true;
    }

    public virtual void Hide()
    {
        if (!IsVisible)
        {
            return;
        }

        IsVisible = false;
        ShowWindow(Hwnd, SW_HIDE);
        ClearContent(); // content is rebuilt on every show; keeping it would pin GPU surfaces (and their device)
        OnHidden();
    }

    public bool Contains(PointI screenPoint) => IsVisible && Bounds.Contains(screenPoint);

    public virtual void Dispose()
    {
        Instances.Remove(Hwnd);
        try
        {
            _target.Root = null;
            _target.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn($"Popup target dispose: {ex.Message}");
        }

        DestroyWindow(Hwnd);
    }

    /// <summary>Create the content visuals (panel background already added). Sizes are in pixels.</summary>
    protected abstract void Build(int width, int height);

    protected virtual void OnPointerMoved(PointI screen)
    {
    }

    protected virtual void OnPointerExited()
    {
    }

    protected virtual void OnPressed(PointI screen)
    {
    }

    protected virtual void OnReleased(PointI screen)
    {
    }

    protected virtual void OnHidden()
    {
    }

    /// <summary>A timer set on <see cref="Hwnd"/> fired.</summary>
    protected virtual void OnTimer(nuint id)
    {
    }

    protected virtual bool IsInteractive(PointI screen) => true;

    protected float Px(float epx) => MathF.Round(epx * Scale);

    /// <summary>Shows a drawing surface at its pixel size; the surface is closed with the content.</summary>
    protected void AddSurface(CompositionDrawingSurface? surface, Vector2 offset, Vector2 size) => AddSurfaceTo(Root, surface, offset, size);

    /// <summary>Like <see cref="AddSurface"/>, in <paramref name="parent"/>; returns the sprite showing it.</summary>
    protected SpriteVisual AddSurfaceTo(ContainerVisual parent, CompositionDrawingSurface? surface, Vector2 offset, Vector2 size)
    {
        if (surface is not null)
        {
            _surfaces.Add(surface);
        }

        var sprite = Shapes.Surface(C, surface, offset, size);
        parent.Children.InsertAtTop(sprite);
        return sprite;
    }

    /// <summary>
    /// Closes surfaces explicitly: their wrappers would otherwise hold the GPU memory until a garbage collection,
    /// which an engine that barely allocates may not see for hours.
    /// </summary>
    private void ClearContent()
    {
        Root.Children.RemoveAll();
        foreach (var surface in _surfaces)
        {
            surface.Dispose();
        }

        _surfaces.Clear();
    }

    private void BuildPanel(int width, int height)
    {
        if (Theme.Acrylic)
        {
            var backdrop = C.CreateSpriteVisual();
            backdrop.Brush = C.CreateHostBackdropBrush();
            backdrop.RelativeSizeAdjustment = Vector2.One;
            Root.Children.InsertAtTop(backdrop);
        }

        // DWM clips the window to an 8 px radius; the stroke follows the same curve.
        var (panel, _, _) = Shapes.RoundedRect(C, Vector2.Zero, new Vector2(width, height), Px(8), Theme.PanelTint, Theme.PanelStroke, MathF.Max(1, Px(1)));
        Root.Children.InsertAtTop(panel);
    }

    private static void EnsureClass(bool canvas)
    {
        if (canvas ? _canvasClassRegistered : _classRegistered)
        {
            return;
        }

        fixed (char* name = canvas ? CanvasClassName : ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                style = canvas ? 0 : CS_DROPSHADOW, // a canvas draws its own shadow, around its panel rather than the window
                lpfnWndProc = &WndProc,
                hInstance = GetModuleHandle(null),
                hCursor = LoadCursor(0, IDC_ARROW),
                lpszClassName = name,
            };
            RegisterClassExW(&wc);
        }

        if (canvas)
        {
            _canvasClassRegistered = true;
        }
        else
        {
            _classRegistered = true;
        }
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (!Instances.TryGetValue(hwnd, out var popup))
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
                    return popup._canvas ? HTTRANSPARENT : HTCLIENT;
                case WM_ERASEBKGND:
                    return 1;
                case WM_DPICHANGED:
                    return 0;
                case WM_MOUSEMOVE:
                    if (!popup._trackingLeave)
                    {
                        var track = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = TME_LEAVE, hwndTrack = hwnd };
                        TrackMouseEvent(&track);
                        popup._trackingLeave = true;
                    }

                    popup.OnPointerMoved(ToScreen(popup, lParam));
                    return 0;
                case WM_MOUSELEAVE:
                    popup._trackingLeave = false;
                    popup.OnPointerExited();
                    return 0;
                case WM_LBUTTONDOWN:
                    popup.OnPressed(ToScreen(popup, lParam));
                    return 0;
                case WM_LBUTTONUP:
                    popup.OnReleased(ToScreen(popup, lParam));
                    return 0;
                case WM_TIMER:
                    popup.OnTimer(wParam);
                    return 0;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Popup message 0x{msg:X} failed", ex);
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static PointI ToScreen(PopupWindow popup, nint lParam) =>
        new(popup.Bounds.Left + (short)(lParam & 0xFFFF), popup.Bounds.Top + (short)((lParam >> 16) & 0xFFFF));
}
