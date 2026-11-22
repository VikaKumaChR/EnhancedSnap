using EnhancedSnap.Core.Geometry;

namespace EnhancedSnap.Core.Displays;

/// <summary>How Windows fits the desktop wallpaper: the values of DESKTOP_WALLPAPER_POSITION.</summary>
public enum WallpaperFit
{
    Center = 0,
    Tile = 1,
    Stretch = 2,
    Fit = 3,
    Fill = 4,
    Span = 5,
}

/// <summary>A rectangle in pixels that may be fractional or reach past the monitor.</summary>
public readonly record struct WallpaperRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public WallpaperRect Scale(double k) => new(X * k, Y * k, Width * k, Height * k);

    public WallpaperRect Intersect(WallpaperRect other)
    {
        double x = Math.Max(X, other.X);
        double y = Math.Max(Y, other.Y);
        double right = Math.Min(Right, other.Right);
        double bottom = Math.Min(Bottom, other.Bottom);
        return right > x && bottom > y ? new WallpaperRect(x, y, right - x, bottom - y) : default;
    }
}

/// <summary>
/// What to decode for a desktop preview, and where it shows (<see cref="WallpaperLayout.Plan"/>): scale the stored frame
/// to <see cref="ScaledWidth"/> × <see cref="ScaledHeight"/>, decode only <see cref="Bounds"/> of it, and turn that part
/// upright with <see cref="WallpaperLayout.Orient"/>.
/// </summary>
/// <param name="ScaledWidth">Width the stored frame is scaled to: the picture's size in the preview, before its EXIF orientation turns it.</param>
/// <param name="ScaledHeight">Height the stored frame is scaled to.</param>
/// <param name="Bounds">The part of the scaled stored frame to decode: what the monitor shows of the picture, about the preview's size at most.</param>
/// <param name="Width">Width of that part once upright: the bitmap's width.</param>
/// <param name="Height">Height of that part once upright.</param>
/// <param name="Placement">Where the bitmap shows, in fractions of the monitor; it may reach a little past the monitor.</param>
/// <param name="Tiled">Tile: the part is one tile, to be repeated over the whole preview from its top-left (<see cref="WallpaperLayout.TileInto"/>); <paramref name="Placement"/> is then the whole monitor.</param>
public readonly record struct WallpaperPlan(int ScaledWidth, int ScaledHeight, RectI Bounds, int Width, int Height, WallpaperRect Placement, bool Tiled);

/// <summary>
/// The desktop wallpaper as Windows lays it out on one monitor, for the settings app's previews (Suggestion01_01,
/// Suggestion01_02). Pure math, unit tested; reading and decoding the file is the settings app's job.
/// </summary>
public static class WallpaperLayout
{
    /// <summary>
    /// Longer side of a preview canvas: about 4 MB at 16:10, and sharp at the previews' largest size. A decoded part,
    /// rounded out to whole pixels so no sliver of the desktop color shows at its edges, can be 2 px more.
    /// </summary>
    public const int MaxPreviewSide = 1280;

    /// <summary>How far past a pixel's edge rounding noise may put a crop's edge before the crop takes in that pixel.</summary>
    private const double Noise = 1e-6;

    /// <summary>A bitmap size with the monitor's aspect ratio whose longer side is at most <paramref name="maxSide"/>.</summary>
    public static (int Width, int Height) PreviewSize(double monitorWidth, double monitorHeight, int maxSide = MaxPreviewSide)
    {
        if (monitorWidth <= 0 || monitorHeight <= 0)
        {
            return (maxSide, (int)Math.Round(maxSide / 1.6));
        }

        double k = Math.Min(1.0, maxSide / Math.Max(monitorWidth, monitorHeight));
        return (Math.Max(1, (int)Math.Round(monitorWidth * k)), Math.Max(1, (int)Math.Round(monitorHeight * k)));
    }

    /// <summary>
    /// Where Windows draws the image on the monitor, in the monitor's pixels; it may reach past the monitor (Fill,
    /// Center, Span) and is centred, like Windows does it. For <see cref="WallpaperFit.Tile"/> it is the first tile,
    /// at the monitor's top-left. <paramref name="monitorInDesktop"/> and <paramref name="desktop"/> (the bounding box of
    /// all monitors) are only used for <see cref="WallpaperFit.Span"/>, which fills that box; without them Span acts as
    /// Fill.
    /// </summary>
    public static WallpaperRect Place(
        WallpaperFit fit,
        double monitorWidth,
        double monitorHeight,
        double imageWidth,
        double imageHeight,
        WallpaperRect monitorInDesktop = default,
        WallpaperRect desktop = default)
    {
        double w = Math.Max(1, monitorWidth);
        double h = Math.Max(1, monitorHeight);
        double iw = Math.Max(1, imageWidth);
        double ih = Math.Max(1, imageHeight);
        switch (fit)
        {
            case WallpaperFit.Stretch:
                return new WallpaperRect(0, 0, w, h);
            case WallpaperFit.Fit:
            {
                double s = Math.Min(w / iw, h / ih);
                return Centered(w, h, iw * s, ih * s);
            }

            case WallpaperFit.Center:
                return Centered(w, h, iw, ih);
            case WallpaperFit.Tile:
                return new WallpaperRect(0, 0, iw, ih);
            case WallpaperFit.Span when !desktop.IsEmpty && !monitorInDesktop.IsEmpty:
            {
                double s = Math.Max(desktop.Width / iw, desktop.Height / ih);
                double dw = iw * s;
                double dh = ih * s;
                return new WallpaperRect(
                    desktop.X + ((desktop.Width - dw) / 2) - monitorInDesktop.X,
                    desktop.Y + ((desktop.Height - dh) / 2) - monitorInDesktop.Y,
                    dw,
                    dh);
            }

            default: // Fill, and Span without the desktop's layout
            {
                double s = Math.Max(w / iw, h / ih);
                return Centered(w, h, iw * s, ih * s);
            }
        }
    }

    /// <summary>
    /// What to decode for a preview of the monitor at <paramref name="canvasWidth"/> × <paramref name="canvasHeight"/>
    /// (<see cref="PreviewSize"/>), and where it shows. The picture is scaled to its size in the preview and only the part
    /// the monitor shows is decoded, so a decode is never much larger than the preview — a photo turned by its EXIF tag
    /// too: that part is found in the stored frame (<see cref="ToStoredFrame"/>) and only it is turned upright. Tile
    /// decodes one tile at its true scale, cut to the preview, so a tile larger than the monitor shows its top-left.
    /// <paramref name="monitor"/> and <paramref name="desktop"/> (the box of all monitors, for Span) are in desktop
    /// coordinates, negative left of and above the primary display. Null when nothing of the picture is on the monitor.
    /// </summary>
    public static WallpaperPlan? Plan(
        WallpaperFit fit,
        RectI monitor,
        RectI desktop,
        int storedWidth,
        int storedHeight,
        int exifOrientation,
        int canvasWidth,
        int canvasHeight)
    {
        if (monitor.IsEmpty || storedWidth <= 0 || storedHeight <= 0 || canvasWidth <= 0 || canvasHeight <= 0)
        {
            return null;
        }

        bool swap = SwapsSides(exifOrientation);
        int imageWidth = swap ? storedHeight : storedWidth; // as shown
        int imageHeight = swap ? storedWidth : storedHeight;
        var place = Place(
            fit,
            monitor.Width,
            monitor.Height,
            imageWidth,
            imageHeight,
            new WallpaperRect(monitor.Left - desktop.Left, monitor.Top - desktop.Top, monitor.Width, monitor.Height),
            new WallpaperRect(0, 0, desktop.Width, desktop.Height));

        // The picture in canvas pixels, decoded at that size: a decoded pixel is a canvas pixel, give or take rounding.
        double kx = canvasWidth / (double)monitor.Width;
        double ky = canvasHeight / (double)monitor.Height;
        var drawn = new WallpaperRect(place.X * kx, place.Y * ky, place.Width * kx, place.Height * ky);
        int scaledWidth = (int)Math.Max(1, Math.Round(drawn.Width));
        int scaledHeight = (int)Math.Max(1, Math.Round(drawn.Height));

        RectI crop;
        WallpaperRect shown;
        if (fit == WallpaperFit.Tile)
        {
            // A tile wider or taller than the canvas never repeats that way, so all that shows of it is its top-left.
            crop = RectI.FromSize(0, 0, Math.Min(scaledWidth, canvasWidth), Math.Min(scaledHeight, canvasHeight));
            shown = new WallpaperRect(0, 0, 1, 1);
        }
        else
        {
            var visible = drawn.Intersect(new WallpaperRect(0, 0, canvasWidth, canvasHeight));
            if (visible.IsEmpty)
            {
                return null;
            }

            // The decoded pixels under it, the far edges taken from its right and bottom: the scaled size is rounded, so a
            // decoded pixel is not quite a canvas pixel, and the crop must still cover all of what shows.
            double ux = scaledWidth / drawn.Width;
            double uy = scaledHeight / drawn.Height;
            int left = Math.Clamp((int)Math.Floor(((visible.X - drawn.X) * ux) + Noise), 0, scaledWidth - 1);
            int top = Math.Clamp((int)Math.Floor(((visible.Y - drawn.Y) * uy) + Noise), 0, scaledHeight - 1);
            int right = Math.Clamp((int)Math.Ceiling(((visible.Right - drawn.X) * ux) - Noise), left + 1, scaledWidth);
            int bottom = Math.Clamp((int)Math.Ceiling(((visible.Bottom - drawn.Y) * uy) - Noise), top + 1, scaledHeight);
            crop = new RectI(left, top, right, bottom);

            // Where those pixels show, in fractions of the monitor.
            var picture = new WallpaperRect(place.X / monitor.Width, place.Y / monitor.Height, place.Width / monitor.Width, place.Height / monitor.Height);
            shown = new WallpaperRect(
                picture.X + (picture.Width * left / scaledWidth),
                picture.Y + (picture.Height * top / scaledHeight),
                picture.Width * crop.Width / scaledWidth,
                picture.Height * crop.Height / scaledHeight);
        }

        return new WallpaperPlan(
            swap ? scaledHeight : scaledWidth,
            swap ? scaledWidth : scaledHeight,
            ToStoredFrame(crop, scaledWidth, scaledHeight, exifOrientation),
            crop.Width,
            crop.Height,
            shown,
            fit == WallpaperFit.Tile);
    }

    /// <summary>
    /// Repeats a BGRA tile over a BGRA canvas from its top-left corner (rows of 4-byte pixels, no padding).
    /// </summary>
    public static void TileInto(byte[] canvas, int canvasWidth, int canvasHeight, byte[] tile, int tileWidth, int tileHeight)
    {
        if (tileWidth <= 0 || tileHeight <= 0 || canvasWidth <= 0 || canvasHeight <= 0)
        {
            return;
        }

        int canvasStride = canvasWidth * 4;
        int tileStride = tileWidth * 4;
        for (int y = 0; y < canvasHeight; y++)
        {
            int sourceRow = (y % tileHeight) * tileStride;
            int targetRow = y * canvasStride;
            for (int x = 0; x < canvasWidth; x += tileWidth)
            {
                int pixels = Math.Min(tileWidth, canvasWidth - x);
                Buffer.BlockCopy(tile, sourceRow, canvas, targetRow + (x * 4), pixels * 4);
            }
        }
    }

    /// <summary>Size of the Mica backdrop: drawn stretched over a preview, this little is a soft blur.</summary>
    public const int BackdropWidth = 48;

    /// <summary>
    /// A tiny copy of the whole desktop — the picture where it sits (<paramref name="placement"/>, fractions of the
    /// desktop) over <paramref name="backgroundBgra"/>, which shows around it and through any transparent part of it —
    /// each pixel the average of the area it covers. Drawn stretched behind the preview's windows and taskbar it is the
    /// blurred wallpaper that Windows 11's Mica is made of, so the windows take on the wallpaper's colors the way real ones
    /// do. Premultiplied BGRA in and out, rows without padding.
    /// </summary>
    public static byte[] Backdrop(byte[] picture, int pictureWidth, int pictureHeight, WallpaperRect placement, uint backgroundBgra, int width, int height)
    {
        var result = new byte[width * height * 4];
        byte bb = (byte)backgroundBgra;
        byte bg = (byte)(backgroundBgra >> 8);
        byte br = (byte)(backgroundBgra >> 16);
        byte ba = (byte)(backgroundBgra >> 24);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var cell = new WallpaperRect(x / (double)width, y / (double)height, 1.0 / width, 1.0 / height);
                var covered = cell.Intersect(placement);
                double coverage = covered.IsEmpty || placement.IsEmpty ? 0 : covered.Width * covered.Height / (cell.Width * cell.Height);
                double b = 0, g = 0, r = 0, a = 0;
                if (coverage > 0)
                {
                    // The picture's pixels under the covered part of this cell.
                    int x0 = Math.Clamp((int)Math.Floor((covered.X - placement.X) / placement.Width * pictureWidth), 0, pictureWidth - 1);
                    int y0 = Math.Clamp((int)Math.Floor((covered.Y - placement.Y) / placement.Height * pictureHeight), 0, pictureHeight - 1);
                    int x1 = Math.Clamp((int)Math.Ceiling((covered.Right - placement.X) / placement.Width * pictureWidth), x0 + 1, pictureWidth);
                    int y1 = Math.Clamp((int)Math.Ceiling((covered.Bottom - placement.Y) / placement.Height * pictureHeight), y0 + 1, pictureHeight);
                    long sb = 0, sg = 0, sr = 0, sa = 0;
                    for (int py = y0; py < y1; py++)
                    {
                        int row = py * pictureWidth * 4;
                        for (int px = x0; px < x1; px++)
                        {
                            int i = row + (px * 4);
                            sb += picture[i];
                            sg += picture[i + 1];
                            sr += picture[i + 2];
                            sa += picture[i + 3];
                        }
                    }

                    // Over the desktop color, as the preview draws the picture: what is transparent shows the color.
                    double n = (double)(x1 - x0) * (y1 - y0);
                    double behind = 1 - (sa / (255.0 * n));
                    b = (sb / n) + (bb * behind);
                    g = (sg / n) + (bg * behind);
                    r = (sr / n) + (br * behind);
                    a = (sa / n) + (ba * behind);
                }

                int o = ((y * width) + x) * 4;
                result[o] = (byte)Math.Round((b * coverage) + (bb * (1 - coverage)));
                result[o + 1] = (byte)Math.Round((g * coverage) + (bg * (1 - coverage)));
                result[o + 2] = (byte)Math.Round((r * coverage) + (br * (1 - coverage)));
                result[o + 3] = (byte)Math.Round((a * coverage) + (ba * (1 - coverage)));
            }
        }

        return result;
    }

    /// <summary>True for the EXIF orientations that turn the picture on its side (5–8): its width and height swap.</summary>
    public static bool SwapsSides(int exifOrientation) => exifOrientation is >= 5 and <= 8;

    /// <summary>
    /// Applies an EXIF orientation (1–8) to BGRA pixels, so a photo shows upright as Windows shows it.
    /// The result is <paramref name="width"/> × <paramref name="height"/>, or the other way round for 5–8.
    /// </summary>
    public static (byte[] Pixels, int Width, int Height) Orient(byte[] pixels, int width, int height, int exifOrientation)
    {
        if (exifOrientation is < 2 or > 8)
        {
            return (pixels, width, height);
        }

        bool swap = SwapsSides(exifOrientation);
        int w = swap ? height : width;
        int h = swap ? width : height;
        var result = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                (int sx, int sy) = exifOrientation switch
                {
                    2 => (width - 1 - x, y),              // mirrored left-right
                    3 => (width - 1 - x, height - 1 - y), // upside down
                    4 => (x, height - 1 - y),             // mirrored top-bottom
                    5 => (y, x),                          // transposed
                    6 => (y, height - 1 - x),             // turned 90° clockwise
                    7 => (width - 1 - y, height - 1 - x), // transverse
                    _ => (width - 1 - y, x),              // 8: turned 90° counter-clockwise
                };
                Buffer.BlockCopy(pixels, ((sy * width) + sx) * 4, result, ((y * w) + x) * 4, 4);
            }
        }

        return (result, w, h);
    }

    /// <summary>
    /// Where a rectangle of the picture as shown — <paramref name="uprightWidth"/> × <paramref name="uprightHeight"/>,
    /// turned upright by its EXIF orientation — lies in the frame as stored: the inverse of <see cref="Orient"/>. Cropping
    /// the stored frame there and turning the crop upright gives the same pixels as turning the whole picture upright and
    /// cropping it, so a turned photo can be decoded only where it shows.
    /// </summary>
    public static RectI ToStoredFrame(RectI upright, int uprightWidth, int uprightHeight, int exifOrientation)
    {
        int x = upright.Left;
        int y = upright.Top;
        int w = upright.Width;
        int h = upright.Height;
        return exifOrientation switch
        {
            2 => RectI.FromSize(uprightWidth - x - w, y, w, h),
            3 => RectI.FromSize(uprightWidth - x - w, uprightHeight - y - h, w, h),
            4 => RectI.FromSize(x, uprightHeight - y - h, w, h),
            5 => RectI.FromSize(y, x, h, w),
            6 => RectI.FromSize(y, uprightWidth - x - w, h, w),
            7 => RectI.FromSize(uprightHeight - y - h, uprightWidth - x - w, h, w),
            8 => RectI.FromSize(uprightHeight - y - h, x, h, w),
            _ => upright,
        };
    }

    /// <summary>A rectangle of BGRA pixels, clamped to the source.</summary>
    public static (byte[] Pixels, int Width, int Height) Crop(byte[] pixels, int width, int height, int x, int y, int cropWidth, int cropHeight)
    {
        x = Math.Clamp(x, 0, Math.Max(0, width - 1));
        y = Math.Clamp(y, 0, Math.Max(0, height - 1));
        cropWidth = Math.Clamp(cropWidth, 1, width - x);
        cropHeight = Math.Clamp(cropHeight, 1, height - y);
        var result = new byte[cropWidth * cropHeight * 4];
        for (int row = 0; row < cropHeight; row++)
        {
            Buffer.BlockCopy(pixels, (((y + row) * width) + x) * 4, result, row * cropWidth * 4, cropWidth * 4);
        }

        return (result, cropWidth, cropHeight);
    }

    private static WallpaperRect Centered(double w, double h, double dw, double dh) => new((w - dw) / 2, (h - dh) / 2, dw, dh);
}
