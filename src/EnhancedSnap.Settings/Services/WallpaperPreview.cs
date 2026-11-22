using System.Runtime.InteropServices.WindowsRuntime;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Displays;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;

namespace EnhancedSnap.Settings.Services;

/// <summary>
/// The primary display's desktop wallpaper for the desktop previews (Suggestion01_01, Suggestion01_02), like the
/// display picture in Windows 11 Settings → Personalization.
/// <para>
/// Read through <see cref="DesktopWallpaper"/> and decoded on a worker thread straight to preview size, as
/// <see cref="WallpaperLayout.Plan"/> works it out: the preview is at most <see cref="WallpaperLayout.MaxPreviewSide"/> px
/// on the longer side (about 4 MB), and only the part of the picture the monitor shows is decoded, never the
/// full-resolution file. A photo turned by its EXIF tag costs no more: that part is found in the stored frame, and only
/// it is turned upright. Laid out the way Windows fits it; a file that can't be read right now gives way to Windows' own
/// copy of the picture. Refreshed when Windows says the wallpaper changed (WM_SETTINGCHANGE with SPI_SETDESKWALLPAPER)
/// and when the window is activated; never polled. Without a readable
/// picture the previews keep their accent gradient.
/// </para>
/// </summary>
internal static class WallpaperPreview
{
    private const string OrientationProperty = "System.Photo.Orientation";
    private static string? _key;
    private static bool _busy;
    private static bool _again;

    /// <summary>The preview, or null before the first read or when it failed (the previews then draw a gradient).</summary>
    public static Snapshot? Current { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="Current"/> changes.</summary>
    public static event Action? Changed;

    /// <summary>True while a desktop preview is on screen.</summary>
    public static bool HasViewers => Changed is not null;

    /// <summary>Re-read the wallpaper (UI thread). Cheap when nothing changed: the file is decoded again only if it did.</summary>
    public static void Refresh(DispatcherQueue ui)
    {
        if (_busy)
        {
            _again = true;
            return;
        }

        _busy = true;
        ui.TryEnqueue(DispatcherQueuePriority.Low, async () =>
        {
            try
            {
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                // Not the message: one about a file names it, and the log never holds the wallpaper's name.
                Log.Warn($"Wallpaper preview failed: {ex.GetType().Name} 0x{ex.HResult:X8}");
            }
            finally
            {
                _busy = false;
                if (_again)
                {
                    _again = false;
                    Refresh(ui);
                }
            }
        });
    }

    private static async Task RefreshAsync()
    {
        if (await Task.Run(Query) is not { } query || query.Info.Monitor.IsEmpty)
        {
            Log.Info("Wallpaper preview: the shell did not say what the desktop shows");
            return; // keep whatever is shown
        }

        var (wallpaper, key) = query;
        if (key == _key)
        {
            return;
        }

        var background = Color.FromArgb(255, (byte)(wallpaper.BackgroundRgb >> 16), (byte)(wallpaper.BackgroundRgb >> 8), (byte)wallpaper.BackgroundRgb);
        Decoded? picture = wallpaper.Path is { } path ? await Task.Run(() => ReadAsync(path, wallpaper)) : null;
        Log.Info($"Wallpaper preview: {Kind(wallpaper.Path)}, {wallpaper.Fit}, monitor {wallpaper.Monitor}{(wallpaper.Slideshow ? ", slideshow" : "")}"
            + $" -> {(picture is null ? "no picture" : $"{picture.Bitmap.PixelWidth}x{picture.Bitmap.PixelHeight} at {picture.Placement}")}");
        if (picture is null && wallpaper.Path is not null)
        {
            return; // unreadable right now, Windows' copy too: keep the last preview, try again on the next activation
        }

        ImageSource? image = null;
        ImageSource? backdrop = null;
        SoftwareBitmap[] bitmaps = [];
        if (picture is not null)
        {
            // XAML reads a SoftwareBitmapSource's bitmap again when it draws or tears the image down: a disposed one
            // fails fast with RO_E_CLOSED. So the bitmaps live as long as the snapshot, and go with it to the GC.
            var tiny = Bitmap(picture.Backdrop, picture.BackdropWidth, picture.BackdropHeight);
            image = await SourceAsync(picture.Bitmap);
            backdrop = await SourceAsync(tiny);
            bitmaps = [picture.Bitmap, tiny];
        }

        _key = key;
        Current = new Snapshot(image, picture?.Placement ?? default, background, backdrop, bitmaps);
        Changed?.Invoke();
    }

    private static async Task<ImageSource> SourceAsync(SoftwareBitmap bitmap)
    {
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(bitmap);
        return source;
    }

    /// <summary>
    /// The primary display's wallpaper and what decides its preview (worker thread: the shell call and the file's time
    /// stamp stay off the UI thread, since a file on a share can stall).
    /// </summary>
    private static (WallpaperInfo Info, string Key)? Query() => DesktopWallpaper.QueryPrimary() is { } info ? (info, Key(info)) : null;

    /// <summary>What decides the picture: the file and its version, how it is fitted, the color and the monitors.</summary>
    private static string Key(WallpaperInfo w)
    {
        string stamp = "";
        if (w.Path is { } path)
        {
            try
            {
                var file = new FileInfo(path);
                stamp = $"{file.LastWriteTimeUtc.Ticks}:{file.Length}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return $"{w.Path}|{stamp}|{w.Fit}|{w.BackgroundRgb:X6}|{w.Monitor}|{w.Desktop}";
    }

    /// <summary>The wallpaper as the log names it: its kind and extension, never the file's name (PRIVACY.md).</summary>
    private static string Kind(string? path) =>
        path is null ? "solid color" : Path.GetExtension(path) is { Length: > 0 } extension ? $"{extension.ToLowerInvariant()} picture" : "picture";

    /// <summary>
    /// The picture, or Windows' own copy of it (TranscodedWallpaper) when the file can't be decoded right now — locked, a
    /// cloud file that is offline, a format without a codec on this PC. Null when neither can be read.
    /// </summary>
    private static async Task<Decoded?> ReadAsync(string path, WallpaperInfo w)
    {
        var picture = await DecodeAsync(path, w);
        string copy = DesktopWallpaper.TranscodedPath;
        if (picture is null && !string.Equals(path, copy, StringComparison.OrdinalIgnoreCase) && File.Exists(copy))
        {
            Log.Info("Wallpaper preview: decoding Windows' copy of the picture instead");
            picture = await DecodeAsync(copy, w);
        }

        return picture;
    }

    /// <summary>
    /// Decodes what <see cref="WallpaperLayout.Plan"/> says the preview needs (worker thread): the part of the picture the
    /// monitor shows, at preview size, turned upright — for Tile one tile, repeated over the preview. The placement comes
    /// back in fractions of the monitor, so a preview of any size can draw it.
    /// </summary>
    private static async Task<Decoded?> DecodeAsync(string path, WallpaperInfo w)
    {
        try
        {
            using IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            int orientation = await OrientationAsync(decoder);
            var (canvasW, canvasH) = WallpaperLayout.PreviewSize(w.Monitor.Width, w.Monitor.Height);
            if (WallpaperLayout.Plan(w.Fit, w.Monitor, w.Desktop, (int)decoder.PixelWidth, (int)decoder.PixelHeight, orientation, canvasW, canvasH) is not { } plan)
            {
                Log.Warn("Wallpaper preview: nothing of the picture is on the monitor");
                return null;
            }

            // Scaled, then cut to the planned part of the stored frame: the decoder never hands over more than that.
            var bounds = plan.Bounds;
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)plan.ScaledWidth,
                ScaledHeight = (uint)plan.ScaledHeight,
                Bounds = new BitmapBounds { X = (uint)bounds.Left, Y = (uint)bounds.Top, Width = (uint)bounds.Width, Height = (uint)bounds.Height },
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            var data = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb);
            byte[] stored = data.DetachPixelData();
            if (stored.Length != bounds.Width * bounds.Height * 4)
            {
                Log.Warn($"Wallpaper preview: the decoder gave {stored.Length} bytes for {bounds}");
                return null;
            }

            var (pixels, width, height) = WallpaperLayout.Orient(stored, bounds.Width, bounds.Height, orientation);
            if (plan.Tiled && (width < canvasW || height < canvasH))
            {
                byte[] tiled = new byte[canvasW * canvasH * 4];
                WallpaperLayout.TileInto(tiled, canvasW, canvasH, pixels, width, height);
                (pixels, width, height) = (tiled, canvasW, canvasH);
            }

            return WithBackdrop(Bitmap(pixels, width, height), pixels, plan.Placement, w, canvasW, canvasH);
        }
        catch (Exception ex)
        {
            // Not the message: it can name the file, and the log never holds the wallpaper's name.
            Log.Warn($"Wallpaper preview could not decode the {Kind(path)}: {ex.GetType().Name} 0x{ex.HResult:X8}");
            return null;
        }
    }

    /// <summary>The EXIF orientation (1–8); 1 for formats without it.</summary>
    private static async Task<int> OrientationAsync(BitmapDecoder decoder)
    {
        try
        {
            // A real array, not a collection expression: CsWinRT's AOT generator marshals an IEnumerable<string> to WinRT
            // only for a type it can see at the call site, and the compiler's own collection types are not among them.
            var properties = await decoder.BitmapProperties.GetPropertiesAsync(new[] { OrientationProperty });
            return properties.TryGetValue(OrientationProperty, out var value) && value.Value is ushort orientation ? orientation : 1;
        }
        catch (Exception ex)
        {
            // Formats without the metadata (BMP …) end up here too; the HRESULT tells them from a marshalling failure.
            Log.Warn($"Wallpaper preview: no EXIF orientation (0x{ex.HResult:X8}), drawn as stored");
            return 1;
        }
    }

    private static SoftwareBitmap Bitmap(byte[] pixels, int width, int height) =>
        SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);

    /// <summary>Adds the tiny whole-desktop copy the preview's Mica windows and taskbar are drawn from.</summary>
    private static Decoded WithBackdrop(SoftwareBitmap bitmap, byte[] pixels, WallpaperRect placement, WallpaperInfo w, int canvasW, int canvasH)
    {
        int width = WallpaperLayout.BackdropWidth;
        int height = Math.Max(1, (int)Math.Round(width * canvasH / (double)canvasW));
        byte[] backdrop = WallpaperLayout.Backdrop(pixels, bitmap.PixelWidth, bitmap.PixelHeight, placement, 0xFF000000 | w.BackgroundRgb, width, height);
        return new Decoded(bitmap, placement, backdrop, width, height);
    }

    /// <summary>
    /// The picture (null for a solid color), where it sits in fractions of the monitor, the desktop color, and the
    /// blurred whole desktop for Mica (null for a solid color: the color itself is the backdrop then).
    /// </summary>
    internal sealed record Snapshot(ImageSource? Image, WallpaperRect Placement, Color Background, ImageSource? Backdrop, SoftwareBitmap[] Bitmaps);

    private sealed record Decoded(SoftwareBitmap Bitmap, WallpaperRect Placement, byte[] Backdrop, int BackdropWidth, int BackdropHeight);
}
