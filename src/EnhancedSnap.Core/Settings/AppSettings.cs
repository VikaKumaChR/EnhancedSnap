using System.Text.Json.Serialization;

namespace EnhancedSnap.Core.Settings;

/// <summary>How the zones overlay is triggered while a window is being dragged.</summary>
public enum ActivationMode
{
    /// <summary>Zones appear only while Shift is held (FancyZones default, never fights Windows Snap).</summary>
    HoldShift,

    /// <summary>Zones appear on every drag; holding Shift temporarily bypasses them.</summary>
    Always,
}

/// <summary>Corner treatment applied to windows snapped by Enhanced Snap (DWMWA_WINDOW_CORNER_PREFERENCE).</summary>
public enum CornerStyle
{
    /// <summary>8 px Windows 11 radius (DWMWCP_ROUND). Matches Fluent 2 "Large" and Edge's content frame.</summary>
    Round,

    /// <summary>4 px radius (DWMWCP_ROUNDSMALL). Fluent 2 "Medium".</summary>
    RoundSmall,

    /// <summary>Do not touch the app's own preference.</summary>
    System,
}

/// <summary>Material of the zones overlay.</summary>
public enum MaterialMode
{
    /// <summary>Acrylic (host backdrop blur + tint). Falls back to solid automatically in Energy saver,
    /// when transparency effects are off, in high contrast and in remote sessions.</summary>
    Auto,

    /// <summary>Always solid tint: lowest GPU cost.</summary>
    Solid,
}

/// <summary>
/// Material of a snap group's frame — the base behind its windows. Fluent 2 puts Mica on long-lived base layers
/// and Acrylic on transient surfaces, so Mica is the default; every translucent choice turns solid when transparency
/// effects are off, in Energy saver, high contrast and remote sessions.
/// </summary>
public enum FrameMaterial
{
    /// <summary>Mica: opaque, tinted with the desktop wallpaper, which is sampled once (the cheapest translucency).</summary>
    Mica,

    /// <summary>Mica Alt: Mica with a stronger wallpaper tint.</summary>
    MicaAlt,

    /// <summary>Background acrylic: blurs the wallpaper and any windows behind the group (re-blurred as they change).</summary>
    Acrylic,

    /// <summary>The theme's solid base color.</summary>
    Solid,
}

/// <summary>Overlay motion.</summary>
public enum MotionMode
{
    /// <summary>Follow Windows "Animation effects"; turned off automatically in Energy saver.</summary>
    Auto,

    /// <summary>No animations.</summary>
    Off,
}

public enum LayoutOrientation
{
    /// <summary>Tracks are columns (left → right); each column is split into rows.</summary>
    Columns,

    /// <summary>Tracks are rows (top → bottom); each row is split into columns.</summary>
    Rows,
}

/// <summary>Persisted user settings (%LOCALAPPDATA%\EnhancedSnap\settings.json).</summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxSpacing = 48;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Master switch. When off the engine keeps running (tray) but never shows zones or handles keys.</summary>
    public bool Enabled { get; set; } = true;

    public ActivationMode Activation { get; set; } = ActivationMode.HoldShift;

    /// <summary>Hold Ctrl while dragging to merge the zones between the first and the current zone.</summary>
    public bool SpanWithCtrl { get; set; } = true;

    public bool ShowZonesOnAllMonitors { get; set; }

    /// <summary>Dragging a snapped window out restores the size it had before it was snapped.</summary>
    public bool RestoreSizeOnUnsnap { get; set; } = true;

    /// <summary>When gap, layout or the display configuration changes, move snapped windows to their new zone rectangles.</summary>
    public bool KeepWindowsInZones { get; set; } = true;

    /// <summary>Replace Win + Arrow (Windows Snap) with zone navigation. Uses a low-level keyboard hook.</summary>
    public bool OverrideWinArrows { get; set; } = true;

    /// <summary>Win + Left/Right continues onto the neighbouring monitor at the edge of a monitor.</summary>
    public bool MoveAcrossMonitors { get; set; } = true;

    /// <summary>
    /// Turn Windows Snap off while Enhanced Snap runs (the "Snap windows" switch in Settings → System → Multitasking)
    /// and restore the previous value when Enhanced Snap pauses or exits.
    /// </summary>
    public bool ReplaceWindowsSnap { get; set; } = true;

    /// <summary>Drag a window to a screen edge or corner to snap it to a half or quarter (Windows Snap parity).</summary>
    public bool EdgeSnapping { get; set; } = true;

    /// <summary>Snap layouts flyout when hovering a window's maximize button or pressing Win + Z.</summary>
    public bool LayoutsOnMaximizeHover { get; set; } = true;

    /// <summary>Snap layouts bar at the top of the screen while dragging a window.</summary>
    public bool SnapBar { get; set; } = true;

    /// <summary>After snapping, offer the other open windows for the remaining zones.</summary>
    public bool SnapAssist { get; set; } = true;

    /// <summary>Draw the Edge-style rounded frame with resize dividers around snap groups.</summary>
    public bool GroupFrame { get; set; } = true;

    public FrameMaterial GroupFrameMaterial { get; set; } = FrameMaterial.Mica;

    /// <summary>Show each snap group in the taskbar thumbnails of the apps it contains (like Windows' snap groups).</summary>
    public bool GroupsInTaskbar { get; set; } = true;

    /// <summary>
    /// Run the engine elevated (a scheduled task with highest privileges, registered once with a UAC prompt) so
    /// windows of apps running as administrator can be snapped and grouped too.
    /// </summary>
    public bool RunAsAdministrator { get; set; }

    /// <summary>Space between snapped windows in effective pixels (scaled by monitor DPI).</summary>
    public int Gap { get; set; } = 8;

    /// <summary>Space between snapped windows and the work-area edges in effective pixels.</summary>
    public int Margin { get; set; } = 8;

    public CornerStyle Corners { get; set; } = CornerStyle.Round;

    public MaterialMode Material { get; set; } = MaterialMode.Auto;

    public MotionMode Motion { get; set; } = MotionMode.Auto;

    /// <summary>UI language tag ("en", "zh-Hant", "zh-Hans") or empty to follow Windows.</summary>
    public string Language { get; set; } = "";

    /// <summary>Process names (or fragments) that are never snapped. "notepad" matches notepad.exe and notepad++.exe;
    /// "notepad.exe" matches only notepad.exe.</summary>
    public List<string> ExcludedApps { get; set; } = [];

    /// <summary>Per-monitor layout choice keyed by the stable monitor device path.</summary>
    public Dictionary<string, MonitorSettings> Monitors { get; set; } = [];

    public List<CustomLayout> CustomLayouts { get; set; } = [];

    /// <summary>Set by the settings app once the welcome tip has been dismissed.</summary>
    public bool WelcomeDismissed { get; set; }

    /// <summary>Clamp values coming from disk so a hand-edited file can never break the engine.</summary>
    public void Normalize()
    {
        Gap = Math.Clamp(Gap, 0, MaxSpacing);
        Margin = Math.Clamp(Margin, 0, MaxSpacing);
        ExcludedApps = ExcludedApps
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .Select(static s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Monitors ??= [];
        CustomLayouts ??= [];
        foreach (var layout in CustomLayouts)
        {
            layout.Normalize();
        }

        CustomLayouts.RemoveAll(static l => string.IsNullOrWhiteSpace(l.Id));
        Language ??= "";
        SchemaVersion = CurrentSchemaVersion;
    }

    public AppSettings Clone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this, SettingsJsonContext.Default.AppSettings);
        return System.Text.Json.JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!;
    }
}

public sealed class MonitorSettings
{
    public string LayoutId { get; set; } = "";

    /// <summary>Friendly name saved for humans reading the JSON file; never used for lookups.</summary>
    public string? Name { get; set; }
}

/// <summary>
/// User-defined layout: a list of tracks (columns or rows), each with a relative weight
/// and a number of equal splits along the other axis. Covers "main + stack", 2/3 + 1/3,
/// ultrawide thirds, portrait stacks, etc. while staying resolution independent.
/// </summary>
public sealed class CustomLayout
{
    public const int MaxTracks = 6;
    public const int MaxSplits = 4;

    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public LayoutOrientation Orientation { get; set; } = LayoutOrientation.Columns;

    public List<LayoutTrack> Tracks { get; set; } = [];

    // UPDATE: Free-form "canvas" layouts (overlapping zones, absolute rectangles) like the FancyZones canvas editor.
    // The engine already works with arbitrary fractional rectangles (ZoneFrac), only the editor is missing.

    public void Normalize()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Custom" : Name.Trim();
        Tracks ??= [];
        if (Tracks.Count == 0)
        {
            Tracks.Add(new LayoutTrack());
        }

        if (Tracks.Count > MaxTracks)
        {
            Tracks.RemoveRange(MaxTracks, Tracks.Count - MaxTracks);
        }

        foreach (var t in Tracks)
        {
            t.Weight = double.IsFinite(t.Weight) ? Math.Clamp(t.Weight, 0.25, 10) : 1;
            t.Splits = Math.Clamp(t.Splits, 1, MaxSplits);
        }
    }
}

public sealed class LayoutTrack
{
    public double Weight { get; set; } = 1;

    public int Splits { get; set; } = 1;
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
public sealed partial class SettingsJsonContext : JsonSerializerContext;
