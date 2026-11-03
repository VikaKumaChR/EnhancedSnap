using EnhancedSnap.Core.Layouts;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace EnhancedSnap.Settings.Controls;

/// <summary>
/// Draws a layout as rounded zones with Fluent spacing. Two looks: gallery thumbnails (flat zones) and a
/// realistic mini desktop with "windows" and a taskbar (<see cref="ShowWindows"/>) for the Appearance preview.
/// <para>
/// The drawing is built once at a fixed logical size with the monitor's aspect ratio and scaled by a Viewbox,
/// so resizing the window only changes a GPU transform — the preview tracks the window size frame by frame
/// instead of being rebuilt on every SizeChanged (the lag/clipping seen in Prob05).
/// </para>
/// </summary>
public sealed partial class LayoutPreview : UserControl
{
    private const double LogicalWidth = 480;
    private static readonly UISettings UiSettings = new();
    private readonly Canvas _canvas = new();
    private LayoutDefinition? _layout;
    private double _aspect = 16.0 / 10.0;

    public LayoutPreview()
    {
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = _canvas };
        IsHitTestVisible = false;
        ActualThemeChanged += (_, _) => Render();
    }

    public LayoutDefinition? Layout
    {
        get => _layout;
        set
        {
            _layout = value;
            Render();
        }
    }

    /// <summary>Width / height of the represented work area (keeps zones undistorted at any control size).</summary>
    public double AspectRatio
    {
        get => _aspect;
        set
        {
            _aspect = Math.Clamp(value, 0.4, 4.0);
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

    private void Render()
    {
        _canvas.Children.Clear();
        double width = LogicalWidth;
        double height = Math.Round(LogicalWidth / _aspect);
        _canvas.Width = width;
        _canvas.Height = height;
        if (_layout is null)
        {
            return;
        }

        bool dark = ActualTheme == ElementTheme.Dark;
        double k = width / ReferenceWidthEpx;
        double workHeight = height;

        if (ShowWindows)
        {
            DrawDesktop(width, height, dark, k, out workHeight);
        }

        double gap = GapEpx <= 0 ? 0 : Math.Max(1.5, GapEpx * k);
        double margin = MarginEpx <= 0 ? 0 : Math.Max(1.5, MarginEpx * k);
        double innerW = width - (2 * margin);
        double innerH = workHeight - (2 * margin);
        double spanX = innerW + gap;
        double spanY = innerH + gap;

        var fill = new SolidColorBrush(dark ? Color.FromArgb(255, 58, 58, 58) : Colors.White);
        var stroke = new SolidColorBrush(dark ? Color.FromArgb(48, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0));
        foreach (var zone in _layout.Zones)
        {
            double left = margin + (zone.X * spanX);
            double top = margin + (zone.Y * spanY);
            double w = Math.Max(1, (zone.Right * spanX) - gap - (zone.X * spanX));
            double h = Math.Max(1, (zone.Bottom * spanY) - gap - (zone.Y * spanY));
            double radius = Math.Min(Math.Max(CornerRadiusEpx > 0 ? 2 : 0, CornerRadiusEpx * k * (ShowWindows ? 1 : 1.6)), Math.Min(w, h) / 4);

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

    private void DrawDesktop(double width, double height, bool dark, double k, out double workHeight)
    {
        Color accent = Safe(UIColorType.Accent, Color.FromArgb(255, 0, 120, 212));
        Color soft = Safe(dark ? UIColorType.AccentDark2 : UIColorType.AccentLight3, Color.FromArgb(255, 153, 235, 255));

        var wallpaper = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1) };
        wallpaper.GradientStops.Add(new GradientStop { Color = soft, Offset = 0 });
        wallpaper.GradientStops.Add(new GradientStop { Color = Blend(soft, accent, 0.55), Offset = 1 });
        _canvas.Children.Add(new Rectangle { Width = width, Height = height, Fill = wallpaper });

        // Taskbar: 48 epx, so the margin above it reads correctly.
        double taskbar = Math.Clamp(48 * k, 10, 30);
        workHeight = height - taskbar;
        var bar = new Rectangle
        {
            Width = width,
            Height = taskbar,
            Fill = new SolidColorBrush(dark ? Color.FromArgb(235, 32, 32, 32) : Color.FromArgb(235, 243, 243, 243)),
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
                Fill = new SolidColorBrush(i == 0 ? accent : (dark ? Color.FromArgb(120, 255, 255, 255) : Color.FromArgb(90, 0, 0, 0))),
            };
            Canvas.SetLeft(glyph, (width / 2) + ((i - 2) * icon * 1.9) - (icon / 2));
            Canvas.SetTop(glyph, workHeight + ((taskbar - icon) / 2));
            _canvas.Children.Add(glyph);
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
}
