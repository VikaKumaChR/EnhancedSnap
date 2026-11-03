using System.Numerics;
using EnhancedSnap.Core.Settings;
using EnhancedSnap.Engine.Platform;
using Windows.UI;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Overlay;

/// <summary>
/// Fluent 2 tokens for the zones overlay, resolved for the current theme and power state.
/// Material choice follows Fluent guidance: Acrylic for transient surfaces, with an automatic solid
/// fallback (like WinUI's AcrylicBrush) when transparency is off, in Energy saver, high contrast or RDP.
/// </summary>
internal sealed record OverlayPalette(
    bool UseAcrylic,
    bool Animate,
    bool HighContrast,
    Color TileFill,
    Color TileStroke,
    float TileStrokeEpx,
    Color TargetFill,
    Color TargetStroke,
    float TargetStrokeEpx,
    float CornerRadiusEpx)
{
    /// <summary>
    /// Windows Snap's own drop preview (screen edges, a group's empty space): a neutral frosted tile with a thin
    /// outline instead of the accent drop target of the Shift + drag zones.
    /// </summary>
    public Color PreviewFill { get; init; }

    public Color PreviewStroke { get; init; }

    public static OverlayPalette Create(SystemState system, AppSettings settings)
    {
        float radius = settings.Corners == CornerStyle.RoundSmall ? 4f : 8f; // Fluent 2 Medium / Large; Windows 11 window radius
        bool animate = settings.Motion == MotionMode.Auto && system.AnimationsEnabled && !system.EnergySaverActive && !system.RemoteSession;

        if (system.HighContrast)
        {
            // System colors only, fully opaque, thicker strokes — readable in every contrast theme.
            return new OverlayPalette(
                UseAcrylic: false,
                Animate: animate,
                HighContrast: true,
                TileFill: SystemState.FromColorRef(GetSysColor(COLOR_WINDOW)),
                TileStroke: SystemState.FromColorRef(GetSysColor(COLOR_WINDOWTEXT)),
                TileStrokeEpx: 2,
                TargetFill: SystemState.FromColorRef(GetSysColor(COLOR_HIGHLIGHT)),
                TargetStroke: SystemState.FromColorRef(GetSysColor(COLOR_HIGHLIGHTTEXT)),
                TargetStrokeEpx: 3,
                CornerRadiusEpx: radius)
            {
                PreviewFill = SystemState.FromColorRef(GetSysColor(COLOR_HIGHLIGHT)),
                PreviewStroke = SystemState.FromColorRef(GetSysColor(COLOR_HIGHLIGHTTEXT)),
            };
        }

        bool acrylic = settings.Material == MaterialMode.Auto
            && system.TransparencyEnabled
            && !system.EnergySaverActive
            && !system.RemoteSession;

        // UPDATE: Full Fluent acrylic recipe (luminosity blend + noise) needs an IGraphicsEffect chain
        // (Win2D-style). Host backdrop blur + tint layer is the lightweight approximation used here.
        if (system.AppDarkMode)
        {
            var accent = system.AccentLight2;
            return new OverlayPalette(
                UseAcrylic: acrylic,
                Animate: animate,
                HighContrast: false,
                TileFill: acrylic ? Color.FromArgb(150, 32, 32, 32) : Color.FromArgb(232, 44, 44, 44),
                TileStroke: Color.FromArgb(36, 255, 255, 255),
                TileStrokeEpx: 1,
                TargetFill: WithAlpha(accent, acrylic ? (byte)70 : (byte)96),
                TargetStroke: WithAlpha(accent, 235),
                TargetStrokeEpx: 2,
                CornerRadiusEpx: radius)
            {
                PreviewFill = Color.FromArgb(acrylic ? (byte)24 : (byte)40, 255, 255, 255),
                PreviewStroke = Color.FromArgb(48, 255, 255, 255),
            };
        }
        else
        {
            var accent = system.AccentDark1;
            return new OverlayPalette(
                UseAcrylic: acrylic,
                Animate: animate,
                HighContrast: false,
                TileFill: acrylic ? Color.FromArgb(140, 243, 243, 243) : Color.FromArgb(232, 243, 243, 243),
                TileStroke: Color.FromArgb(28, 0, 0, 0),
                TileStrokeEpx: 1,
                TargetFill: WithAlpha(accent, acrylic ? (byte)64 : (byte)84),
                TargetStroke: WithAlpha(accent, 235),
                TargetStrokeEpx: 2,
                CornerRadiusEpx: radius)
            {
                PreviewFill = Color.FromArgb(acrylic ? (byte)72 : (byte)110, 255, 255, 255),
                PreviewStroke = Color.FromArgb(40, 0, 0, 0),
            };
        }
    }

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
}

/// <summary>Fluent motion tokens (WinUI control timings), used by the compositor animations.</summary>
internal static class FluentMotion
{
    public static readonly TimeSpan Enter = TimeSpan.FromMilliseconds(167);
    public static readonly TimeSpan Exit = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan Glide = TimeSpan.FromMilliseconds(150);

    /// <summary>Decelerate ("fast out, slow in"): cubic-bezier(0, 0, 0, 1).</summary>
    public static readonly (Vector2 C1, Vector2 C2) Decelerate = (new Vector2(0f, 0f), new Vector2(0f, 1f));

    /// <summary>Accelerate for exits: cubic-bezier(0.7, 0, 1, 0.5).</summary>
    public static readonly (Vector2 C1, Vector2 C2) Accelerate = (new Vector2(0.7f, 0f), new Vector2(1f, 0.5f));
}
