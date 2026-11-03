using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Core.Layouts;
using EnhancedSnap.Core.Settings;

namespace EnhancedSnap.Core.Tests;

public class ZoneMathTests
{
    // The developer machine this was designed on: 2560x1600 @150%, 72 px taskbar.
    private static readonly RectI Work = new(0, 0, 2560, 1528);

    [Fact]
    public void Halves_have_exact_gap_and_margins()
    {
        var zones = ZoneMath.ComputeZones(LayoutCatalog.FindBuiltIn(LayoutCatalog.Halves)!, Work, 1.5, gapEpx: 8, marginEpx: 8);

        Assert.Equal(2, zones.Length);
        Assert.Equal(12, zones[0].Left);                 // 8 epx * 1.5
        Assert.Equal(12, zones[0].Top);
        Assert.Equal(1528 - 12, zones[0].Bottom);
        Assert.Equal(2560 - 12, zones[1].Right);
        Assert.Equal(12, zones[1].Left - zones[0].Right); // gap
        Assert.InRange(Math.Abs(zones[0].Width - zones[1].Width), 0, 1);
    }

    [Theory]
    [InlineData(LayoutCatalog.Thirds)]
    [InlineData(LayoutCatalog.Quarters)]
    [InlineData(LayoutCatalog.Grid3x2)]
    [InlineData(LayoutCatalog.MainStack)]
    [InlineData(LayoutCatalog.HalfStack)]
    [InlineData(LayoutCatalog.CenterFocus)]
    public void Neighbouring_zones_are_separated_by_exactly_the_gap(string layoutId)
    {
        var zones = ZoneMath.ComputeZones(LayoutCatalog.FindBuiltIn(layoutId)!, Work, 1.25, gapEpx: 10, marginEpx: 4);
        int gap = (int)Math.Round(10 * 1.25);

        for (int i = 0; i < zones.Length; i++)
        {
            for (int j = 0; j < zones.Length; j++)
            {
                if (i == j)
                {
                    continue;
                }

                Assert.False(zones[i].Overlaps(zones[j]), $"{layoutId}: zone {i} overlaps zone {j}");
                bool verticalNeighbours = zones[i].Top < zones[j].Bottom && zones[j].Top < zones[i].Bottom;
                if (verticalNeighbours && zones[j].Left >= zones[i].Right && zones[j].Left - zones[i].Right < gap * 2)
                {
                    Assert.Equal(gap, zones[j].Left - zones[i].Right);
                }
            }
        }
    }

    [Fact]
    public void Full_layout_fills_work_area_minus_margin()
    {
        var zones = ZoneMath.ComputeZones(LayoutCatalog.FindBuiltIn(LayoutCatalog.Full)!, new RectI(-1920, 0, 0, 1040), 1.0, 8, 16);
        Assert.Equal(new RectI(-1904, 16, -16, 1024), zones[0]);
    }

    [Fact]
    public void Zero_spacing_tiles_the_work_area()
    {
        var zones = ZoneMath.ComputeZones(LayoutCatalog.FindBuiltIn(LayoutCatalog.Quarters)!, Work, 1.5, 0, 0);
        Assert.Equal(Work.Area, zones.Sum(z => z.Area));
        Assert.Equal(Work, ZoneMath.Bounds(zones));
    }

    [Fact]
    public void Hit_test_picks_nearest_zone_inside_a_gap()
    {
        var zones = ZoneMath.ComputeZones(LayoutCatalog.FindBuiltIn(LayoutCatalog.Halves)!, Work, 1.0, 40, 0);
        var inGapCloserToRight = new PointI(zones[0].Right + 30, 500);
        Assert.Equal(1, ZoneMath.HitTest(zones, inGapCloserToRight));
        Assert.Equal(0, ZoneMath.HitTest(zones, new PointI(10, 10)));
    }

    [Fact]
    public void Span_selection_closes_to_a_rectangle()
    {
        var zones = ZoneMath.ComputeZones(LayoutCatalog.FindBuiltIn(LayoutCatalog.Grid3x2)!, Work, 1.0, 8, 8);
        // Top-left to bottom-middle must include the 4 zones of the 2x2 block.
        var span = ZoneMath.SpanSelection(zones, 0, 4);
        Assert.Equal([0, 1, 3, 4], span);
        var union = ZoneMath.UnionOf(zones, span);
        Assert.Equal(zones[0].Left, union.Left);
        Assert.Equal(zones[4].Right, union.Right);
    }

    [Fact]
    public void Navigation_in_main_and_stack()
    {
        var zones = ZoneMath.ComputeZones(LayoutCatalog.FindBuiltIn(LayoutCatalog.MainStack)!, Work, 1.0, 8, 8);
        Assert.Equal(1, ZoneMath.FindNeighbor(zones, zones[0], Direction.Right)); // main → top of stack
        Assert.Equal(0, ZoneMath.FindNeighbor(zones, zones[2], Direction.Left));
        Assert.Equal(2, ZoneMath.FindNeighbor(zones, zones[1], Direction.Down));
        Assert.Equal(1, ZoneMath.FindNeighbor(zones, zones[2], Direction.Up));
        Assert.Equal(-1, ZoneMath.FindNeighbor(zones, zones[0], Direction.Up));
        Assert.Equal(-1, ZoneMath.FindNeighbor(zones, zones[1], Direction.Right));
    }

    [Fact]
    public void Navigation_prefers_aligned_zone_in_quarters()
    {
        var zones = ZoneMath.ComputeZones(LayoutCatalog.FindBuiltIn(LayoutCatalog.Quarters)!, Work, 1.0, 8, 8);
        Assert.Equal(1, ZoneMath.FindNeighbor(zones, zones[0], Direction.Right));
        Assert.Equal(3, ZoneMath.FindNeighbor(zones, zones[2], Direction.Right));
        Assert.Equal(2, ZoneMath.FindNeighbor(zones, zones[0], Direction.Down));
    }

    [Fact]
    public void Edge_zone_uses_perpendicular_centre()
    {
        var zones = ZoneMath.ComputeZones(LayoutCatalog.FindBuiltIn(LayoutCatalog.Quarters)!, Work, 1.0, 8, 8);
        Assert.Equal(0, ZoneMath.FindEdgeZone(zones, Direction.Left, perpendicularCenter: 100));
        Assert.Equal(2, ZoneMath.FindEdgeZone(zones, Direction.Left, perpendicularCenter: 1400));
        Assert.Equal(1, ZoneMath.FindEdgeZone(zones, Direction.Right, perpendicularCenter: 0));
    }

    [Fact]
    public void Match_zones_recognises_single_and_spanned_rects()
    {
        var zones = ZoneMath.ComputeZones(LayoutCatalog.FindBuiltIn(LayoutCatalog.Thirds)!, Work, 1.0, 8, 8);
        Assert.Equal([1], ZoneMath.MatchZones(zones, zones[1]));
        Assert.Equal([0, 1], ZoneMath.MatchZones(zones, zones[0].Union(zones[1])));
        Assert.Empty(ZoneMath.MatchZones(zones, new RectI(5, 5, 300, 300)));
    }

    [Fact]
    public void Custom_layout_columns_with_splits()
    {
        var custom = new CustomLayout
        {
            Id = "custom-test",
            Name = "Test",
            Orientation = LayoutOrientation.Columns,
            Tracks = [new LayoutTrack { Weight = 2, Splits = 1 }, new LayoutTrack { Weight = 1, Splits = 3 }],
        };
        var def = LayoutCatalog.FromCustom(custom);
        Assert.Equal(4, def.Zones.Count);
        Assert.Equal(2.0 / 3.0, def.Zones[0].W, 6);
        Assert.Equal(1.0 / 3.0, def.Zones[3].H, 6);

        var rows = new CustomLayout { Id = "r", Orientation = LayoutOrientation.Rows, Tracks = [new LayoutTrack(), new LayoutTrack { Splits = 2 }] };
        var rowsDef = LayoutCatalog.FromCustom(rows);
        Assert.Equal(new ZoneFrac(0.5, 0.5, 0.5, 0.5), rowsDef.Zones[2]);
    }

    [Theory]
    [InlineData(2560, 1600, LayoutCatalog.Halves)]
    [InlineData(3440, 1440, LayoutCatalog.CenterFocus)]
    [InlineData(1080, 1920, LayoutCatalog.Rows)]
    public void Default_layout_follows_monitor_shape(int w, int h, string expected) =>
        Assert.Equal(expected, LayoutCatalog.DefaultLayoutIdFor(w, h));

    [Fact]
    public void Tiny_work_area_degrades_gracefully()
    {
        var zones = ZoneMath.ComputeZones(LayoutCatalog.FindBuiltIn(LayoutCatalog.Thirds)!, new RectI(0, 0, 100, 80), 2.0, 48, 48);
        Assert.All(zones, z => Assert.False(z.IsEmpty));
    }
}
