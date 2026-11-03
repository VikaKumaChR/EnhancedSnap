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
/// </summary>
internal abstract unsafe class PopupWindow : IDisposable
{
    private const string ClassName = "EnhancedSnap.Popup";
    private static readonly Dictionary<nint, PopupWindow> Instances = [];
    private static bool _classRegistered;

    private readonly DesktopWindowTarget _target;
    private readonly List<CompositionDrawingSurface> _surfaces = [];
    private bool _trackingLeave;

    protected PopupWindow(CompositionHost host)
    {
        EnsureClass();
        C = host.Compositor;
        Hwnd = CreateWindowEx(
            WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP,
            ClassName,
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
        SetDwmInt(Hwnd, DWMWA_USE_HOSTBACKDROPBRUSH, 1);
        SetDwmInt(Hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, 1);
        SetDwmInt(Hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
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
        BuildPanel(bounds.Width, bounds.Height);
        Build(bounds.Width, bounds.Height);

        if (theme.Animate && !IsVisible)
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

    protected virtual bool IsInteractive(PointI screen) => true;

    protected float Px(float epx) => MathF.Round(epx * Scale);

    /// <summary>Shows a drawing surface at its pixel size; the surface is closed with the content.</summary>
    protected void AddSurface(CompositionDrawingSurface? surface, Vector2 offset, Vector2 size)
    {
        if (surface is not null)
        {
            _surfaces.Add(surface);
        }

        Root.Children.InsertAtTop(Shapes.Surface(C, surface, offset, size));
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

    private static void EnsureClass()
    {
        if (_classRegistered)
        {
            return;
        }

        fixed (char* name = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                style = CS_DROPSHADOW,
                lpfnWndProc = &WndProc,
                hInstance = GetModuleHandle(null),
                hCursor = LoadCursor(0, IDC_ARROW),
                lpszClassName = name,
            };
            RegisterClassExW(&wc);
        }

        _classRegistered = true;
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
                    return HTCLIENT;
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
