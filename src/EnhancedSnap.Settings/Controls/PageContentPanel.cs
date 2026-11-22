using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace EnhancedSnap.Settings.Controls;

/// <summary>
/// A page's column on the Content Layer (Suggestion02): the same space on the left and the right at every window
/// width, with the content stretched between them, like the WinUI 3 Gallery. <see cref="Gutter"/> on both sides, or
/// <see cref="NarrowGutter"/> once the content layer is narrower than <see cref="NarrowWidth"/>, so a narrow window
/// keeps room for the cards. It replaces a left-aligned column capped at 1000 epx, which gave wide windows a much
/// wider margin on the right than on the left.
/// </summary>
public sealed partial class PageContentPanel : Panel
{
    /// <summary>Space on each side (effective pixels).</summary>
    public const double Gutter = 40;

    /// <summary>Space on each side in a narrow window.</summary>
    public const double NarrowGutter = 24;

    /// <summary>Content-layer width below which the narrow gutter applies.</summary>
    public const double NarrowWidth = 640;

    /// <summary>Space below the last card.</summary>
    public const double BottomPadding = 40;

    public static double GutterFor(double width) => width < NarrowWidth ? NarrowGutter : Gutter;

    protected override Size MeasureOverride(Size availableSize)
    {
        bool unbounded = double.IsInfinity(availableSize.Width);
        double gutter = unbounded ? Gutter : GutterFor(availableSize.Width);
        double inner = unbounded ? double.PositiveInfinity : Math.Max(0, availableSize.Width - (2 * gutter));
        double width = 0;
        double height = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(inner, double.PositiveInfinity));
            width = Math.Max(width, child.DesiredSize.Width);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(unbounded ? width + (2 * gutter) : availableSize.Width, height + BottomPadding);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double gutter = GutterFor(finalSize.Width);
        var column = new Rect(gutter, 0, Math.Max(0, finalSize.Width - (2 * gutter)), Math.Max(0, finalSize.Height - BottomPadding));
        foreach (var child in Children)
        {
            child.Arrange(column);
        }

        return finalSize;
    }
}
