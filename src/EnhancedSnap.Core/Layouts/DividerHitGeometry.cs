using EnhancedSnap.Core.Geometry;

namespace EnhancedSnap.Core.Layouts;

/// <summary>
/// A divider of a snap group in screen pixels. It can be grabbed on <see cref="StripParts"/> (the gap between the
/// windows, less any window that reaches into it) and <see cref="Caps"/> (the extra targets at its ends), nowhere
/// else. <see cref="Strip"/> is the whole gap, whose centre is where the divider is. <see cref="Bar"/> is only drawn:
/// the wider bar shown over the seam while it is hovered reaches a little over the windows' edges, and those stay the
/// windows'.
/// </summary>
public readonly record struct DividerHit(Divider Divider, RectI Strip, RectI Bar, RectI[] Caps, RectI[] StripParts)
{
    public bool StripContains(PointI point) => AnyContains(StripParts, point);

    public bool CapsContain(PointI point) => AnyContains(Caps, point);

    /// <summary>Value equality, caps and parts included (the record's own equality compares arrays by reference).</summary>
    public bool SameAs(DividerHit other) =>
        Divider == other.Divider && Strip == other.Strip && Bar == other.Bar
        && (Caps ?? []).AsSpan().SequenceEqual(other.Caps ?? []) && (StripParts ?? []).AsSpan().SequenceEqual(other.StripParts ?? []);

    private static bool AnyContains(RectI[]? rects, PointI point)
    {
        foreach (var r in rects ?? [])
        {
            if (r.Contains(point))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The hit area of a snap group's dividers (V_Prob04). Windows 11 gives every window an invisible resize border of
/// about 7 epx outside its visible frame, with corner zones at the ends; in the gap the divider sits above those, but
/// at the ends of a gap the pointer used to fall into a window's corner and resize that one window. So each end gets
/// a cap:
/// <list type="bullet">
/// <item>where the divider reaches the edge of the group, the cap runs through the margin to the edge of the divider
/// layer, which reaches the work area's edge — at the top of the screen the pointer can simply be pushed against the
/// screen edge, whatever the margin;</item>
/// <item>where it ends next to empty space or at an L-shaped corner, the cap covers the corner and a little of the
/// empty space;</item>
/// <item>where it ends against a divider that runs on (a T junction), that divider's strip already covers the joint,
/// so there is no cap.</item>
/// </list>
/// The strip is exactly the gap, taken from the windows' own edges; only a gap narrower than
/// <see cref="MinimumStripEpx"/> is widened to it, and then reaches a little over the edges beside it. Caps reach
/// <see cref="CapSideEpx"/> past the strip on each side, so the windows' corner resize zones beside the seam belong to
/// the divider, but they never cover a window's visible frame: every member's zone, and its real frame where that
/// reaches past the zone, is cut out of them. A window larger than its zone (an app that can't shrink) can reach into
/// the gap itself; its frame is cut out of the strip too, except from the widened minimum strip of a tiny gap. Pure
/// geometry, unit tested.
/// </summary>
public static class DividerHitGeometry
{
    /// <summary>The narrowest strip (effective pixels, two equal halves): a narrower gap is widened to it.</summary>
    public const int MinimumStripEpx = 6;

    /// <summary>How far a cap reaches past the strip on each side (effective pixels).</summary>
    public const int CapSideEpx = 16;

    /// <summary>How far a cap at an open end reaches beyond the perpendicular gap (effective pixels).</summary>
    public const int CapDepthEpx = 8;

    private const double Eps = 1e-4;

    /// <param name="dividers">The group's dividers (fractions of <paramref name="inner"/>).</param>
    /// <param name="members">The members' zone rectangles: the strips run exactly between them, and caps never cover them.</param>
    /// <param name="inner">The monitor's usable area (work area minus margin), as in <see cref="ZoneMath.Inner"/>.</param>
    /// <param name="gap">Space between windows in pixels.</param>
    /// <param name="frame">The divider layer's bounds (its input window): nothing reaches outside it, and a cap where a divider reaches the group's edge runs to it.</param>
    /// <param name="scale">Display scale (1 = 96 DPI).</param>
    /// <param name="windows">The members' real frames where known. A window can be larger than its zone (an app's minimum size); caps never cover it either, nor does the strip of a gap at least <see cref="MinimumStripEpx"/> wide.</param>
    public static List<DividerHit> Compute(IReadOnlyList<Divider> dividers, IReadOnlyList<RectI> members, RectI inner, int gap, RectI frame, double scale, IReadOnlyList<RectI>? windows = null)
    {
        int minimum = 2 * (int)Math.Round(MinimumStripEpx / 2.0 * scale);
        int barHalf = Math.Max((gap / 2) + (int)Math.Round(4 * scale), (int)Math.Round(8 * scale)); // a little over the seam
        int side = (int)Math.Round(CapSideEpx * scale);
        int depth = gap + (int)Math.Round(CapDepthEpx * scale);
        var cutters = new List<RectI>(members);
        if (windows is not null)
        {
            cutters.AddRange(windows);
        }

        var hits = new List<DividerHit>(dividers.Count);
        foreach (var d in dividers)
        {
            bool vertical = d.Orientation == DividerOrientation.Vertical;
            int alongStart = vertical ? inner.Top : inner.Left;
            int alongLength = vertical ? inner.Height : inner.Width;
            int start = Edge(d.Start, alongStart, alongLength, gap);
            int end = Edge(d.End, alongStart, alongLength, gap) - gap;
            var (gapLow, gapHigh) = GapAt(d, vertical, members, inner, gap, start, end);
            int widen = Math.Max(0, minimum - (gapHigh - gapLow));
            int low = gapLow - (widen / 2);
            int high = gapHigh + widen - (widen / 2);
            int center = low + ((high - low) / 2);
            var strip = Make(vertical, low, high, start, end);
            var bar = Make(vertical, center - barHalf, center + barHalf, start, end);

            var caps = new List<RectI>();
            foreach (bool atStart in (ReadOnlySpan<bool>)[true, false])
            {
                double at = atStart ? d.Start : d.End;
                if (EndsAtThroughDivider(dividers, d, at))
                {
                    continue;
                }

                bool boundary = atStart ? at <= Eps : at >= 1 - Eps;
                int frameStart = vertical ? frame.Top : frame.Left;
                int frameEnd = vertical ? frame.Bottom : frame.Right;
                int a = atStart ? (boundary ? frameStart : Math.Max(frameStart, start - depth)) : end;
                int b = atStart ? start : (boundary ? frameEnd : Math.Min(frameEnd, end + depth));
                if (b <= a)
                {
                    continue;
                }

                var cap = Make(vertical, low - side, high + side, a, b).Intersect(frame);
                caps.AddRange(Subtract(cap, cutters));
            }

            // A window that reaches into the gap keeps its edge: the strip is cut around it (a tiny gap's widened strip
            // overlaps the edges beside it on purpose, so it isn't cut).
            RectI[] parts = widen == 0 && windows is { Count: > 0 } ? [.. Subtract(strip, windows)] : [strip];
            hits.Add(new DividerHit(d, strip, bar, [.. caps], parts));
        }

        return hits;
    }

    /// <summary><paramref name="rect"/> minus every cutter, as non-overlapping rectangles.</summary>
    public static List<RectI> Subtract(RectI rect, IReadOnlyList<RectI> cutters)
    {
        var pieces = new List<RectI>();
        if (rect.IsEmpty)
        {
            return pieces;
        }

        pieces.Add(rect);
        foreach (var cutter in cutters)
        {
            var next = new List<RectI>(pieces.Count + 3);
            foreach (var piece in pieces)
            {
                if (!piece.Overlaps(cutter))
                {
                    next.Add(piece);
                    continue;
                }

                if (cutter.Top > piece.Top)
                {
                    next.Add(new RectI(piece.Left, piece.Top, piece.Right, cutter.Top));
                }

                if (cutter.Bottom < piece.Bottom)
                {
                    next.Add(new RectI(piece.Left, cutter.Bottom, piece.Right, piece.Bottom));
                }

                int top = Math.Max(piece.Top, cutter.Top);
                int bottom = Math.Min(piece.Bottom, cutter.Bottom);
                if (cutter.Left > piece.Left)
                {
                    next.Add(new RectI(piece.Left, top, cutter.Left, bottom));
                }

                if (cutter.Right < piece.Right)
                {
                    next.Add(new RectI(cutter.Right, top, piece.Right, bottom));
                }
            }

            pieces = next;
        }

        return pieces;
    }

    /// <summary>
    /// The gap at a divider, between the windows' own edges. The divider's position is rounded (GroupGeometry keeps 4
    /// decimals), so the pixel it gives can be 1 px off the edge ZoneMath rounded from the exact fraction: the edges
    /// of the members beside the divider, within 1 px of it, are used instead.
    /// </summary>
    private static (int Low, int High) GapAt(Divider d, bool vertical, IReadOnlyList<RectI> members, RectI inner, int gap, int start, int end)
    {
        int high = Edge(d.Position, vertical ? inner.Left : inner.Top, vertical ? inner.Width : inner.Height, gap);
        int low = high - gap;
        int before = int.MinValue; // where the windows before the gap end
        int after = int.MaxValue; // where the windows after it begin
        foreach (var m in members)
        {
            if (Math.Min(vertical ? m.Bottom : m.Right, end) <= Math.Max(vertical ? m.Top : m.Left, start))
            {
                continue; // not beside this divider
            }

            int farEdge = vertical ? m.Right : m.Bottom;
            int nearEdge = vertical ? m.Left : m.Top;
            if (Math.Abs(farEdge - low) <= 1)
            {
                before = Math.Max(before, farEdge);
            }

            if (Math.Abs(nearEdge - high) <= 1)
            {
                after = Math.Min(after, nearEdge);
            }
        }

        return (before == int.MinValue ? low : before, after == int.MaxValue ? high : after);
    }

    /// <summary>
    /// The pixel at fraction <paramref name="f"/> along an axis, rounded exactly as
    /// <see cref="ZoneMath.ComputeZones(IReadOnlyList{ZoneFrac}, RectI, double, int, int)"/> rounds zone edges.
    /// </summary>
    private static int Edge(double f, int innerStart, int innerLength, int gap) =>
        innerStart + (int)Math.Round(Math.Round(f, 6) * (innerLength + gap));

    /// <summary>True when a perpendicular divider passes through this end, so its strip covers the joint (T junction).</summary>
    private static bool EndsAtThroughDivider(IReadOnlyList<Divider> dividers, Divider d, double at)
    {
        foreach (var p in dividers)
        {
            if (p.Orientation != d.Orientation && Math.Abs(p.Position - at) < Eps && p.Start < d.Position - Eps && p.End > d.Position + Eps)
            {
                return true;
            }
        }

        return false;
    }

    private static RectI Make(bool vertical, int across0, int across1, int along0, int along1) =>
        vertical ? new RectI(across0, along0, across1, along1) : new RectI(along0, across0, along1, across1);
}
