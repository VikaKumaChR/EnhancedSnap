using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Core.Layouts;

namespace EnhancedSnap.Core.Tests;

public class GroupGeometryTests
{
    private static ZoneFrac[] Zones(string id) => [.. LayoutCatalog.FindBuiltIn(id)!.Zones];

    [Fact]
    public void Halves_have_one_full_height_divider()
    {
        var dividers = GroupGeometry.FindDividers(Zones(LayoutCatalog.Halves));
        var d = Assert.Single(dividers);
        Assert.Equal(DividerOrientation.Vertical, d.Orientation);
        Assert.Equal(0.5, d.Position, 4);
        Assert.Equal(0, d.Start, 4);
        Assert.Equal(1, d.End, 4);
    }

    [Fact]
    public void Quarters_share_full_length_dividers_between_small_windows()
    {
        var dividers = GroupGeometry.FindDividers(Zones(LayoutCatalog.Quarters));
        Assert.Equal(2, dividers.Count);
        Assert.Contains(dividers, d => d.Orientation == DividerOrientation.Vertical && d.Start == 0 && Math.Abs(d.End - 1) < 1e-4);
        Assert.Contains(dividers, d => d.Orientation == DividerOrientation.Horizontal && d.Start == 0 && Math.Abs(d.End - 1) < 1e-4);
    }

    [Fact]
    public void Main_and_stack_has_a_partial_horizontal_divider()
    {
        var dividers = GroupGeometry.FindDividers(Zones(LayoutCatalog.MainStack));
        var horizontal = Assert.Single(dividers, d => d.Orientation == DividerOrientation.Horizontal);
        Assert.Equal(0.6, horizontal.Start, 4);
        Assert.Equal(1.0, horizontal.End, 4);
    }

    [Fact]
    public void Moving_vertical_divider_resizes_both_sides()
    {
        var zones = Zones(LayoutCatalog.Halves);
        var moved = GroupGeometry.MoveDivider(zones, GroupGeometry.FindDividers(zones)[0], 0.3, 0.1);
        Assert.Equal(0.3, moved[0].W, 6);
        Assert.Equal(0.3, moved[1].X, 6);
        Assert.Equal(0.7, moved[1].W, 6);
    }

    [Fact]
    public void Moving_quarters_horizontal_divider_moves_all_four_windows()
    {
        var zones = Zones(LayoutCatalog.Quarters);
        var horizontal = GroupGeometry.FindDividers(zones).Single(d => d.Orientation == DividerOrientation.Horizontal);
        var moved = GroupGeometry.MoveDivider(zones, horizontal, 0.65, 0.1);
        Assert.All(new[] { moved[0], moved[1] }, z => Assert.Equal(0.65, z.H, 6));
        Assert.All(new[] { moved[2], moved[3] }, z => Assert.Equal(0.65, z.Y, 6));
    }

    [Fact]
    public void Main_and_stack_horizontal_divider_leaves_main_zone_alone()
    {
        var zones = Zones(LayoutCatalog.MainStack);
        var horizontal = GroupGeometry.FindDividers(zones).Single(d => d.Orientation == DividerOrientation.Horizontal);
        var moved = GroupGeometry.MoveDivider(zones, horizontal, 0.3, 0.1);
        Assert.Equal(zones[0], moved[0]);
        Assert.Equal(0.3, moved[1].H, 6);
        Assert.Equal(0.3, moved[2].Y, 6);
    }

    [Fact]
    public void Divider_is_clamped_by_minimum_size()
    {
        var zones = Zones(LayoutCatalog.Thirds);
        var first = GroupGeometry.FindDividers(zones).OrderBy(d => d.Position).First();
        var moved = GroupGeometry.MoveDivider(zones, first, 0.95, 0.1);
        // The middle third ends at 2/3, so the first divider stops at 2/3 - 0.1.
        Assert.Equal(2.0 / 3.0 - 0.1, moved[0].W, 6);
        Assert.True(moved[1].W >= 0.1 - 1e-9);
    }

    [Fact]
    public void Find_relocates_divider_after_move()
    {
        var zones = Zones(LayoutCatalog.Halves);
        var moved = GroupGeometry.MoveDivider(zones, GroupGeometry.FindDividers(zones)[0], 0.4, 0.1);
        var again = GroupGeometry.Find(moved, DividerOrientation.Vertical, 0.4, 0.5);
        Assert.NotNull(again);
        Assert.Equal(0.4, again!.Value.Position, 4);
    }

    [Fact]
    public void Arranged_rectangles_become_snapped_fractions()
    {
        var area = new RectI(0, 0, 2000, 1000);
        var left = GroupGeometry.ToFraction(new RectI(0, 0, 994, 1000), area, 12);
        var right = GroupGeometry.ToFraction(new RectI(1006, 0, 2000, 1000), area, 12);
        Assert.Equal(new ZoneFrac(0, 0, 0.5, 1), left);
        Assert.Equal(new ZoneFrac(0.5, 0, 0.5, 1), right);
    }

    [Fact]
    public void Gap_centre_and_fraction_are_inverse()
    {
        var (inner, gap) = ZoneMath.Inner(new RectI(0, 0, 2560, 1528), 1.5, 8, 8);
        double center = ZoneMath.FractionToGapCenter(0.5, inner.Left, inner.Width, gap);
        Assert.Equal(0.5, ZoneMath.PixelToFraction((int)Math.Round(center), inner.Left, inner.Width, gap), 3);
    }

    [Fact]
    public void Divider_range_keeps_each_side_at_its_minimum()
    {
        var zones = Zones(LayoutCatalog.Halves);
        var (low, high) = GroupGeometry.DividerRange(zones, GroupGeometry.FindDividers(zones)[0], [0.3, 0.2]);
        Assert.Equal(0.3, low, 6);
        Assert.Equal(0.8, high, 6);
    }

    [Fact]
    public void Divider_range_between_small_windows_uses_the_largest_minimum_on_each_side()
    {
        // Quarters: 0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right; the vertical divider spans all four.
        var zones = Zones(LayoutCatalog.Quarters);
        var vertical = GroupGeometry.FindDividers(zones).Single(d => d.Orientation == DividerOrientation.Vertical);
        double[] minimums = [0.2, 0.1, 0.35, 0.4];
        var (low, high) = GroupGeometry.DividerRange(zones, vertical, minimums);
        Assert.Equal(0.35, low, 6);
        Assert.Equal(0.6, high, 6);
    }

    [Fact]
    public void Divider_cannot_move_when_minimums_do_not_fit()
    {
        var zones = Zones(LayoutCatalog.Halves);
        var (low, high) = GroupGeometry.DividerRange(zones, GroupGeometry.FindDividers(zones)[0], [0.7, 0.6]);
        Assert.True(low > high);
    }

    [Theory]
    [InlineData(2560, 1528, 0, 382)]   // landscape: a quarter of the height
    [InlineData(1528, 2560, 0, 382)]   // portrait: a quarter of the width
    [InlineData(3440, 1400, 0, 350)]   // ultrawide: columns may get as narrow as ~10 % of the width
    [InlineData(2560, 1528, 500, 500)] // the app's own minimum wins when it is larger
    public void Minimum_extent_follows_the_short_side_of_the_display(int width, int height, int appMinimum, int expected) =>
        Assert.Equal(expected, GroupGeometry.MinimumExtent(appMinimum, new RectI(0, 0, width, height)));

    [Fact]
    public void Extent_fraction_round_trips_with_zone_math()
    {
        var area = new RectI(0, 0, 2560, 1528);
        var (inner, gap) = ZoneMath.Inner(area, 1.5, 8, 8);
        double fraction = GroupGeometry.ExtentToFraction(600, inner.Width, gap);
        var rects = ZoneMath.ComputeZones([new ZoneFrac(0, 0, fraction, 1)], area, 1.5, 8, 8);
        Assert.InRange(rects[0].Width, 599, 601);
    }

    [Fact]
    public void Free_space_of_an_untouched_empty_zone_is_the_zone()
    {
        var zones = Zones(LayoutCatalog.Quarters);
        AssertFrac(zones[3], GroupGeometry.FreeSpace(zones, 3));
    }

    [Fact]
    public void Free_space_takes_in_the_strip_a_moved_divider_left_behind()
    {
        // Members TL, TR, BL; the divider between the top windows and BL moved up to 0.3 (it never reaches the empty
        // bottom-right zone, so that zone stays at 0.5 and leaves 0.3–0.5 to nobody).
        var zones = Zones(LayoutCatalog.Quarters);
        var members = new[] { zones[0], zones[1], zones[2] };
        var vertical = GroupGeometry.FindDividers(members).Single(d => d.Orientation == DividerOrientation.Vertical);
        zones = GroupGeometry.MoveDivider(zones, vertical, 0.3, 0.1);
        members = [zones[0], zones[1], zones[2]];
        var horizontal = GroupGeometry.FindDividers(members).Single(d => d.Orientation == DividerOrientation.Horizontal);
        zones = GroupGeometry.MoveDivider(zones, horizontal, 0.3, 0.1);
        Assert.Equal(0.5, zones[3].Y, 6);

        AssertFrac(new ZoneFrac(0.5, 0.3, 0.5, 0.7), GroupGeometry.FreeSpace(zones, 3));
    }

    [Fact]
    public void Free_space_gives_way_to_a_member_that_grew_into_the_zone()
    {
        ZoneFrac[] zones = [new(0, 0, 0.5, 0.7), new(0.5, 0, 0.5, 0.7), new(0, 0.7, 0.5, 0.3), new(0.5, 0.5, 0.5, 0.5)];
        AssertFrac(new ZoneFrac(0.5, 0.7, 0.5, 0.3), GroupGeometry.FreeSpace(zones, 3));
    }

    [Fact]
    public void Free_space_stops_at_other_empty_zones()
    {
        var zones = Zones(LayoutCatalog.Quarters); // TR and BR both empty: neither takes the other's space
        AssertFrac(zones[1], GroupGeometry.FreeSpace(zones, 1));
        AssertFrac(zones[3], GroupGeometry.FreeSpace(zones, 3));
    }

    [Fact]
    public void Free_space_of_a_covered_zone_is_empty()
    {
        ZoneFrac[] zones = [new(0, 0, 1, 1), new(0.5, 0.5, 0.5, 0.5)];
        var free = GroupGeometry.FreeSpace(zones, 1);
        Assert.Equal(0, free.W * free.H, 6);
    }

    private static void AssertFrac(ZoneFrac expected, ZoneFrac actual)
    {
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
        Assert.Equal(expected.W, actual.W, 6);
        Assert.Equal(expected.H, actual.H, 6);
    }
}
