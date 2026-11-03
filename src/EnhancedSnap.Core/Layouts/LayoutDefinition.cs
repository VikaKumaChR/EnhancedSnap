namespace EnhancedSnap.Core.Layouts;

/// <summary>A zone expressed as fractions (0..1) of the usable work area.</summary>
public readonly record struct ZoneFrac(double X, double Y, double W, double H)
{
    public double Right => X + W;

    public double Bottom => Y + H;
}

public enum Direction
{
    Left,
    Right,
    Up,
    Down,
}

/// <summary>An immutable, resolution-independent layout (built-in template or user-defined).</summary>
public sealed class LayoutDefinition
{
    public LayoutDefinition(string id, ZoneFrac[] zones, bool isCustom = false, string? customName = null)
    {
        Id = id;
        Zones = zones;
        IsCustom = isCustom;
        CustomName = customName;
    }

    public string Id { get; }

    public IReadOnlyList<ZoneFrac> Zones { get; }

    public bool IsCustom { get; }

    /// <summary>User-provided name for custom layouts; built-in names come from localization.</summary>
    public string? CustomName { get; }

    public override string ToString() => $"{Id} ({Zones.Count} zones)";
}
