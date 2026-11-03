namespace EnhancedSnap.Core.Layouts;

public enum DividerOrientation
{
    /// <summary>A vertical line at X = <see cref="Divider.Position"/>; dragging it resizes horizontally.</summary>
    Vertical,

    /// <summary>A horizontal line at Y = <see cref="Divider.Position"/>; dragging it resizes vertically.</summary>
    Horizontal,
}

/// <summary>
/// A shared edge inside a snap group, in fractions of the group area. <see cref="Start"/>/<see cref="End"/>
/// span the perpendicular axis. A divider is the maximal run where zones on one side meet zones on the other,
/// so in a 2×2 grid the middle lines span the whole area and move all four windows ("small window" combos).
/// </summary>
public readonly record struct Divider(DividerOrientation Orientation, double Position, double Start, double End);

/// <summary>Pure geometry for snap groups: find dividers and move them. Unit tested, no Win32.</summary>
public static class GroupGeometry
{
    /// <summary>No zone gets smaller than this share of the display's shorter side (see <see cref="MinimumExtent"/>).</summary>
    public const double AspectFloor = 0.25;

    private const double Eps = 1e-4;

    /// <summary>
    /// Smallest extent (pixels) a grouped window may have along one axis: its app's own minimum size, but never less
    /// than a quarter of the display's shorter side. Tying the floor to the short side makes the limits follow the
    /// display's aspect ratio — on a landscape display columns can become relatively narrower than rows, on a
    /// portrait display it is the other way round — and the largest size is whatever the neighbours leave.
    /// </summary>
    public static int MinimumExtent(int appMinimum, Geometry.RectI workArea) =>
        Math.Max(appMinimum, (int)Math.Round(Math.Min(workArea.Width, workArea.Height) * AspectFloor));

    /// <summary>Pixels → zone fraction along an axis (a zone of fraction f is f·(inner + gap) − gap pixels).</summary>
    public static double ExtentToFraction(int pixels, int innerExtent, int gap) =>
        (pixels + gap) / (double)Math.Max(1, innerExtent + gap);

    /// <summary>
    /// Where a divider may go so that every rectangle touching it keeps its minimum extent (fractions along the
    /// divider's axis, one per rectangle). <c>Low &gt; High</c> means the divider can't move at all.
    /// </summary>
    public static (double Low, double High) DividerRange(IReadOnlyList<ZoneFrac> rects, Divider divider, IReadOnlyList<double> minExtent)
    {
        bool vertical = divider.Orientation == DividerOrientation.Vertical;
        double low = 0;
        double high = 1;
        for (int i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            if (!Touches(r, divider, vertical))
            {
                continue;
            }

            double start = vertical ? r.X : r.Y;
            double end = vertical ? r.Right : r.Bottom;
            if (Near(end, divider.Position))
            {
                low = Math.Max(low, start + minExtent[i]);
            }
            else if (Near(start, divider.Position))
            {
                high = Math.Min(high, end - minExtent[i]);
            }
        }

        return (low, high);
    }

    public static List<Divider> FindDividers(IReadOnlyList<ZoneFrac> zones)
    {
        var dividers = new List<Divider>();
        Collect(zones, vertical: true, dividers);
        Collect(zones, vertical: false, dividers);
        return dividers;
    }

    /// <summary>
    /// Moves a divider to <paramref name="position"/> (clamped so every affected zone keeps at least
    /// <paramref name="minSize"/>). Returns new fractions; zones not touching the divider are unchanged.
    /// </summary>
    public static ZoneFrac[] MoveDivider(IReadOnlyList<ZoneFrac> zones, Divider divider, double position, double minSize)
    {
        bool vertical = divider.Orientation == DividerOrientation.Vertical;
        double low = 0;
        double high = 1;
        foreach (var z in zones)
        {
            if (!Touches(z, divider, vertical))
            {
                continue;
            }

            double start = vertical ? z.X : z.Y;
            double end = vertical ? z.Right : z.Bottom;
            if (Near(end, divider.Position))
            {
                low = Math.Max(low, start + minSize);
            }
            else if (Near(start, divider.Position))
            {
                high = Math.Min(high, end - minSize);
            }
        }

        if (low > high)
        {
            return [.. zones]; // no room to move
        }

        double p = Math.Clamp(position, low, high);
        var result = zones.ToArray();
        for (int i = 0; i < result.Length; i++)
        {
            var z = result[i];
            if (!Touches(z, divider, vertical))
            {
                continue;
            }

            if (vertical)
            {
                if (Near(z.Right, divider.Position))
                {
                    result[i] = z with { W = p - z.X };
                }
                else if (Near(z.X, divider.Position))
                {
                    result[i] = z with { X = p, W = z.Right - p };
                }
            }
            else
            {
                if (Near(z.Bottom, divider.Position))
                {
                    result[i] = z with { H = p - z.Y };
                }
                else if (Near(z.Y, divider.Position))
                {
                    result[i] = z with { Y = p, H = z.Bottom - p };
                }
            }
        }

        return result;
    }

    /// <summary>
    /// The space empty zone <paramref name="index"/> really has. Dividers run between members only, so moving one
    /// leaves the empty zones beside it where they were: a strip that belongs to no zone opens up next to them, or a
    /// member grows into them. The zone is cut back to the largest part no other zone overlaps, then grown up and down,
    /// then left and right, to the nearest other zone or the edge of the area.
    /// </summary>
    public static ZoneFrac FreeSpace(IReadOnlyList<ZoneFrac> zones, int index)
    {
        var z = zones[index];
        double left = z.X, top = z.Y, right = z.Right, bottom = z.Bottom;
        var others = zones.Where((_, i) => i != index).ToList();
        foreach (var o in others)
        {
            if (Math.Min(right, o.Right) - Math.Max(left, o.X) <= Eps || Math.Min(bottom, o.Bottom) - Math.Max(top, o.Y) <= Eps)
            {
                continue;
            }

            (double L, double T, double R, double B)[] parts =
            [
                (left, Math.Max(top, o.Bottom), right, bottom),
                (left, top, right, Math.Min(bottom, o.Y)),
                (Math.Max(left, o.Right), top, right, bottom),
                (left, top, Math.Min(right, o.X), bottom),
            ];
            (left, top, right, bottom) = parts.MaxBy(p => Math.Max(0, p.R - p.L) * Math.Max(0, p.B - p.T));
        }

        if (right - left <= Eps || bottom - top <= Eps)
        {
            return new ZoneFrac(left, top, 0, 0); // fully covered
        }

        top = others.Where(o => o.Bottom <= top + Eps && Overlap(o.X, o.Right, left, right)).Select(o => o.Bottom).DefaultIfEmpty(0).Max();
        bottom = others.Where(o => o.Y >= bottom - Eps && Overlap(o.X, o.Right, left, right)).Select(o => o.Y).DefaultIfEmpty(1).Min();
        left = others.Where(o => o.Right <= left + Eps && Overlap(o.Y, o.Bottom, top, bottom)).Select(o => o.Right).DefaultIfEmpty(0).Max();
        right = others.Where(o => o.X >= right - Eps && Overlap(o.Y, o.Bottom, top, bottom)).Select(o => o.X).DefaultIfEmpty(1).Min();
        return new ZoneFrac(left, top, right - left, bottom - top);

        static bool Overlap(double a0, double a1, double b0, double b1) => Math.Min(a1, b1) - Math.Max(a0, b0) > Eps;
    }

    /// <summary>The divider at the same line as <paramref name="divider"/> after zones moved (for continuous drags).</summary>
    public static Divider? Find(IReadOnlyList<ZoneFrac> zones, DividerOrientation orientation, double position, double perpendicular)
    {
        Divider? best = null;
        double bestDistance = double.MaxValue;
        foreach (var d in FindDividers(zones))
        {
            if (d.Orientation != orientation || perpendicular < d.Start - Eps || perpendicular > d.End + Eps)
            {
                continue;
            }

            double distance = Math.Abs(d.Position - position);
            if (distance < bestDistance)
            {
                best = d;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Normalizes absolute rectangles (e.g. windows arranged by Windows Snap) into fractions of an area.</summary>
    public static ZoneFrac ToFraction(Geometry.RectI rect, Geometry.RectI area, int gap)
    {
        double spanX = area.Width + gap;
        double spanY = area.Height + gap;
        double x = (rect.Left - area.Left) / spanX;
        double y = (rect.Top - area.Top) / spanY;
        double right = (rect.Right + gap - area.Left) / spanX;
        double bottom = (rect.Bottom + gap - area.Top) / spanY;
        x = Snap(Math.Clamp(x, 0, 1));
        y = Snap(Math.Clamp(y, 0, 1));
        right = Snap(Math.Clamp(right, 0, 1));
        bottom = Snap(Math.Clamp(bottom, 0, 1));
        return new ZoneFrac(x, y, Math.Max(0.02, right - x), Math.Max(0.02, bottom - y));
    }

    private static double Snap(double f)
    {
        // Pull values that are within half a percent of common splits onto them, so dividers line up.
        foreach (double s in (ReadOnlySpan<double>)[0, 0.25, 1.0 / 3.0, 0.5, 2.0 / 3.0, 0.75, 1])
        {
            if (Math.Abs(f - s) < 0.005)
            {
                return s;
            }
        }

        return Math.Round(f, 4);
    }

    private static void Collect(IReadOnlyList<ZoneFrac> zones, bool vertical, List<Divider> dividers)
    {
        var positions = zones
            .Select(z => vertical ? z.Right : z.Bottom)
            .Where(p => p > Eps && p < 1 - Eps)
            .Select(p => Math.Round(p, 4))
            .Distinct()
            .OrderBy(p => p);

        foreach (double p in positions)
        {
            var overlaps = new List<(double Start, double End)>();
            foreach (var a in zones)
            {
                if (!Near(vertical ? a.Right : a.Bottom, p))
                {
                    continue;
                }

                foreach (var b in zones)
                {
                    if (!Near(vertical ? b.X : b.Y, p))
                    {
                        continue;
                    }

                    double start = Math.Max(vertical ? a.Y : a.X, vertical ? b.Y : b.X);
                    double end = Math.Min(vertical ? a.Bottom : a.Right, vertical ? b.Bottom : b.Right);
                    if (end - start > Eps)
                    {
                        overlaps.Add((start, end));
                    }
                }
            }

            overlaps.Sort();
            int i = 0;
            while (i < overlaps.Count)
            {
                var (start, end) = overlaps[i];
                int j = i + 1;
                while (j < overlaps.Count && overlaps[j].Start <= end + Eps)
                {
                    end = Math.Max(end, overlaps[j].End);
                    j++;
                }

                dividers.Add(new Divider(vertical ? DividerOrientation.Vertical : DividerOrientation.Horizontal, p, start, end));
                i = j;
            }
        }
    }

    private static bool Touches(ZoneFrac z, Divider d, bool vertical)
    {
        double start = vertical ? z.Y : z.X;
        double end = vertical ? z.Bottom : z.Right;
        bool onLine = vertical
            ? Near(z.Right, d.Position) || Near(z.X, d.Position)
            : Near(z.Bottom, d.Position) || Near(z.Y, d.Position);
        return onLine && Math.Min(end, d.End) - Math.Max(start, d.Start) > Eps;
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-3;
}
