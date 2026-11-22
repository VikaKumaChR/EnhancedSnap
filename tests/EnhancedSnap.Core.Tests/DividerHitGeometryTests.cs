using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Core.Layouts;

namespace EnhancedSnap.Core.Tests;

/// <summary>V_Prob04: the ends of a gap must belong to the divider, never to one window's own resize border.</summary>
public class DividerHitGeometryTests
{
    private static readonly RectI WorkArea = new(0, 0, 1920, 1032); // 1080p with a 48 px taskbar

    private static readonly string[] Layouts =
    [
        LayoutCatalog.Halves, LayoutCatalog.Thirds, LayoutCatalog.Quarters, LayoutCatalog.MainStack, LayoutCatalog.HalfStack, LayoutCatalog.Grid3x2,
    ];

    private sealed record Group(List<DividerHit> Hits, RectI[] Members, RectI Frame, RectI Inner, int Gap);

    /// <summary>Gaps (epx) from none to twice the default, odd pixel widths included, at the usual display scales.</summary>
    public static TheoryData<int, double> Spacings()
    {
        var data = new TheoryData<int, double>();
        foreach (int gap in (int[])[0, 2, 4, 6, 8, 12, 16])
        {
            foreach (double scale in (double[])[1, 1.25, 1.5, 1.75, 2])
            {
                data.Add(gap, scale);
            }
        }

        return data;
    }

    /// <summary>
    /// The engine's own steps: zone rectangles, dividers between members, and the divider layer's bounds — the members
    /// plus max(padding, margin) on every side, clipped to the work area. <paramref name="windows"/> are real frames
    /// that differ from the zones, as the engine's tracker records them.
    /// </summary>
    private static Group Build(IReadOnlyList<ZoneFrac> members, double scale = 1, int gapEpx = 8, int marginEpx = 6, IReadOnlyList<RectI>? windows = null)
    {
        var (inner, gap) = ZoneMath.Inner(WorkArea, scale, gapEpx, marginEpx);
        var rects = ZoneMath.ComputeZones(members, WorkArea, scale, gapEpx, marginEpx);
        int padding = Math.Max(gap, (int)Math.Round(6 * scale));
        int reach = Math.Max(padding, inner.Left - WorkArea.Left); // the padding, or the whole margin when wider
        var frame = ZoneMath.Bounds(rects).Inflate(reach, reach, reach, reach).Intersect(WorkArea);
        var hits = DividerHitGeometry.Compute(GroupGeometry.FindDividers(members), rects, inner, gap, frame, scale, windows);
        return new Group(hits, rects, frame, inner, gap);
    }

    private static ZoneFrac[] Layout(string id) => [.. LayoutCatalog.FindBuiltIn(id)!.Zones];

    private static ZoneFrac[] ThreeQuarters() => [.. Layout(LayoutCatalog.Quarters).Where(z => !(z.X > 0.4 && z.Y > 0.4))];

    private static bool Grabbed(Group g, PointI p) => g.Hits.Any(h => h.StripContains(p) || h.CapsContain(p));

    [Fact]
    public void The_strip_is_exactly_the_gap()
    {
        var g = Build(Layout(LayoutCatalog.Halves));
        var hit = Assert.Single(g.Hits);
        Assert.Equal(g.Members[0].Right, hit.Strip.Left);
        Assert.Equal(g.Members[1].Left, hit.Strip.Right);
        Assert.Equal(g.Members[0].Top, hit.Strip.Top);
        Assert.Equal(g.Members[0].Bottom, hit.Strip.Bottom);
    }

    [Fact]
    public void The_strip_follows_the_windows_edges_wherever_the_divider_is()
    {
        // A divider dragged to an odd place. GroupGeometry rounds its position to 4 decimals (0.4371): on its own that
        // gives a gap 1 px left of the edges ZoneMath rounded from the exact fraction, over the left window's edge.
        const double f = 0.437119;
        var g = Build([new ZoneFrac(0, 0, f, 1), new ZoneFrac(f, 0, 1 - f, 1)]);
        var hit = Assert.Single(g.Hits);
        Assert.Equal(g.Members[0].Right, hit.Strip.Left);
        Assert.Equal(g.Members[1].Left, hit.Strip.Right);
    }

    [Fact]
    public void A_divider_that_reaches_the_edge_runs_through_the_margin_to_the_frame()
    {
        var g = Build(Layout(LayoutCatalog.Halves));
        var hit = g.Hits[0];
        int x = (int)hit.Strip.CenterX;

        // Above the windows, in the margin, up to the screen edge (where the pointer stops when pushed up).
        Assert.True(hit.CapsContain(new PointI(x, g.Frame.Top)));
        Assert.True(hit.CapsContain(new PointI(x, g.Members[0].Top - 1)));
        // Below the windows, over their invisible bottom resize borders, down to the frame's bottom.
        Assert.True(hit.CapsContain(new PointI(x, g.Members[0].Bottom)));
        Assert.True(hit.CapsContain(new PointI(x, g.Frame.Bottom - 1)));
        // Wider than the gap: the windows' corner resize zones beside the seam are the divider's, 16 epx each side.
        Assert.True(hit.CapsContain(new PointI(hit.Strip.Left - 16, g.Frame.Top)));
        Assert.True(hit.CapsContain(new PointI(hit.Strip.Right + 15, g.Frame.Top)));
        Assert.False(hit.CapsContain(new PointI(hit.Strip.Left - 17, g.Frame.Top)));
        Assert.False(hit.CapsContain(new PointI(hit.Strip.Right + 16, g.Frame.Top)));
    }

    [Fact]
    public void A_cap_reaches_the_screen_edge_through_a_margin_wider_than_the_gap()
    {
        // The base stops max(gap, 6 epx) outside the windows, 6 px here; the divider layer goes on through the 16 px
        // margin, so the pointer pushed against the top or bottom of the screen is still on the seam.
        var g = Build(Layout(LayoutCatalog.Halves), gapEpx: 4, marginEpx: 16);
        var hit = Assert.Single(g.Hits);
        int x = (int)hit.Strip.CenterX;
        Assert.True(hit.CapsContain(new PointI(x, WorkArea.Top)));
        Assert.True(hit.CapsContain(new PointI(x, WorkArea.Bottom - 1)));
    }

    [Theory]
    [MemberData(nameof(Spacings))]
    public void Nothing_covers_a_window(int gapEpx, double scale)
    {
        int minimum = 2 * (int)Math.Round(3 * scale); // the narrowest strip: 6 epx, in two equal halves
        foreach (string layout in Layouts)
        {
            var g = Build(Layout(layout), scale, gapEpx);
            foreach (var hit in g.Hits)
            {
                string context = $"{layout}, gap {g.Gap} px @{scale}";
                bool vertical = hit.Divider.Orientation == DividerOrientation.Vertical;
                Assert.Equal(Math.Max(g.Gap, minimum), vertical ? hit.Strip.Width : hit.Strip.Height);
                foreach (var member in g.Members)
                {
                    // Only a gap narrower than the minimum is widened over the windows' edges.
                    Assert.False(g.Gap >= minimum && hit.Strip.Overlaps(member), $"{context}: strip {hit.Strip} over window {member}");
                    Assert.False(hit.Caps.Any(cap => cap.Overlaps(member)), $"{context}: a cap over window {member}");
                }

                Assert.All(hit.Caps, cap => Assert.Equal(cap, cap.Intersect(g.Frame)));
            }
        }
    }

    [Fact]
    public void Caps_never_cover_a_window_larger_than_its_zone()
    {
        // Three quarters filled, the bottom-right one empty. The top-right app can't shrink to its zone: its real frame
        // reaches down past the gap into the empty quarter, and up and right into the margin.
        var members = ThreeQuarters();
        var zonesOnly = Build(members);
        var topRight = zonesOnly.Members.Single(m => m.Left > zonesOnly.Inner.CenterX && m.Top < zonesOnly.Inner.CenterY);
        var window = topRight.Inflate(0, 4, 4, 48);
        Assert.Contains(zonesOnly.Hits, h => h.Caps.Any(cap => cap.Overlaps(window))); // what the zones alone would cover

        var g = Build(members, windows: [window]);
        Assert.All(g.Hits, h => Assert.DoesNotContain(h.Caps, cap => cap.Overlaps(window)));
        // The rest of each end still belongs to the divider: the top of the seam, and the joint of the gaps.
        var vertical = g.Hits.Single(h => h.Divider.Orientation == DividerOrientation.Vertical);
        var horizontal = g.Hits.Single(h => h.Divider.Orientation == DividerOrientation.Horizontal);
        Assert.True(vertical.CapsContain(new PointI((int)vertical.Strip.CenterX, g.Frame.Top)));
        Assert.True(Grabbed(g, new PointI((int)vertical.Strip.CenterX, (int)horizontal.Strip.CenterY)));
    }

    [Fact]
    public void The_strip_never_covers_a_window_that_reaches_into_the_gap()
    {
        // The left app can't shrink to its half: its real frame reaches 3 px into the 8 px gap, over its top half.
        var zonesOnly = Build(Layout(LayoutCatalog.Halves));
        var left = zonesOnly.Members[0];
        var window = new RectI(left.Left, left.Top, left.Right + 3, (int)left.CenterY);
        Assert.True(Assert.Single(zonesOnly.Hits).StripContains(new PointI(left.Right + 1, left.Top + 10))); // the zones alone

        var g = Build(Layout(LayoutCatalog.Halves), windows: [window]);
        var hit = Assert.Single(g.Hits);
        Assert.Equal(zonesOnly.Hits[0].Strip, hit.Strip); // the divider itself stays where it was
        Assert.DoesNotContain(hit.StripParts, part => part.Overlaps(window));
        Assert.All(hit.StripParts, part => Assert.Equal(part, part.Intersect(hit.Strip)));
        Assert.Equal(hit.Strip.Area - window.Intersect(hit.Strip).Area, hit.StripParts.Sum(part => part.Area)); // all the rest
        Assert.False(Grabbed(g, new PointI(left.Right + 1, left.Top + 10))); // the window's edge stays the window's
        Assert.True(Grabbed(g, new PointI(window.Right, left.Top + 10))); // the rest of the gap beside it
        Assert.True(Grabbed(g, new PointI(left.Right, left.Bottom - 10))); // the whole gap below it
    }

    [Fact]
    public void A_tiny_gap_keeps_its_whole_strip_beside_a_window_that_reaches_into_it()
    {
        // A 2 epx gap is widened to 6 epx over the windows' edges on purpose: cutting a window out of it would leave
        // a sliver too thin to grab.
        var zonesOnly = Build(Layout(LayoutCatalog.Halves), gapEpx: 2);
        var left = zonesOnly.Members[0];
        var g = Build(Layout(LayoutCatalog.Halves), gapEpx: 2, windows: [left.Inflate(0, 0, 1, 0)]);
        var hit = Assert.Single(g.Hits);
        Assert.Equal(hit.Strip, Assert.Single(hit.StripParts));
    }

    [Fact]
    public void A_T_junction_belongs_to_the_divider_that_runs_through()
    {
        // V_Prob04's group: one window on the left, two stacked on the right.
        var g = Build(Layout(LayoutCatalog.MainStack));
        var vertical = Assert.Single(g.Hits, h => h.Divider.Orientation == DividerOrientation.Vertical);
        var horizontal = Assert.Single(g.Hits, h => h.Divider.Orientation == DividerOrientation.Horizontal);
        var joint = new PointI((int)vertical.Strip.CenterX, (int)horizontal.Strip.CenterY);

        Assert.True(vertical.Strip.Contains(joint));
        Assert.True(horizontal.Strip.Left >= vertical.Strip.Right); // no cap pointing back into the joint …
        Assert.All(horizontal.Caps, cap => Assert.True(cap.Left >= horizontal.Strip.Right)); // … only the one at the right edge
        Assert.NotEmpty(horizontal.Caps);
    }

    [Fact]
    public void The_top_of_the_seam_in_V_Prob04_is_the_divider()
    {
        var g = Build(Layout(LayoutCatalog.MainStack));
        var vertical = g.Hits.Single(h => h.Divider.Orientation == DividerOrientation.Vertical);
        var right = g.Members.Where(m => m.Left > vertical.Strip.Left).MinBy(m => m.Top);

        // Where the pointer was in V_Prob04 (1 s, 9 s): the top end of the seam, just above the windows.
        Assert.True(Grabbed(g, new PointI((int)vertical.Strip.CenterX, right.Top - 2)));
        // A little to the right, still above the right window's corner: no longer that window's corner resize.
        Assert.True(Grabbed(g, new PointI(right.Left + 10, right.Top - 2)));
        // Inside the window it is the window's (its own caption and resize band are left alone).
        Assert.False(Grabbed(g, new PointI(right.Left + 10, right.Top + 2)));
    }

    [Fact]
    public void An_L_shaped_corner_next_to_empty_space_is_covered()
    {
        // Three quarters filled (TL, TR, BL), the bottom-right quarter empty.
        var g = Build(ThreeQuarters());
        var vertical = g.Hits.Single(h => h.Divider.Orientation == DividerOrientation.Vertical);
        var horizontal = g.Hits.Single(h => h.Divider.Orientation == DividerOrientation.Horizontal);
        var topRight = g.Members.Single(m => m.Left > vertical.Strip.Left && m.Top < horizontal.Strip.Top);

        // The joint of the two gaps, and just below the top-right window's bottom-left corner (its invisible border).
        Assert.True(Grabbed(g, new PointI((int)vertical.Strip.CenterX, (int)horizontal.Strip.CenterY)));
        Assert.True(Grabbed(g, new PointI(topRight.Left + 5, topRight.Bottom + 3)));
        // The empty quarter itself stays free a little further away.
        Assert.False(Grabbed(g, new PointI(topRight.Left + 60, topRight.Bottom + 60)));
    }

    [Fact]
    public void Caps_scale_with_the_display()
    {
        var g = Build(Layout(LayoutCatalog.Halves), scale: 1.5);
        var hit = g.Hits[0];
        Assert.True(hit.CapsContain(new PointI(hit.Strip.Left - 24, g.Frame.Top)));
        Assert.False(hit.CapsContain(new PointI(hit.Strip.Left - 25, g.Frame.Top)));
    }

    [Fact]
    public void Same_as_compares_caps_and_parts_by_value()
    {
        var a = Build(Layout(LayoutCatalog.Halves)).Hits[0];
        var b = Build(Layout(LayoutCatalog.Halves)).Hits[0];
        Assert.True(a.SameAs(b));
        Assert.False(a.SameAs(b with { Caps = [] }));
        Assert.False(a.SameAs(b with { StripParts = [] }));
    }

    [Fact]
    public void Subtract_leaves_everything_but_the_cutter()
    {
        var rect = new RectI(0, 0, 100, 100);
        var cutter = new RectI(30, 40, 60, 70);
        var pieces = DividerHitGeometry.Subtract(rect, [cutter]);
        Assert.Equal(rect.Area - cutter.Area, pieces.Sum(p => p.Area));
        Assert.DoesNotContain(pieces, p => p.Overlaps(cutter));
        Assert.All(pieces, p => Assert.Equal(p, p.Intersect(rect)));
        for (int i = 0; i < pieces.Count; i++)
        {
            for (int j = i + 1; j < pieces.Count; j++)
            {
                Assert.False(pieces[i].Overlaps(pieces[j]));
            }
        }
    }
}
