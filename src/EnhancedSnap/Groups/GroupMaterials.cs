using System.Numerics;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Engine.Ui;
using Windows.UI.Composition;

namespace EnhancedSnap.Engine.Groups;

/// <summary>
/// Fluent materials for the group surfaces. The wallpaper brush (what Mica is made of: the desktop wallpaper,
/// blurred once by the system) and the host backdrop brush (background acrylic) are rendered by DWM; one instance
/// of each is shared by every frame, divider and placeholder. Windows hosting them set DWMWA_USE_HOSTBACKDROPBRUSH.
/// </summary>
internal sealed class GroupMaterials(Compositor compositor)
{
    private CompositionBrush? _wallpaper;
    private bool _wallpaperTried;
    private CompositionBrush? _backdrop;

    /// <summary>A rounded rectangle filled with the material, clipped with an anti-aliased geometric clip.</summary>
    public ContainerVisual Fill(MaterialRecipe recipe, Vector2 offset, Vector2 size, float radius)
    {
        var c = compositor;
        var container = c.CreateContainerVisual();
        container.Offset = new Vector3(offset, 0);
        container.Size = size;
        if (radius > 0)
        {
            var geometry = c.CreateRoundedRectangleGeometry();
            geometry.Size = size;
            geometry.CornerRadius = new Vector2(radius);
            container.Clip = c.CreateGeometricClip(geometry);
        }

        var source = recipe.Source switch
        {
            MaterialSource.Wallpaper => Wallpaper ?? Backdrop,
            MaterialSource.Backdrop => Backdrop,
            _ => null,
        };

        if (source is not null)
        {
            container.Children.InsertAtTop(Layer(source));
            container.Children.InsertAtTop(Layer(c.CreateColorBrush(recipe.Tint)));
        }
        else
        {
            container.Children.InsertAtTop(Layer(c.CreateColorBrush(recipe.Fallback)));
        }

        return container;
    }

    private CompositionBrush Backdrop => _backdrop ??= compositor.CreateHostBackdropBrush();

    private CompositionBrush? Wallpaper
    {
        get
        {
            if (!_wallpaperTried)
            {
                _wallpaperTried = true;
                try
                {
                    _wallpaper = compositor.TryCreateBlurredWallpaperBackdropBrush();
                }
                catch (Exception ex)
                {
                    Log.Warn($"Wallpaper backdrop unavailable, using acrylic for Mica: {ex.Message}");
                }
            }

            return _wallpaper;
        }
    }

    private SpriteVisual Layer(CompositionBrush brush)
    {
        var sprite = compositor.CreateSpriteVisual();
        sprite.Brush = brush;
        sprite.RelativeSizeAdjustment = Vector2.One;
        return sprite;
    }
}
