using System.Globalization;
using System.Numerics;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Engine.Native;
using EnhancedSnap.Engine.Snapping;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Overlay;

/// <summary>
/// One click-through overlay window per monitor, covering the work area. Visual tree:
/// <code>
/// root (opacity: fade in/out)
///  ├─ tiles: one rounded tile per zone = [host-backdrop blur] + tint/stroke shape
///  └─ highlight: rounded drop target that glides between zones (offset/size animations)
/// </code>
/// The tiles use the same corner radius as the snapped window, so the window lands exactly on its tile.
/// </summary>
internal sealed unsafe class ZoneOverlay : IDisposable
{
    private const string ClassName = "EnhancedSnap.Overlay";
    private const uint LWA_ALPHA = 0x2;
    private static bool _classRegistered;

    private readonly Compositor _c;
    private readonly DesktopWindowTarget _target;
    private readonly ContainerVisual _root;
    private readonly ContainerVisual _tiles;
    private readonly ContainerVisual _highlight;
    private readonly SpriteVisual _highlightBackdrop;
    private readonly CompositionRoundedRectangleGeometry _highlightClip;
    private readonly CompositionRoundedRectangleGeometry _highlightBaseGeometry;
    private readonly CompositionRoundedRectangleGeometry _highlightAccentGeometry;
    private readonly CompositionSpriteShape _highlightBase;
    private readonly CompositionSpriteShape _highlightAccent;
    private OverlayPalette _palette;
    private bool _neutral;
    private bool _highlightVisible;
    private int _hideGeneration;
    private bool _topmost;

    public ZoneOverlay(CompositionHost host, MonitorZones monitor, OverlayPalette palette)
    {
        EnsureWindowClass();
        _c = host.Compositor;
        Monitor = monitor;
        _palette = palette;

        var work = monitor.Display.WorkArea;
        Hwnd = CreateWindowEx(
            WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TRANSPARENT,
            ClassName,
            "Enhanced Snap zones",
            WS_POPUP,
            work.Left,
            work.Top,
            work.Width,
            work.Height,
            0,
            0,
            GetModuleHandle(null),
            0);
        if (Hwnd == 0)
        {
            throw new InvalidOperationException("CreateWindowEx failed for the zones overlay.");
        }

        SetLayeredWindowAttributes(Hwnd, 0, 255, LWA_ALPHA);
        SetDwmInt(Hwnd, DWMWA_USE_HOSTBACKDROPBRUSH, 1);       // allows Compositor.CreateHostBackdropBrush (acrylic)
        SetDwmInt(Hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, 1);   // our own Fluent motion, no DWM show/hide transition
        SetDwmInt(Hwnd, DWMWA_EXCLUDED_FROM_PEEK, 1);
        SetDwmInt(Hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_DONOTROUND);

        _target = host.CreateDesktopWindowTarget(Hwnd, isTopmost: true);
        _root = _c.CreateContainerVisual();
        _root.Opacity = 0f;
        _target.Root = _root;

        _tiles = _c.CreateContainerVisual();
        _tiles.RelativeSizeAdjustment = Vector2.One;
        _root.Children.InsertAtTop(_tiles);

        // Drop-target highlight. Child geometries follow the container size through expression animations,
        // so a single Offset/Size animation morphs the whole highlight on the compositor thread.
        _highlight = _c.CreateContainerVisual();
        _highlight.Opacity = 0f;
        _highlightClip = _c.CreateRoundedRectangleGeometry();
        _highlight.Clip = _c.CreateGeometricClip(_highlightClip);
        BindSize(_highlightClip, inset: 0);

        _highlightBackdrop = _c.CreateSpriteVisual();
        _highlightBackdrop.RelativeSizeAdjustment = Vector2.One;
        _highlight.Children.InsertAtTop(_highlightBackdrop);

        var shapes = _c.CreateShapeVisual();
        shapes.RelativeSizeAdjustment = Vector2.One;
        _highlightBaseGeometry = _c.CreateRoundedRectangleGeometry();
        _highlightAccentGeometry = _c.CreateRoundedRectangleGeometry();
        _highlightBase = _c.CreateSpriteShape(_highlightBaseGeometry);
        _highlightAccent = _c.CreateSpriteShape(_highlightAccentGeometry);
        shapes.Shapes.Add(_highlightBase);
        shapes.Shapes.Add(_highlightAccent);
        _highlight.Children.InsertAtTop(shapes);
        _root.Children.InsertAtTop(_highlight);

        Update(monitor, palette);
    }

    public nint Hwnd { get; }

    public MonitorZones Monitor { get; private set; }

    public bool IsShown { get; private set; }

    private float Scale => (float)Monitor.Display.Scale;

    // UPDATE: Zone numbers / the dragged app's icon in the drop target. Needs text and bitmaps in the visual tree:
    // CompositionGraphicsDevice + Direct2D/DirectWrite drawing surfaces (ICompositionDrawingSurfaceInterop).

    /// <summary>Re-create tiles for new zones, palette or DPI. Cheap: a handful of visuals.</summary>
    public void Update(MonitorZones monitor, OverlayPalette palette)
    {
        Monitor = monitor;
        _palette = palette;
        var work = monitor.Display.WorkArea;
        SetWindowPos(Hwnd, 0, work.Left, work.Top, work.Width, work.Height, SWP_NOACTIVATE | SWP_NOZORDER);
        _root.Size = new Vector2(work.Width, work.Height);

        float radius = palette.CornerRadiusEpx * Scale;
        float stroke = MathF.Max(1f, MathF.Round(palette.TileStrokeEpx * Scale));
        var fill = _c.CreateColorBrush(palette.TileFill);
        var strokeBrush = _c.CreateColorBrush(palette.TileStroke);
        CompositionBrush? backdrop = palette.UseAcrylic ? _c.CreateHostBackdropBrush() : null;

        _tiles.Children.RemoveAll();
        foreach (var zone in monitor.Zones)
        {
            var local = zone.Offset(-work.Left, -work.Top);
            var size = new Vector2(local.Width, local.Height);

            var tile = _c.CreateContainerVisual();
            tile.Offset = new Vector3(local.Left, local.Top, 0f);
            tile.Size = size;
            var clip = _c.CreateRoundedRectangleGeometry();
            clip.Size = size;
            clip.CornerRadius = new Vector2(radius);
            tile.Clip = _c.CreateGeometricClip(clip);

            if (backdrop is not null)
            {
                var blur = _c.CreateSpriteVisual();
                blur.Brush = backdrop;
                blur.RelativeSizeAdjustment = Vector2.One;
                tile.Children.InsertAtTop(blur);
            }

            var shapeVisual = _c.CreateShapeVisual();
            shapeVisual.RelativeSizeAdjustment = Vector2.One;
            var geometry = _c.CreateRoundedRectangleGeometry();
            geometry.Offset = new Vector2(stroke / 2f);
            geometry.Size = size - new Vector2(stroke);
            geometry.CornerRadius = new Vector2(MathF.Max(0f, radius - (stroke / 2f)));
            var shape = _c.CreateSpriteShape(geometry);
            shape.FillBrush = fill;
            shape.StrokeBrush = strokeBrush;
            shape.StrokeThickness = stroke;
            shapeVisual.Shapes.Add(shape);
            tile.Children.InsertAtTop(shapeVisual);

            _tiles.Children.InsertAtTop(tile);
        }

        // Highlight styling.
        float targetStroke = MathF.Max(1f, MathF.Round(palette.TargetStrokeEpx * Scale));
        _highlightClip.CornerRadius = new Vector2(radius);
        _highlightBackdrop.Brush = backdrop;
        _highlightBase.FillBrush = fill;
        ApplyTargetStyle();
        _highlightAccent.StrokeThickness = targetStroke;
        _highlightBaseGeometry.CornerRadius = new Vector2(radius);
        _highlightAccentGeometry.Offset = new Vector2(targetStroke / 2f);
        _highlightAccentGeometry.CornerRadius = new Vector2(MathF.Max(0f, radius - (targetStroke / 2f)));
        BindSize(_highlightBaseGeometry, inset: 0);
        BindSize(_highlightAccentGeometry, inset: targetStroke);
    }

    /// <summary>Zone tiles on (zones mode) or off (edge-snap / flyout preview: only the drop target shows).</summary>
    public void SetTilesVisible(bool visible) => _tiles.Opacity = visible ? 1f : 0f;

    /// <summary>Neutral frosted preview (Windows Snap gestures) or the accent drop target (Shift + drag zones).</summary>
    public void SetNeutralTarget(bool neutral)
    {
        if (neutral != _neutral)
        {
            _neutral = neutral;
            ApplyTargetStyle();
        }
    }

    private void ApplyTargetStyle()
    {
        _highlightAccent.FillBrush = _c.CreateColorBrush(_neutral ? _palette.PreviewFill : _palette.TargetFill);
        _highlightAccent.StrokeBrush = _c.CreateColorBrush(_neutral ? _palette.PreviewStroke : _palette.TargetStroke);
    }

    /// <summary>Show below <paramref name="insertAfter"/> (the dragged window) or topmost for previews.</summary>
    public void Show(nint insertAfter, bool animate)
    {
        _hideGeneration++;
        var work = Monitor.Display.WorkArea;
        nint after = insertAfter != 0 ? insertAfter : HWND_TOPMOST;
        _topmost = insertAfter == 0;
        SetWindowPos(Hwnd, after, work.Left, work.Top, work.Width, work.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);

        if (animate && !IsShown)
        {
            _root.StartAnimation("Opacity", Scalar(1f, FluentMotion.Enter, FluentMotion.Decelerate));
        }
        else
        {
            _root.StopAnimation("Opacity");
            _root.Opacity = 1f;
        }

        IsShown = true;
    }

    public void Hide(bool animate)
    {
        if (!IsShown)
        {
            return;
        }

        IsShown = false;
        int generation = ++_hideGeneration;
        if (!animate)
        {
            _root.StopAnimation("Opacity");
            _root.Opacity = 0f;
            HideWindow();
            return;
        }

        var batch = _c.CreateScopedBatch(CompositionBatchTypes.Animation);
        _root.StartAnimation("Opacity", Scalar(0f, FluentMotion.Exit, FluentMotion.Accelerate));
        batch.End();
        batch.Completed += (_, _) =>
        {
            if (generation == _hideGeneration)
            {
                HideWindow();
            }
        };
    }

    /// <summary>Move the drop-target highlight (screen coordinates), or fade it out with null.</summary>
    public void SetTarget(RectI? target, bool animate)
    {
        if (target is null)
        {
            if (_highlightVisible)
            {
                _highlightVisible = false;
                if (animate)
                {
                    _highlight.StartAnimation("Opacity", Scalar(0f, FluentMotion.Exit, FluentMotion.Accelerate));
                }
                else
                {
                    _highlight.StopAnimation("Opacity");
                    _highlight.Opacity = 0f;
                }
            }

            return;
        }

        var work = Monitor.Display.WorkArea;
        var local = target.Value.Offset(-work.Left, -work.Top);
        var offset = new Vector3(local.Left, local.Top, 0f);
        var size = new Vector2(local.Width, local.Height);

        if (!_highlightVisible || !animate)
        {
            _highlight.StopAnimation("Offset");
            _highlight.StopAnimation("Size");
            _highlight.Offset = offset;
            _highlight.Size = size;
            if (animate)
            {
                _highlight.StartAnimation("Opacity", Scalar(1f, FluentMotion.Enter, FluentMotion.Decelerate));
            }
            else
            {
                _highlight.StopAnimation("Opacity");
                _highlight.Opacity = 1f;
            }
        }
        else
        {
            var easing = _c.CreateCubicBezierEasingFunction(FluentMotion.Decelerate.C1, FluentMotion.Decelerate.C2);
            var move = _c.CreateVector3KeyFrameAnimation();
            move.InsertKeyFrame(1f, offset, easing);
            move.Duration = FluentMotion.Glide;
            var resize = _c.CreateVector2KeyFrameAnimation();
            resize.InsertKeyFrame(1f, size, easing);
            resize.Duration = FluentMotion.Glide;
            _highlight.StartAnimation("Offset", move);
            _highlight.StartAnimation("Size", resize);
            _highlight.StartAnimation("Opacity", Scalar(1f, FluentMotion.Glide, FluentMotion.Decelerate));
        }

        _highlightVisible = true;
    }

    public void Dispose()
    {
        try
        {
            _target.Root = null;
            _target.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn($"Overlay target dispose: {ex.Message}");
        }

        DestroyWindow(Hwnd);
    }

    private void HideWindow()
    {
        ShowWindow(Hwnd, SW_HIDE);
        if (_topmost)
        {
            // Drop topmost while hidden so the next drag can sit right under the dragged window.
            SetWindowPos(Hwnd, -2 /* HWND_NOTOPMOST */, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            _topmost = false;
        }

        _highlight.StopAnimation("Opacity");
        _highlight.Opacity = 0f;
        _highlightVisible = false;
    }

    private ScalarKeyFrameAnimation Scalar(float to, TimeSpan duration, (Vector2 C1, Vector2 C2) curve)
    {
        var animation = _c.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1f, to, _c.CreateCubicBezierEasingFunction(curve.C1, curve.C2));
        animation.Duration = duration;
        return animation;
    }

    private void BindSize(CompositionRoundedRectangleGeometry geometry, float inset)
    {
        string expression = inset > 0
            ? string.Create(CultureInfo.InvariantCulture, $"host.Size - Vector2({inset}, {inset})")
            : "host.Size";
        var animation = _c.CreateExpressionAnimation(expression);
        animation.SetReferenceParameter("host", _highlight);
        geometry.StartAnimation("Size", animation);
    }

    private static void EnsureWindowClass()
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
                lpfnWndProc = &WndProc,
                hInstance = GetModuleHandle(null),
                lpszClassName = name,
            };
            RegisterClassExW(&wc);
        }

        _classRegistered = true;
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                return HTTRANSPARENT;
            case WM_MOUSEACTIVATE:
                return MA_NOACTIVATE;
            case WM_ERASEBKGND:
                return 1;
            case WM_DPICHANGED:
                return 0; // Geometry is managed explicitly in physical pixels.
            default:
                return DefWindowProc(hwnd, msg, wParam, lParam);
        }
    }
}
