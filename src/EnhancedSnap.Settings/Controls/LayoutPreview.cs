using System.Numerics;
using System.Runtime.InteropServices;
using EnhancedSnap.Core.Layouts;
using EnhancedSnap.Settings.Services;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Win32;
using Windows.System.Power;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace EnhancedSnap.Settings.Controls;

/// <summary>
/// Draws a layout as rounded zones with Fluent spacing. Two looks: gallery thumbnails (flat zones) and a
/// realistic mini desktop with "windows" and a taskbar (<see cref="ShowWindows"/>) for the General and Appearance
/// previews — on the user's own wallpaper, like the display picture in Windows Settings → Personalization
/// (<see cref="WallpaperPreview"/>; an accent gradient until it is read or when it can't be). Its windows and taskbar
/// look the way Windows draws real ones right now: Mica only while Windows draws Mica see-through, and the taskbar in
/// the Windows mode.
/// <para>
/// The drawing is built once at a fixed logical size with the monitor's aspect ratio and scaled by a Viewbox,
/// so resizing the window only changes a GPU transform — the preview tracks the window size frame by frame
/// instead of being rebuilt on every SizeChanged (the lag/clipping seen in Prob05).
/// </para>
/// </summary>
public sealed partial class LayoutPreview : UserControl
{
    private const double LogicalWidth = 480;
    private const int SM_REMOTESESSION = 0x1000;
    private static readonly UISettings UiSettings = new();
    private static AccessibilitySettings? _accessibility;
    private readonly Canvas _canvas = new();
    private LayoutDefinition? _layout;
    private double _aspect = 16.0 / 10.0;
    private CompositionRoundedRectangleGeometry? _clip;
    private bool _listening;
    private AccessibilitySettings? _listeningTo;
    private int _renderQueued;

    public LayoutPreview()
    {
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = _canvas };
        IsHitTestVisible = false;
        ActualThemeChanged += (_, _) => Render();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => ApplyRoundedClip();

        // The empty 16:10 canvas, so a frame that hugs the picture has its size before the page sets the monitor's shape.
        Render();
    }

    /// <summary>
    /// Rounds the preview's corners (effective pixels) on the GPU, at any size — to sit inside a rounded border like
    /// the display picture in Windows Settings. 0 = square.
    /// </summary>
    public double ClipCornerRadius { get; set; }

    public LayoutDefinition? Layout
    {
        get => _layout;
        set
        {
            _layout = value;
            Render();
        }
    }

    /// <summary>
    /// Width / height of the represented work area (keeps zones undistorted at any control size). The desktop look
    /// (<see cref="ShowWindows"/>) keeps the monitor's true shape, the one the wallpaper's placement is computed for;
    /// gallery thumbnails and the editor draw it clamped to 0.4–4.
    /// </summary>
    public double AspectRatio
    {
        get => _aspect;
        set
        {
            _aspect = value > 0 && double.IsFinite(value) ? value : 16.0 / 10.0;
            Render();
        }
    }

    /// <summary>Width, in effective pixels, of the desktop this preview represents (sets the scale of gaps).</summary>
    public double ReferenceWidthEpx { get; set; } = 1280;

    public double GapEpx { get; set; } = 8;

    public double MarginEpx { get; set; } = 8;

    public double CornerRadiusEpx { get; set; } = 8;

    public bool ShowWindows { get; set; }

    public void Refresh() => Render();

    // The wallpaper and the system's look matter only to the desktop look; the gallery thumbnails stay flat. These events
    // are static or on shared objects and would keep the preview alive, so it listens only while it is loaded.
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!ShowWindows)
        {
            return;
        }

        if (!_listening)
        {
            _listening = true;
            WallpaperPreview.Changed += Render;
            UiSettings.AdvancedEffectsEnabledChanged += OnSystemLookChanged;
            UiSettings.ColorValuesChanged += OnSystemLookChanged;
            PowerManager.EnergySaverStatusChanged += OnSystemLookChanged;
            try
            {
                _accessibility ??= new AccessibilitySettings();
                _accessibility.HighContrastChanged += OnSystemLookChanged;
                _listeningTo = _accessibility;
            }
            catch (Exception)
            {
                // Not every app model gets this event. ColorValuesChanged also comes with high contrast, which changes the
                // system colors.
            }
        }

        WallpaperPreview.Refresh(DispatcherQueue);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_listening)
        {
            return;
        }

        _listening = false;
        WallpaperPreview.Changed -= Render;
        UiSettings.AdvancedEffectsEnabledChanged -= OnSystemLookChanged;
        UiSettings.ColorValuesChanged -= OnSystemLookChanged;
        PowerManager.EnergySaverStatusChanged -= OnSystemLookChanged;
        if (_listeningTo is not null)
        {
            _listeningTo.HighContrastChanged -= OnSystemLookChanged;
            _listeningTo = null;
        }
    }

    // Transparency, Energy saver, high contrast, the Windows mode or the accent changed. Raised on worker threads, often
    // several times for one change: one re-render on the UI thread.
    private void OnSystemLookChanged(object? sender, object args)
    {
        if (Interlocked.Exchange(ref _renderQueued, 1) == 0 && !DispatcherQueue.TryEnqueue(RenderQueued))
        {
            Interlocked.Exchange(ref _renderQueued, 0);
        }
    }

    private void RenderQueued()
    {
        Interlocked.Exchange(ref _renderQueued, 0);
        Render();
    }

    /// <summary>
    /// Rounds the picture's corners. The clip is the rectangle the Viewbox fits the canvas into (uniform, centred), so it
    /// follows the picture's shape even when the control is not the picture's size.
    /// </summary>
    private void ApplyRoundedClip()
    {
        if (ClipCornerRadius <= 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        if (_clip is null)
        {
            var visual = ElementCompositionPreview.GetElementVisual(this);
            _clip = visual.Compositor.CreateRoundedRectangleGeometry();
            visual.Clip = visual.Compositor.CreateGeometricClip(_clip);
        }

        double scale = Math.Min(ActualWidth / _canvas.Width, ActualHeight / _canvas.Height);
        double width = _canvas.Width * scale;
        double height = _canvas.Height * scale;
        _clip.Offset = new Vector2((float)((ActualWidth - width) / 2), (float)((ActualHeight - height) / 2));
        _clip.Size = new Vector2((float)width, (float)height);
        _clip.CornerRadius = new Vector2((float)ClipCornerRadius);
    }

    private void Render()
    {
        _canvas.Children.Clear();
        double aspect = ShowWindows ? _aspect : Math.Clamp(_aspect, 0.4, 4.0);
        double width = LogicalWidth;
        double height = Math.Max(1, Math.Round(LogicalWidth / aspect));
        _canvas.Width = width;
        _canvas.Height = height;
        _canvas.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, width, height) }; // Fill and Center reach past it
        ApplyRoundedClip(); // a new shape in a control that keeps its size raises no SizeChanged
        if (_layout is null)
        {
            return;
        }

        bool dark = ActualTheme == ElementTheme.Dark;
        double k = width / ReferenceWidthEpx;
        double workHeight = height;
        var desktop = ShowWindows ? WallpaperPreview.Current : null;
        bool translucent = ShowWindows && Translucent();

        if (ShowWindows)
        {
            DrawDesktop(desktop, width, height, dark, translucent, k, out workHeight);
        }

        double gap = GapEpx <= 0 ? 0 : Math.Max(1.5, GapEpx * k);
        double margin = MarginEpx <= 0 ? 0 : Math.Max(1.5, MarginEpx * k);
        double innerW = width - (2 * margin);
        double innerH = workHeight - (2 * margin);
        double spanX = innerW + gap;
        double spanY = innerH + gap;

        // Over the user's wallpaper the windows are Mica, as on a real Windows 11 desktop: the blurred wallpaper under a
        // light (or dark) tint, so they take on its colors. Without it, or while Windows draws Mica solid, plain cards.
        var behind = translucent ? desktop : null;
        var fill = new SolidColorBrush(behind is not null
            ? (dark ? Color.FromArgb(214, 40, 40, 40) : Color.FromArgb(204, 252, 252, 252))
            : (dark ? Color.FromArgb(255, 58, 58, 58) : Colors.White));
        var stroke = new SolidColorBrush(dark ? Color.FromArgb(48, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0));
        foreach (var zone in _layout.Zones)
        {
            double left = margin + (zone.X * spanX);
            double top = margin + (zone.Y * spanY);
            double w = Math.Max(1, (zone.Right * spanX) - gap - (zone.X * spanX));
            double h = Math.Max(1, (zone.Bottom * spanY) - gap - (zone.Y * spanY));
            double radius = Math.Min(Math.Max(CornerRadiusEpx > 0 ? 2 : 0, CornerRadiusEpx * k * (ShowWindows ? 1 : 1.6)), Math.Min(w, h) / 4);

            if (behind is not null)
            {
                var mica = new Rectangle { Width = w, Height = h, RadiusX = radius, RadiusY = radius, Fill = MicaBrush(behind, width, height, left, top, w, h) };
                Canvas.SetLeft(mica, left);
                Canvas.SetTop(mica, top);
                _canvas.Children.Add(mica);
            }

            var tile = new Border
            {
                Width = w,
                Height = h,
                CornerRadius = new CornerRadius(radius),
                Background = fill,
                BorderBrush = stroke,
                BorderThickness = new Thickness(1),
            };

            if (ShowWindows && h > 24)
            {
                tile.Child = MiniWindowChrome(w, h, dark);
            }

            Canvas.SetLeft(tile, left);
            Canvas.SetTop(tile, top);
            _canvas.Children.Add(tile);
        }
    }

    private void DrawDesktop(WallpaperPreview.Snapshot? desktop, double width, double height, bool dark, bool translucent, double k, out double workHeight)
    {
        Color accent = Safe(UIColorType.Accent, Color.FromArgb(255, 0, 120, 212));
        if (desktop is not null)
        {
            // The user's own desktop, fitted the way Windows fits it; its color shows around Fit and Center.
            _canvas.Children.Add(new Rectangle { Width = width, Height = height, Fill = new SolidColorBrush(desktop.Background) });
            if (desktop.Image is { } image && !desktop.Placement.IsEmpty)
            {
                var picture = new Image
                {
                    Source = image,
                    Stretch = Stretch.Fill,
                    Width = desktop.Placement.Width * width,
                    Height = desktop.Placement.Height * height,
                };
                AutomationProperties.SetAccessibilityView(picture, AccessibilityView.Raw); // decoration, not an unnamed image for screen readers
                Canvas.SetLeft(picture, desktop.Placement.X * width);
                Canvas.SetTop(picture, desktop.Placement.Y * height);
                _canvas.Children.Add(picture);
            }
        }
        else
        {
            Color soft = Safe(dark ? UIColorType.AccentDark2 : UIColorType.AccentLight3, Color.FromArgb(255, 153, 235, 255));
            var gradient = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1) };
            gradient.GradientStops.Add(new GradientStop { Color = soft, Offset = 0 });
            gradient.GradientStops.Add(new GradientStop { Color = Blend(soft, accent, 0.55), Offset = 1 });
            _canvas.Children.Add(new Rectangle { Width = width, Height = height, Fill = gradient });
        }

        // Taskbar: 48 epx, so the margin above it reads correctly. Like the real one it is tinted for the Windows mode
        // (the windows follow the app's), and Mica over the wallpaper only while Windows draws Mica see-through.
        double taskbar = Math.Clamp(48 * k, 10, 30);
        workHeight = height - taskbar;
        if (translucent && desktop is not null)
        {
            var mica = new Rectangle { Width = width, Height = taskbar, Fill = MicaBrush(desktop, width, height, 0, workHeight, width, taskbar) };
            Canvas.SetTop(mica, workHeight);
            _canvas.Children.Add(mica);
        }

        bool windowsDark = WindowsModeIsDark();
        byte barAlpha = !translucent ? (byte)255 : desktop is null ? (byte)235 : (byte)214;
        var bar = new Rectangle
        {
            Width = width,
            Height = taskbar,
            Fill = new SolidColorBrush(windowsDark ? Color.FromArgb(barAlpha, 32, 32, 32) : Color.FromArgb(barAlpha, 243, 243, 243)),
        };
        Canvas.SetTop(bar, workHeight);
        _canvas.Children.Add(bar);

        double icon = taskbar * 0.42;
        for (int i = 0; i < 5; i++)
        {
            var glyph = new Rectangle
            {
                Width = icon,
                Height = icon,
                RadiusX = icon / 4,
                RadiusY = icon / 4,
                Fill = new SolidColorBrush(i == 0 ? accent : (windowsDark ? Color.FromArgb(120, 255, 255, 255) : Color.FromArgb(90, 0, 0, 0))),
            };
            Canvas.SetLeft(glyph, (width / 2) + ((i - 2) * icon * 1.9) - (icon / 2));
            Canvas.SetTop(glyph, workHeight + ((taskbar - icon) / 2));
            _canvas.Children.Add(glyph);
        }
    }

    /// <summary>
    /// What Mica shows behind an element at (<paramref name="left"/>, <paramref name="top"/>) of the canvas: the part
    /// of the blurred whole-desktop backdrop under it (the solid desktop color when there is no picture).
    /// </summary>
    private static Brush MicaBrush(WallpaperPreview.Snapshot desktop, double canvasW, double canvasH, double left, double top, double w, double h) =>
        desktop.Backdrop is { } backdrop
            ? new ImageBrush
            {
                ImageSource = backdrop,
                Stretch = Stretch.Fill,
                Transform = new CompositeTransform { ScaleX = canvasW / w, ScaleY = canvasH / h, TranslateX = -left, TranslateY = -top },
            }
            : new SolidColorBrush(desktop.Background);

    /// <summary>
    /// Whether Windows draws Mica see-through right now, by the engine's rule (FluentTheme): transparency effects on,
    /// and not in Energy saver, high contrast or a remote session. Otherwise real windows and the taskbar are solid.
    /// </summary>
    private static bool Translucent() =>
        UiSettings.AdvancedEffectsEnabled && !EnergySaver() && !HighContrast() && GetSystemMetrics(SM_REMOTESESSION) == 0;

    /// <summary>Energy saver (or battery saver) is on; off when the power state can't be read, as on the Appearance page.</summary>
    private static bool EnergySaver()
    {
        try
        {
            return PowerManager.EnergySaverStatus == EnergySaverStatus.On;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>A high contrast theme is on; off when the setting can't be read.</summary>
    private static bool HighContrast()
    {
        try
        {
            return (_accessibility ??= new AccessibilitySettings()).HighContrast;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The Windows mode, which the real taskbar follows; windows follow the app mode (<see cref="FrameworkElement.ActualTheme"/>).
    /// No WinRT API reports it, so it is read where the engine's SystemState reads it.
    /// </summary>
    private static bool WindowsModeIsDark()
    {
        try
        {
            using var personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return personalize?.GetValue("SystemUsesLightTheme") is int light && light == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static Grid MiniWindowChrome(double width, double height, bool dark)
    {
        double bar = Math.Clamp(height * 0.1, 6, 14);
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(bar) });
        grid.RowDefinitions.Add(new RowDefinition());

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Spacing = bar * 0.35, Margin = new Thickness(0, 0, bar * 0.6, 0) };
        var dot = new SolidColorBrush(dark ? Color.FromArgb(110, 255, 255, 255) : Color.FromArgb(80, 0, 0, 0));
        for (int i = 0; i < 3; i++)
        {
            buttons.Children.Add(new Ellipse { Width = bar * 0.32, Height = bar * 0.32, Fill = dot });
        }

        grid.Children.Add(buttons);

        var lines = new StackPanel { Spacing = Math.Max(2, bar * 0.4), Margin = new Thickness(bar * 0.8, bar * 0.4, bar * 0.8, 0) };
        Grid.SetRow(lines, 1);
        var lineBrush = new SolidColorBrush(dark ? Color.FromArgb(28, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0));
        double lineHeight = Math.Max(2, bar * 0.35);
        for (int i = 0; i < 3 && (i + 1) * (lineHeight + bar * 0.4) < height - bar * 2; i++)
        {
            lines.Children.Add(new Rectangle { Height = lineHeight, Width = Math.Max(4, (width - bar * 1.6) * (i == 2 ? 0.5 : 0.85)), HorizontalAlignment = HorizontalAlignment.Left, RadiusX = lineHeight / 2, RadiusY = lineHeight / 2, Fill = lineBrush });
        }

        grid.Children.Add(lines);
        return grid;
    }

    private static Color Safe(UIColorType type, Color fallback)
    {
        try
        {
            return UiSettings.GetColorValue(type);
        }
        catch
        {
            return fallback;
        }
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        255,
        (byte)(a.R + ((b.R - a.R) * t)),
        (byte)(a.G + ((b.G - a.G) * t)),
        (byte)(a.B + ((b.B - a.B) * t)));

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int nIndex);
}
