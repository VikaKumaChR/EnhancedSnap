using EnhancedSnap.Core.Geometry;

namespace EnhancedSnap.Core.Layouts;

/// <summary>
/// Pure geometry: turns a <see cref="LayoutDefinition"/> into pixel rectangles with Fluent spacing,
/// and answers hit-testing / spanning / keyboard navigation questions. No Win32 here, so it is unit tested.
/// </summary>
public static class ZoneMath
{
    /// <summary>
    /// Computes zone rectangles (the visible window frame targets) inside <paramref name="workArea"/>.
    /// </summary>
    /// <remarks>
    /// A fractional edge f maps to P(f) = inner.Left + f * (inner.Width + gap) and a zone [f0, f1] becomes
    /// [P(f0), P(f1) - gap]. Shared edges are rounded once, so neighbours are separated by exactly
    /// <c>gap</c> pixels and a full-width zone ends exactly at the inner edge. All values in physical pixels.
    /// </remarks>
    public static RectI[] ComputeZones(LayoutDefinition layout, RectI workArea, double scale, int gapEpx, int marginEpx) =>
        ComputeZones(layout.Zones, workArea, scale, gapEpx, marginEpx);

    /// <summary>Same as above for raw fractions (snap groups keep their own, divider-adjusted fractions).</summary>
    public static RectI[] ComputeZones(IReadOnlyList<ZoneFrac> fractions, RectI workArea, double scale, int gapEpx, int marginEpx)
    {
        var (inner, gap) = Inner(workArea, scale, gapEpx, marginEpx);
        double spanX = inner.Width + gap;
        double spanY = inner.Height + gap;
        int Px(double f) => inner.Left + (int)Math.Round(Math.Round(f, 6) * spanX);
        int Py(double f) => inner.Top + (int)Math.Round(Math.Round(f, 6) * spanY);

        var zones = new RectI[fractions.Count];
        for (int i = 0; i < zones.Length; i++)
        {
            var z = fractions[i];
            int left = Px(z.X);
            int top = Py(z.Y);
            int right = Math.Max(left + 1, Px(z.Right) - gap);
            int bottom = Math.Max(top + 1, Py(z.Bottom) - gap);
            zones[i] = new RectI(left, top, right, bottom);
        }

        return zones;
    }

    /// <summary>Usable area (work area minus margin) and the gap in pixels, as used by <see cref="ComputeZones(IReadOnlyList{ZoneFrac}, RectI, double, int, int)"/>.</summary>
    public static (RectI Inner, int Gap) Inner(RectI workArea, double scale, int gapEpx, int marginEpx)
    {
        int gap = (int)Math.Round(Math.Max(0, gapEpx) * scale);
        int margin = (int)Math.Round(Math.Max(0, marginEpx) * scale);
        var inner = new RectI(workArea.Left + margin, workArea.Top + margin, workArea.Right - margin, workArea.Bottom - margin);
        if (inner.Width < 64 || inner.Height < 64)
        {
            return (workArea, 0); // Degenerate (tiny work area): ignore spacing rather than producing negative zones.
        }

        return (inner, gap);
    }

    /// <summary>Inverse of the edge formula: the fraction whose gap centre lies at <paramref name="pixel"/>.</summary>
    public static double PixelToFraction(int pixel, int innerStart, int innerLength, int gap) =>
        (pixel + (gap / 2.0) - innerStart) / Math.Max(1.0, innerLength + gap);

    /// <summary>Centre of the gap that sits at fraction <paramref name="fraction"/>.</summary>
    public static double FractionToGapCenter(double fraction, int innerStart, int innerLength, int gap) =>
        innerStart + (fraction * (innerLength + gap)) - (gap / 2.0);

    /// <summary>Index of the zone under the point, or the nearest zone when the point is in a gap. -1 if none.</summary>
    public static int HitTest(IReadOnlyList<RectI> zones, PointI point)
    {
        int best = -1;
        long bestDistance = long.MaxValue;
        long bestArea = long.MaxValue;
        for (int i = 0; i < zones.Count; i++)
        {
            long d = zones[i].DistanceSquaredTo(point);
            long area = zones[i].Area;
            // Inside several (overlapping custom zones)? Prefer the smallest, like FancyZones' "smallest zone" policy.
            if (d < bestDistance || (d == bestDistance && area < bestArea))
            {
                best = i;
                bestDistance = d;
                bestArea = area;
            }
        }

        return best;
    }

    /// <summary>
    /// Zones selected when spanning from <paramref name="anchor"/> to <paramref name="current"/> (Ctrl + drag):
    /// every zone overlapping the bounding box, expanded until the selection is a closed rectangle.
    /// </summary>
    public static int[] SpanSelection(IReadOnlyList<RectI> zones, int anchor, int current)
    {
        if (anchor < 0 || current < 0 || anchor >= zones.Count || current >= zones.Count)
        {
            return current >= 0 && current < zones.Count ? [current] : [];
        }

        var bounds = zones[anchor].Union(zones[current]);
        var selected = new SortedSet<int>();
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < zones.Count; i++)
            {
                if (!selected.Contains(i) && zones[i].Overlaps(bounds))
                {
                    selected.Add(i);
                    var grown = bounds.Union(zones[i]);
                    if (grown != bounds)
                    {
                        bounds = grown;
                        changed = true;
                    }
                }
            }
        }

        return [.. selected];
    }

    public static RectI UnionOf(IReadOnlyList<RectI> zones, IReadOnlyList<int> indices)
    {
        if (indices.Count == 0)
        {
            return default;
        }

        var r = zones[indices[0]];
        for (int i = 1; i < indices.Count; i++)
        {
            r = r.Union(zones[indices[i]]);
        }

        return r;
    }

    /// <summary>The whole usable area of a layout (all zones merged) — used for "fill" (rounded maximize).</summary>
    public static RectI Bounds(IReadOnlyList<RectI> zones)
    {
        if (zones.Count == 0)
        {
            return default;
        }

        var r = zones[0];
        for (int i = 1; i < zones.Count; i++)
        {
            r = r.Union(zones[i]);
        }

        return r;
    }

    /// <summary>
    /// Nearest rectangle strictly in <paramref name="direction"/> from <paramref name="current"/>.
    /// Candidates overlapping on the perpendicular axis win; then the closest along the direction;
    /// then the closest centre on the perpendicular axis; then the lowest index. -1 if none.
    /// </summary>
    public static int FindNeighbor(IReadOnlyList<RectI> rects, RectI current, Direction direction, int tolerance = 4)
    {
        int best = -1;
        (int overlapRank, long primary, double perpendicular, int index) bestKey = (int.MaxValue, long.MaxValue, double.MaxValue, int.MaxValue);
        for (int i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            if (r.NearlyEquals(current, tolerance))
            {
                continue;
            }

            long primary;
            long overlap;
            double perpendicular;
            switch (direction)
            {
                case Direction.Left:
                    if (r.Right > current.Left + tolerance) continue;
                    primary = current.Left - r.Right;
                    overlap = Overlap(r.Top, r.Bottom, current.Top, current.Bottom);
                    perpendicular = Math.Abs(r.CenterY - current.CenterY);
                    break;
                case Direction.Right:
                    if (r.Left < current.Right - tolerance) continue;
                    primary = r.Left - current.Right;
                    overlap = Overlap(r.Top, r.Bottom, current.Top, current.Bottom);
                    perpendicular = Math.Abs(r.CenterY - current.CenterY);
                    break;
                case Direction.Up:
                    if (r.Bottom > current.Top + tolerance) continue;
                    primary = current.Top - r.Bottom;
                    overlap = Overlap(r.Left, r.Right, current.Left, current.Right);
                    perpendicular = Math.Abs(r.CenterX - current.CenterX);
                    break;
                default:
                    if (r.Top < current.Bottom - tolerance) continue;
                    primary = r.Top - current.Bottom;
                    overlap = Overlap(r.Left, r.Right, current.Left, current.Right);
                    perpendicular = Math.Abs(r.CenterX - current.CenterX);
                    break;
            }

            var key = (overlap > 0 ? 0 : 1, Math.Max(0, primary), Math.Round(perpendicular, 1), i);
            if (key.CompareTo(bestKey) < 0)
            {
                bestKey = key;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// Zone along an edge of the layout (e.g. the left-most column for <see cref="Direction.Left"/>), choosing the
    /// one whose centre is closest to <paramref name="perpendicularCenter"/> (a global Y for Left/Right, X for Up/Down).
    /// Used for snapping floating windows and when keyboard navigation enters another monitor.
    /// </summary>
    public static int FindEdgeZone(IReadOnlyList<RectI> zones, Direction edge, double perpendicularCenter, int tolerance = 4)
    {
        if (zones.Count == 0)
        {
            return -1;
        }

        int extreme = edge switch
        {
            Direction.Left => zones.Min(static z => z.Left),
            Direction.Right => zones.Max(static z => z.Right),
            Direction.Up => zones.Min(static z => z.Top),
            _ => zones.Max(static z => z.Bottom),
        };

        int best = -1;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < zones.Count; i++)
        {
            var z = zones[i];
            bool onEdge = edge switch
            {
                Direction.Left => Math.Abs(z.Left - extreme) <= tolerance,
                Direction.Right => Math.Abs(z.Right - extreme) <= tolerance,
                Direction.Up => Math.Abs(z.Top - extreme) <= tolerance,
                _ => Math.Abs(z.Bottom - extreme) <= tolerance,
            };
            if (!onEdge)
            {
                continue;
            }

            double centre = edge is Direction.Left or Direction.Right ? z.CenterY : z.CenterX;
            double distance = Math.Abs(centre - perpendicularCenter);
            if (distance < bestDistance - 0.5)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>Indices of zones whose union exactly matches <paramref name="rect"/> (within tolerance), or empty.</summary>
    public static int[] MatchZones(IReadOnlyList<RectI> zones, RectI rect, int tolerance = 2)
    {
        for (int i = 0; i < zones.Count; i++)
        {
            if (zones[i].NearlyEquals(rect, tolerance))
            {
                return [i];
            }
        }

        var inside = new List<int>();
        for (int i = 0; i < zones.Count; i++)
        {
            var z = zones[i];
            if (z.Left >= rect.Left - tolerance && z.Top >= rect.Top - tolerance &&
                z.Right <= rect.Right + tolerance && z.Bottom <= rect.Bottom + tolerance)
            {
                inside.Add(i);
            }
        }

        return inside.Count > 0 && UnionOf(zones, inside).NearlyEquals(rect, tolerance) ? [.. inside] : [];
    }

    private static long Overlap(int a0, int a1, int b0, int b1) => Math.Max(0, Math.Min(a1, b1) - Math.Max(a0, b0));
}
