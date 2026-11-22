using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Core.Layouts;

namespace EnhancedSnap.Engine.Groups;

/// <summary>
/// Windows snapped together into one layout on one monitor (Windows "snap group"). The group owns its zone
/// fractions, so dragging a divider changes the group without touching the monitor's saved layout.
/// </summary>
internal sealed class SnapGroup(string monitorId, LayoutDefinition layout, ZoneFrac[] zones)
{
    public Guid Id { get; } = Guid.NewGuid();

    public string MonitorId { get; set; } = monitorId;

    public LayoutDefinition Layout { get; } = layout;

    /// <summary>Current (divider-adjusted) zone fractions.</summary>
    public ZoneFrac[] Zones { get; set; } = zones;

    /// <summary>Member window → zone indices (several when the window spans zones).</summary>
    public Dictionary<nint, int[]> Members { get; } = [];

    /// <summary>Members by most recent activation first (drives z-order when the group is raised).</summary>
    public List<nint> RecentOrder { get; } = [];

    public long LastUsed { get; set; } = Environment.TickCount64;

    public IEnumerable<int> OccupiedZones => Members.Values.SelectMany(static z => z);

    public int[] EmptyZones() => [.. Enumerable.Range(0, Zones.Length).Except(OccupiedZones)];

    /// <summary>Fraction rectangle a member covers (union of its zones).</summary>
    public ZoneFrac MemberFraction(int[] zoneIndices)
    {
        double left = zoneIndices.Min(i => Zones[i].X);
        double top = zoneIndices.Min(i => Zones[i].Y);
        double right = zoneIndices.Max(i => Zones[i].Right);
        double bottom = zoneIndices.Max(i => Zones[i].Bottom);
        return new ZoneFrac(left, top, right - left, bottom - top);
    }

    /// <summary>Dividers between members only (never between a member and an empty zone).</summary>
    public List<Divider> MemberDividers() =>
        GroupGeometry.FindDividers(Members.Values.Select(MemberFraction).ToList());

    public void Touch(nint hwnd)
    {
        LastUsed = Environment.TickCount64;
        RecentOrder.Remove(hwnd);
        RecentOrder.Insert(0, hwnd);
    }

    public override string ToString() => $"group {Id.ToString()[..8]} {Layout.Id} members={Members.Count}";
}

/// <summary>
/// A divider of a group in screen pixels: <see cref="HitRect"/> is the gap between the windows (its centre is where the
/// divider is), <see cref="StripParts"/> that gap less any window reaching into it, and <see cref="Caps"/> the targets
/// at its ends (<see cref="DividerHitGeometry"/>): parts and caps are all of it that can be grabbed (no parts given:
/// the whole gap). <see cref="Bar"/> is the slightly wider bar shown over the seam while it is hovered or dragged, only
/// drawn: the windows' edges under it stay theirs.
/// </summary>
internal readonly record struct DividerHandle(Divider Divider, RectI HitRect, RectI Bar, RectI[]? Caps = null, RectI[]? StripParts = null)
{
    public static DividerHandle From(DividerHit hit) => new(hit.Divider, hit.Strip, hit.Bar, hit.Caps, hit.StripParts);

    /// <summary>What of the gap can be grabbed.</summary>
    public RectI[] Strip => StripParts ?? [HitRect];

    public bool StripContains(PointI point) => AnyContains(Strip, point);

    public bool CapsContain(PointI point) => AnyContains(Caps, point);

    /// <summary>Value equality, caps and parts included (the record's own equality compares arrays by reference).</summary>
    public bool SameAs(DividerHandle other) =>
        Divider == other.Divider && HitRect == other.HitRect && Bar == other.Bar
        && (Caps ?? []).AsSpan().SequenceEqual(other.Caps ?? []) && Strip.AsSpan().SequenceEqual(other.Strip);

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
