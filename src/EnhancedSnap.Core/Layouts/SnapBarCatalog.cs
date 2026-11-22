using EnhancedSnap.Core.Settings;
using static EnhancedSnap.Core.Layouts.LayoutCatalog;

namespace EnhancedSnap.Core.Layouts;

/// <summary>One set of layouts in the snap bar's pop down: a tab.</summary>
public sealed record SnapBarGroup(SnapBarSet Set, IReadOnlyList<LayoutDefinition> Layouts);

/// <summary>
/// What the snap bar's pop down offers when a window is dragged to the top of the screen (suggestion00): the classic
/// set (default snap: what Windows Snap offers for the screen), the horizontal set (top and bottom splits) and the
/// user's custom set (customization), each one tab. Every layout is fractions of the work area, so a set fits any
/// resolution, DPI and orientation. Pure, unit tested.
/// </summary>
public static class SnapBarCatalog
{
    /// <summary>Most cards in one row of the pop down (Windows' own shows up to six).</summary>
    public const int MaxColumns = 6;

    /// <summary>The horizontal set: halves top and bottom, two-thirds on top, a main window above two, three rows.</summary>
    public static IReadOnlyList<LayoutDefinition> Horizontal { get; } =
        [.. new[] { Rows, TopTwoThirds, TopMain, ThreeRows }.Select(static id => FindBuiltIn(id)!)];

    /// <summary>
    /// The classic set, what Windows Snap offers for the screen: four layouts under 1900 effective pixels wide (halves,
    /// two-thirds and a third, one and two stacked, quarters), six on wider screens; ultrawide and portrait screens get
    /// their own. The monitor's own layout comes first when it isn't one of them (in place of the last).
    /// </summary>
    public static List<LayoutDefinition> Classic(int workWidth, int workHeight, double scale, LayoutDefinition? monitorLayout = null)
    {
        double aspect = workWidth / (double)Math.Max(1, workHeight);
        double effectiveWidth = workWidth / Math.Max(0.5, scale);
        string[] ids = aspect >= 2.1
            ? [CenterFocus, Thirds, Halves, MainStack, Quarters, Grid3x2]
            : aspect < 1
                ? [Rows, Halves, Quarters]
                : effectiveWidth >= 1900
                    ? [Halves, LeftTwoThirds, Thirds, HalfStack, Quarters, CenterFocus]
                    : [Halves, LeftTwoThirds, HalfStack, Quarters];

        var layouts = ids.Select(static id => FindBuiltIn(id)!).ToList();
        if (monitorLayout is { Zones.Count: > 1 } own && layouts.All(l => l.Id != own.Id))
        {
            layouts.Insert(0, own);
            layouts.RemoveAt(layouts.Count - 1);
        }

        return layouts;
    }

    /// <summary>The custom set: the user's layouts in their order, without any that no longer exist.</summary>
    public static List<LayoutDefinition> Custom(AppSettings settings) =>
        [.. settings.SnapBarLayouts.CustomLayoutIds
            .Select(id => Resolve(settings, id))
            .OfType<LayoutDefinition>()
            .Where(static l => l.Id != Full)
            .Take(SnapBarSettings.MaxCustomLayouts)];

    /// <summary>
    /// The sets the pop down offers, in tab order: the classic set always, the horizontal set unless it is turned off,
    /// the custom set once it has a layout.
    /// </summary>
    public static List<SnapBarGroup> Groups(AppSettings settings, int workWidth, int workHeight, double scale, LayoutDefinition? monitorLayout = null)
    {
        var groups = new List<SnapBarGroup> { new(SnapBarSet.Classic, Classic(workWidth, workHeight, scale, monitorLayout)) };
        if (settings.SnapBarLayouts.ShowHorizontal)
        {
            groups.Add(new SnapBarGroup(SnapBarSet.Horizontal, Horizontal));
        }

        var custom = Custom(settings);
        if (custom.Count > 0)
        {
            groups.Add(new SnapBarGroup(SnapBarSet.Custom, custom));
        }

        return groups;
    }

    /// <summary>The tab to open: the wanted set when it is offered, else the classic set (the first).</summary>
    public static int IndexOf(IReadOnlyList<SnapBarGroup> groups, SnapBarSet wanted)
    {
        for (int i = 0; i < groups.Count; i++)
        {
            if (groups[i].Set == wanted)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>How many cards of <paramref name="cardEpx"/> fit side by side in <paramref name="availableEpx"/> (1 to <see cref="MaxColumns"/>).</summary>
    public static int FitColumns(double availableEpx, double cardEpx, double spacingEpx) =>
        Math.Clamp((int)Math.Floor((availableEpx + spacingEpx) / (cardEpx + spacingEpx)), 1, MaxColumns);

    /// <summary>
    /// Cards per row and rows for <paramref name="count"/> layouts: no more than fit, in as few rows as possible, the
    /// rows filled evenly (seven in rows of six give four and three, not six and one).
    /// </summary>
    public static (int Columns, int Rows) Grid(int count, int fitColumns)
    {
        if (count <= 0)
        {
            return (0, 0);
        }

        int max = Math.Clamp(fitColumns, 1, MaxColumns);
        int rows = (int)Math.Ceiling(count / (double)max);
        return ((int)Math.Ceiling(count / (double)rows), rows);
    }
}
