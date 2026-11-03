using System.Runtime.InteropServices;
using EnhancedSnap.Core.Diagnostics;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Groups;

/// <summary>
/// The desktop wallpaper behind a group's preview, like Windows' snap-group thumbnails in Alt + Tab, Task View and the
/// taskbar. Decoded once with GDI+ (any format, also the extension-less TranscodedWallpaper) and kept as a small
/// bitmap until the wallpaper file changes; nothing is loaded until the first preview is drawn.
/// </summary>
internal static unsafe partial class Wallpaper
{
    private const uint SPI_GETDESKWALLPAPER = 0x0073;
    private const int CacheWidth = 960;

    private static readonly Lock Gate = new(); // previews render on pool threads; a bitmap fits in one DC at a time
    private static nint _gdiplus;
    private static nint _bitmap;
    private static int _width;
    private static int _height;
    private static string? _key;

    /// <summary>Draws the wallpaper over the whole <paramref name="width"/> × <paramref name="height"/> area ("Fill": cropped, never stretched).</summary>
    public static bool Draw(nint dc, int width, int height)
    {
        lock (Gate)
        {
            if (!Ensure())
            {
                return false;
            }

            double k = Math.Max(width / (double)_width, height / (double)_height);
            int sw = Math.Max(1, (int)(width / k));
            int sh = Math.Max(1, (int)(height / k));
            nint source = CreateCompatibleDC(0);
            nint old = SelectObject(source, _bitmap);
            SetStretchBltMode(dc, HALFTONE);
            SetBrushOrgEx(dc, 0, 0, 0);
            bool ok = StretchBlt(dc, 0, 0, width, height, source, (_width - sw) / 2, (_height - sh) / 2, sw, sh, SRCCOPY);
            SelectObject(source, old);
            DeleteDC(source);
            GdiFlush(); // done with the shared bitmap before the next thread selects it
            return ok;
        }
    }

    private static bool Ensure()
    {
        string? path = CurrentPath();
        if (path is null)
        {
            return false;
        }

        string key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
        if (key == _key && _bitmap != 0)
        {
            return true;
        }

        if (_bitmap != 0)
        {
            DeleteObject(_bitmap);
            _bitmap = 0;
        }

        _key = null;
        try
        {
            if (_gdiplus == 0)
            {
                var input = new GdiplusStartupInput { GdiplusVersion = 1 };
                if (GdiplusStartup(out _gdiplus, ref input, 0) != 0)
                {
                    return false;
                }
            }

            nint image;
            fixed (char* file = path)
            {
                if (GdipCreateBitmapFromFile(file, out image) != 0 || image == 0)
                {
                    return false;
                }
            }

            try
            {
                GdipGetImageWidth(image, out uint fullWidth);
                GdipGetImageHeight(image, out uint fullHeight);
                if (fullWidth == 0 || fullHeight == 0 || GdipCreateHBITMAPFromBitmap(image, out nint full, 0xFF000000) != 0)
                {
                    return false;
                }

                // Keep a small copy: previews are at most a few hundred pixels wide.
                _width = (int)Math.Min(fullWidth, CacheWidth);
                _height = Math.Max(1, (int)(fullHeight * (_width / (double)fullWidth)));
                var header = new Native.BITMAPINFOHEADER { biSize = (uint)sizeof(Native.BITMAPINFOHEADER), biWidth = _width, biHeight = -_height, biPlanes = 1, biBitCount = 32 };
                _bitmap = CreateDIBSection(0, &header, 0, out _, 0, 0);
                nint target = CreateCompatibleDC(0);
                nint source = CreateCompatibleDC(0);
                nint oldTarget = SelectObject(target, _bitmap);
                nint oldSource = SelectObject(source, full);
                SetStretchBltMode(target, HALFTONE);
                SetBrushOrgEx(target, 0, 0, 0);
                StretchBlt(target, 0, 0, _width, _height, source, 0, 0, (int)fullWidth, (int)fullHeight, SRCCOPY);
                SelectObject(source, oldSource);
                SelectObject(target, oldTarget);
                DeleteDC(source);
                DeleteDC(target);
                DeleteObject(full);
                _key = key;
                return _bitmap != 0;
            }
            finally
            {
                GdipDisposeImage(image);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Wallpaper for previews unavailable: {ex.Message}");
            return false;
        }
    }

    private static string? CurrentPath()
    {
        char* buffer = stackalloc char[520];
        if (SystemParametersInfo(SPI_GETDESKWALLPAPER, 520, buffer, 0))
        {
            string path = new(buffer);
            if (path.Length > 0 && File.Exists(path))
            {
                return path;
            }
        }

        // Slideshow / Spotlight: the shell keeps the current picture here (no extension; GDI+ reads the content).
        string transcoded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
        return File.Exists(transcoded) ? transcoded : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GdiplusStartupInput
    {
        public uint GdiplusVersion;
        public nint DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    [LibraryImport("gdiplus.dll")]
    private static partial int GdiplusStartup(out nint token, ref GdiplusStartupInput input, nint output);

    [LibraryImport("gdiplus.dll")]
    private static partial int GdipCreateBitmapFromFile(char* filename, out nint bitmap);

    [LibraryImport("gdiplus.dll")]
    private static partial int GdipGetImageWidth(nint image, out uint width);

    [LibraryImport("gdiplus.dll")]
    private static partial int GdipGetImageHeight(nint image, out uint height);

    [LibraryImport("gdiplus.dll")]
    private static partial int GdipCreateHBITMAPFromBitmap(nint bitmap, out nint hbmReturn, uint background);

    [LibraryImport("gdiplus.dll")]
    private static partial int GdipDisposeImage(nint image);
}
