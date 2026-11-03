using System.Numerics;
using EnhancedSnap.Core.Settings;
using EnhancedSnap.Engine.Platform;
using Windows.UI;
using Windows.UI.Composition;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Ui;

/// <summary>Where a material's translucency comes from.</summary>
internal enum MaterialSource
{
    /// <summary>Opaque fallback color only.</summary>
    Solid,

    /// <summary>The blurred desktop wallpaper, sampled once by the system (Mica).</summary>
    Wallpaper,

    /// <summary>Live blur of whatever is behind the window (background acrylic).</summary>
    Backdrop,
}

/// <summary>A Fluent material resolved for the theme and power state: source + tint layer, or the solid fallback.</summary>
internal sealed record MaterialRecipe(MaterialSource Source, Color Tint, Color Fallback);

/// <summary>
/// Fluent 2 tokens for Enhanced Snap's own surfaces (snap layouts flyout, Snap Assist, group frame, dividers).
/// Follows the app theme like Windows' snap flyout and Edge's frame. Materials follow the Fluent guidance — Mica for
/// the long-lived group frame, Acrylic for transient surfaces (the hover divider) — and turn solid in Energy saver,
/// with transparency off, in high contrast and over Remote Desktop, like Mica and Acrylic do in WinUI.
/// </summary>
internal sealed record FluentTheme(
    bool Dark,
    bool HighContrast,
    bool Acrylic,
    bool Animate,
    Color PanelTint,
    Color PanelStroke,
    Color CardFill,
    Color CardHover,
    Color CardStroke,
    Color ZoneFill,
    Color ZoneStroke,
    Color ZoneAppFill,
    Color Accent,
    Color Text,
    Color TextSecondary,
    MaterialRecipe Frame,
    Color FrameStroke,
    MaterialRecipe Divider,
    Color DividerStroke,
    Color Grip,
    MaterialRecipe Placeholder)
{
    public static FluentTheme Create(SystemState system, AppSettings settings)
    {
        bool animate = settings.Motion == MotionMode.Auto && system.AnimationsEnabled && !system.EnergySaverActive && !system.RemoteSession;
        bool translucent = system.TransparencyEnabled && !system.EnergySaverActive && !system.RemoteSession;
        bool acrylic = settings.Material == MaterialMode.Auto && translucent;

        if (system.HighContrast)
        {
            Color window = SystemState.FromColorRef(GetSysColor(COLOR_WINDOW));
            Color text = SystemState.FromColorRef(GetSysColor(COLOR_WINDOWTEXT));
            Color highlight = SystemState.FromColorRef(GetSysColor(COLOR_HIGHLIGHT));
            var solid = new MaterialRecipe(MaterialSource.Solid, window, window);
            return new FluentTheme(
                false, true, false, animate, window, text, window, highlight, text, window, text, window, highlight, text, text,
                solid, text, new MaterialRecipe(MaterialSource.Solid, highlight, highlight), text, text, solid);
        }

        bool dark = system.AppDarkMode;

        // Tint layers approximate the Fluent recipes (WinUI MicaController / AcrylicBrush tint and luminosity) with a
        // single translucent color over the source; the fallbacks are SolidBackgroundFillColorBase / BaseAlt.
        // UPDATE: The exact recipes (luminosity blend + 2 % noise texture) need a composition effect graph; the noise
        // also needs a drawing surface kept alive for every frame, which would keep a graphics device in the engine.
        var frame = settings.GroupFrameMaterial switch
        {
            FrameMaterial.MicaAlt => dark
                ? Recipe(translucent, MaterialSource.Wallpaper, Color.FromArgb(158, 10, 10, 10), Color.FromArgb(255, 10, 10, 10))
                : Recipe(translucent, MaterialSource.Wallpaper, Color.FromArgb(158, 218, 218, 218), Color.FromArgb(255, 218, 218, 218)),
            FrameMaterial.Acrylic => dark
                ? Recipe(translucent, MaterialSource.Backdrop, Color.FromArgb(199, 44, 44, 44), Color.FromArgb(255, 44, 44, 44))
                : Recipe(translucent, MaterialSource.Backdrop, Color.FromArgb(184, 252, 252, 252), Color.FromArgb(255, 249, 249, 249)),
            FrameMaterial.Solid => dark
                ? Recipe(false, MaterialSource.Solid, default, Color.FromArgb(255, 32, 32, 32))
                : Recipe(false, MaterialSource.Solid, default, Color.FromArgb(255, 243, 243, 243)),
            _ => dark
                ? Recipe(translucent, MaterialSource.Wallpaper, Color.FromArgb(219, 32, 32, 32), Color.FromArgb(255, 32, 32, 32))
                : Recipe(translucent, MaterialSource.Wallpaper, Color.FromArgb(214, 243, 243, 243), Color.FromArgb(255, 243, 243, 243)),
        };

        // The hover divider is transient UI, so background acrylic (like Windows' own snap divider bar).
        var divider = dark
            ? Recipe(acrylic, MaterialSource.Backdrop, Color.FromArgb(214, 44, 44, 44), Color.FromArgb(255, 58, 58, 58))
            : Recipe(acrylic, MaterialSource.Backdrop, Color.FromArgb(219, 228, 228, 228), Color.FromArgb(255, 227, 227, 227));

        // Placeholders while a divider is dragged: the blurred wallpaper, lightly tinted (what Windows shows).
        var placeholder = dark
            ? Recipe(translucent, MaterialSource.Wallpaper, Color.FromArgb(77, 0, 0, 0), Color.FromArgb(255, 43, 43, 43))
            : Recipe(translucent, MaterialSource.Wallpaper, Color.FromArgb(64, 255, 255, 255), Color.FromArgb(255, 229, 229, 229));

        if (dark)
        {
            return new FluentTheme(
                Dark: true,
                HighContrast: false,
                Acrylic: acrylic,
                Animate: animate,
                PanelTint: Color.FromArgb(acrylic ? (byte)214 : (byte)255, 44, 44, 44),
                PanelStroke: Color.FromArgb(28, 255, 255, 255),
                CardFill: Color.FromArgb(16, 255, 255, 255),
                CardHover: Color.FromArgb(28, 255, 255, 255),
                CardStroke: Color.FromArgb(18, 255, 255, 255),
                ZoneFill: Color.FromArgb(18, 255, 255, 255),    // ControlAltFillColorQuarternary (Windows' layout zones)
                ZoneStroke: Color.FromArgb(139, 255, 255, 255), // ControlStrongStrokeColorDefault
                ZoneAppFill: Color.FromArgb(40, 255, 255, 255), // a suggested app's zone, lighter, as in Windows' snap suggestions
                Accent: system.AccentLight2,
                Text: Color.FromArgb(255, 255, 255, 255),
                TextSecondary: Color.FromArgb(200, 255, 255, 255),
                Frame: frame,
                FrameStroke: Color.FromArgb(22, 255, 255, 255),
                Divider: divider,
                DividerStroke: Color.FromArgb(24, 255, 255, 255),
                Grip: Color.FromArgb(235, 0, 0, 0),
                Placeholder: placeholder);
        }

        return new FluentTheme(
            Dark: false,
            HighContrast: false,
            Acrylic: acrylic,
            Animate: animate,
            PanelTint: Color.FromArgb(acrylic ? (byte)210 : (byte)255, 243, 243, 243), // AcrylicBackgroundFillColorBase, as Windows' flyout
            PanelStroke: Color.FromArgb(18, 0, 0, 0),
            CardFill: Color.FromArgb(178, 255, 255, 255),
            CardHover: Color.FromArgb(255, 255, 255, 255),
            CardStroke: Color.FromArgb(16, 0, 0, 0),
            ZoneFill: Color.FromArgb(24, 0, 0, 0),      // ControlAltFillColorQuarternary: #D9D9D9 on the panel, as Windows draws it
            ZoneStroke: Color.FromArgb(114, 0, 0, 0),   // ControlStrongStrokeColorDefault: the #888 outline
            ZoneAppFill: Color.FromArgb(179, 255, 255, 255), // ControlFillColorDefault: a suggested app's zone, as in Windows' snap suggestions
            Accent: system.AccentDark1,
            Text: Color.FromArgb(228, 0, 0, 0),
            TextSecondary: Color.FromArgb(158, 0, 0, 0),
            Frame: frame,
            FrameStroke: Color.FromArgb(22, 0, 0, 0),
            Divider: divider,
            DividerStroke: Color.FromArgb(16, 0, 0, 0),
            Grip: Color.FromArgb(140, 0, 0, 0),
            Placeholder: placeholder);
    }

    /// <summary>Text color on top of <see cref="Accent"/>.</summary>
    public Color OnAccent => HighContrast ? Text : Dark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 255, 255, 255);

    private static MaterialRecipe Recipe(bool translucent, MaterialSource source, Color tint, Color fallback) =>
        translucent ? new MaterialRecipe(source, tint, fallback) : new MaterialRecipe(MaterialSource.Solid, fallback, fallback);
}

/// <summary>Small composition helpers shared by the popups and the group frame.</summary>
internal static class Shapes
{
    /// <summary>Rounded rectangle with optional fill/stroke, sized in pixels; returns the visual and its sprite shape.</summary>
    public static (ShapeVisual Visual, CompositionSpriteShape Shape, CompositionRoundedRectangleGeometry Geometry) RoundedRect(
        Compositor c, Vector2 offset, Vector2 size, float radius, Color? fill, Color? stroke, float strokeThickness = 1)
    {
        var visual = c.CreateShapeVisual();
        visual.Offset = new Vector3(offset, 0);
        visual.Size = size;
        var geometry = c.CreateRoundedRectangleGeometry();
        float inset = stroke is null ? 0 : strokeThickness / 2f;
        geometry.Offset = new Vector2(inset);
        geometry.Size = Vector2.Max(Vector2.Zero, size - new Vector2(inset * 2));
        geometry.CornerRadius = new Vector2(MathF.Max(0, radius - inset));
        var shape = c.CreateSpriteShape(geometry);
        if (fill is { } f)
        {
            shape.FillBrush = c.CreateColorBrush(f);
        }

        if (stroke is { } s)
        {
            shape.StrokeBrush = c.CreateColorBrush(s);
            shape.StrokeThickness = strokeThickness;
        }

        visual.Shapes.Add(shape);
        return (visual, shape, geometry);
    }

    /// <summary>A sprite showing a drawing surface at its native pixel size.</summary>
    public static SpriteVisual Surface(Compositor c, ICompositionSurface? surface, Vector2 offset, Vector2 size)
    {
        var sprite = c.CreateSpriteVisual();
        sprite.Offset = new Vector3(offset, 0);
        sprite.Size = size;
        if (surface is not null)
        {
            var brush = c.CreateSurfaceBrush(surface);
            brush.Stretch = CompositionStretch.None;
            brush.HorizontalAlignmentRatio = 0;
            brush.VerticalAlignmentRatio = 0;
            sprite.Brush = brush;
        }

        return sprite;
    }

    public static ScalarKeyFrameAnimation Fade(Compositor c, float to, TimeSpan duration, bool decelerate = true)
    {
        var animation = c.CreateScalarKeyFrameAnimation();
        var curve = decelerate ? Overlay.FluentMotion.Decelerate : Overlay.FluentMotion.Accelerate;
        animation.InsertKeyFrame(1f, to, c.CreateCubicBezierEasingFunction(curve.C1, curve.C2));
        animation.Duration = duration;
        return animation;
    }
}
