using EnhancedSnap.Core.Settings;

namespace EnhancedSnap.Core.Layouts;

/// <summary>Built-in layout templates plus resolution of user-defined layouts.</summary>
public static class LayoutCatalog
{
    public const string Halves = "halves";
    public const string Thirds = "thirds";
    public const string LeftTwoThirds = "left-two-thirds";
    public const string RightTwoThirds = "right-two-thirds";
    public const string CenterFocus = "center-focus";
    public const string MainStack = "main-stack";
    public const string HalfStack = "half-stack";
    public const string Quarters = "quarters";
    public const string Grid3x2 = "grid-3x2";
    public const string Rows = "rows";
    public const string Full = "full";

    private const double Third = 1.0 / 3.0;

    /// <summary>Built-in templates in gallery order.</summary>
    public static IReadOnlyList<LayoutDefinition> BuiltIns { get; } =
    [
        new(Halves, Columns(1, 1)),
        new(Thirds, Columns(1, 1, 1)),
        new(LeftTwoThirds, Columns(2, 1)),
        new(RightTwoThirds, Columns(1, 2)),
        new(CenterFocus, Columns(1, 2, 1)),
        new(MainStack,
        [
            new ZoneFrac(0, 0, 0.6, 1),
            new ZoneFrac(0.6, 0, 0.4, 0.5),
            new ZoneFrac(0.6, 0.5, 0.4, 0.5),
        ]),
        new(HalfStack, // Windows' own "one + two stacked": a half, and the other half split in two
        [
            new ZoneFrac(0, 0, 0.5, 1),
            new ZoneFrac(0.5, 0, 0.5, 0.5),
            new ZoneFrac(0.5, 0.5, 0.5, 0.5),
        ]),
        new(Quarters,
        [
            new ZoneFrac(0, 0, 0.5, 0.5),
            new ZoneFrac(0.5, 0, 0.5, 0.5),
            new ZoneFrac(0, 0.5, 0.5, 0.5),
            new ZoneFrac(0.5, 0.5, 0.5, 0.5),
        ]),
        new(Grid3x2,
        [
            new ZoneFrac(0, 0, Third, 0.5),
            new ZoneFrac(Third, 0, Third, 0.5),
            new ZoneFrac(2 * Third, 0, Third, 0.5),
            new ZoneFrac(0, 0.5, Third, 0.5),
            new ZoneFrac(Third, 0.5, Third, 0.5),
            new ZoneFrac(2 * Third, 0.5, Third, 0.5),
        ]),
        new(Rows,
        [
            new ZoneFrac(0, 0, 1, 0.5),
            new ZoneFrac(0, 0.5, 1, 0.5),
        ]),
        new(Full, [new ZoneFrac(0, 0, 1, 1)]),
    ];

    // UPDATE: Per-virtual-desktop layouts (IVirtualDesktopManager.GetWindowDesktopId) — today a monitor has
    // one layout across all desktops.

    public static LayoutDefinition? FindBuiltIn(string id) =>
        BuiltIns.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Default for monitors the user has not configured yet, picked from the monitor's shape.</summary>
    public static string DefaultLayoutIdFor(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return Halves;
        }

        double aspect = (double)width / height;
        if (aspect >= 2.1)
        {
            return CenterFocus; // 21:9 and 32:9 ultrawides
        }

        return aspect < 1 ? Rows : Halves;
    }

    /// <summary>Resolve the layout for a monitor: its saved choice, else the shape-based default.</summary>
    public static LayoutDefinition ResolveForMonitor(AppSettings settings, string monitorId, int width, int height)
    {
        if (settings.Monitors.TryGetValue(monitorId, out var monitor) && !string.IsNullOrEmpty(monitor.LayoutId))
        {
            var resolved = Resolve(settings, monitor.LayoutId);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return FindBuiltIn(DefaultLayoutIdFor(width, height))!;
    }

    public static LayoutDefinition? Resolve(AppSettings settings, string layoutId)
    {
        var builtIn = FindBuiltIn(layoutId);
        if (builtIn is not null)
        {
            return builtIn;
        }

        var custom = settings.CustomLayouts.FirstOrDefault(c => c.Id == layoutId);
        return custom is null ? null : FromCustom(custom);
    }

    public static LayoutDefinition FromCustom(CustomLayout custom)
    {
        custom.Normalize();
        double total = custom.Tracks.Sum(static t => t.Weight);
        var zones = new List<ZoneFrac>();
        double start = 0;
        foreach (var track in custom.Tracks)
        {
            double size = track.Weight / total;
            for (int i = 0; i < track.Splits; i++)
            {
                double splitStart = (double)i / track.Splits;
                double splitSize = 1.0 / track.Splits;
                zones.Add(custom.Orientation == LayoutOrientation.Columns
                    ? new ZoneFrac(start, splitStart, size, splitSize)
                    : new ZoneFrac(splitStart, start, splitSize, size));
            }

            start += size;
        }

        return new LayoutDefinition(custom.Id, [.. zones], isCustom: true, customName: custom.Name);
    }

    public static string NewCustomLayoutId() => "custom-" + Guid.NewGuid().ToString("N")[..12];

    private static ZoneFrac[] Columns(params double[] weights)
    {
        double total = weights.Sum();
        var zones = new ZoneFrac[weights.Length];
        double x = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            double w = weights[i] / total;
            zones[i] = new ZoneFrac(x, 0, w, 1);
            x += w;
        }

        return zones;
    }
}
