using System.Runtime.InteropServices;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Displays;
using EnhancedSnap.Core.Geometry;
using Microsoft.Win32;

namespace EnhancedSnap.Settings.Services;

/// <summary>What Windows shows on the primary display's desktop.</summary>
/// <param name="Path">The picture, or null for a solid color.</param>
/// <param name="BackgroundRgb">The desktop color (0xRRGGBB): behind Fit and Center, and the whole desktop without a picture.</param>
/// <param name="Monitor">The primary monitor in desktop coordinates.</param>
/// <param name="Desktop">The bounding box of all monitors (for Span).</param>
internal readonly record struct WallpaperInfo(string? Path, WallpaperFit Fit, uint BackgroundRgb, RectI Monitor, RectI Desktop, bool Slideshow);

/// <summary>
/// Reads the desktop wallpaper of the primary display through the shell's IDesktopWallpaper (read only; nothing is
/// ever set). Called on a worker thread. Calls go straight through the COM vtable, which NativeAOT supports without
/// runtime marshalling.
/// </summary>
internal static unsafe partial class DesktopWallpaper
{
    private const uint CLSCTX_ALL = 0x17;
    private const uint COINIT_MULTITHREADED = 0x0;
    private const int DSS_SLIDESHOW = 0x2;

    private static readonly Guid ClsidDesktopWallpaper = new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");
    private static readonly Guid IidDesktopWallpaper = new("B92B56A9-8B55-4E14-9A89-0199BBB6F93B");

    /// <summary>
    /// Windows' own copy of the picture it shows (a slideshow's current one, Spotlight's): readable when the picture's
    /// file has moved on or can't be read.
    /// </summary>
    public static string TranscodedPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Themes", "TranscodedWallpaper");

    /// <summary>The primary display's wallpaper, or null when the shell can't say (no Explorer, a remote session…).</summary>
    public static WallpaperInfo? QueryPrimary()
    {
        int init = CoInitializeEx(0, COINIT_MULTITHREADED);
        try
        {
            Guid clsid = ClsidDesktopWallpaper;
            Guid iid = IidDesktopWallpaper;
            void* wallpaper = null;
            int hr = CoCreateInstance(&clsid, 0, CLSCTX_ALL, &iid, &wallpaper);
            if (hr < 0 || wallpaper is null)
            {
                Log.Warn($"Desktop wallpaper: IDesktopWallpaper unavailable (0x{hr:X8})");
                return null;
            }

            try
            {
                return Read(wallpaper);
            }
            finally
            {
                ((delegate* unmanaged[Stdcall]<void*, uint>)(*(void***)wallpaper)[2])(wallpaper); // Release
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Desktop wallpaper query failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (init >= 0)
            {
                CoUninitialize(); // S_OK and S_FALSE both need it
            }
        }
    }

    private static WallpaperInfo? Read(void* w)
    {
        void** vtbl = *(void***)w;
        var getWallpaper = (delegate* unmanaged[Stdcall]<void*, char*, char**, int>)vtbl[4];
        var getMonitorAt = (delegate* unmanaged[Stdcall]<void*, uint, char**, int>)vtbl[5];
        var getMonitorCount = (delegate* unmanaged[Stdcall]<void*, uint*, int>)vtbl[6];
        var getMonitorRect = (delegate* unmanaged[Stdcall]<void*, char*, RECT*, int>)vtbl[7];
        var getBackground = (delegate* unmanaged[Stdcall]<void*, uint*, int>)vtbl[9];
        var getPosition = (delegate* unmanaged[Stdcall]<void*, int*, int>)vtbl[11];
        var getStatus = (delegate* unmanaged[Stdcall]<void*, int*, int>)vtbl[17];

        uint count = 0;
        int hr = getMonitorCount(w, &count);
        if (hr < 0 || count == 0)
        {
            Log.Warn($"Desktop wallpaper: no monitors (0x{hr:X8}, {count})");
            return null;
        }

        string? primaryId = null;
        RectI primary = default;
        RectI desktop = default;
        for (uint i = 0; i < count; i++)
        {
            char* id = null;
            if (getMonitorAt(w, i, &id) < 0 || id is null)
            {
                continue;
            }

            try
            {
                RECT r;
                if (getMonitorRect(w, id, &r) < 0)
                {
                    continue; // not active
                }

                var rect = new RectI(r.Left, r.Top, r.Right, r.Bottom);
                if (rect.IsEmpty)
                {
                    continue;
                }

                desktop = desktop.IsEmpty ? rect : desktop.Union(rect);
                if (primaryId is null || (rect.Left == 0 && rect.Top == 0)) // the primary display sits at the origin
                {
                    primaryId = new string(id);
                    primary = rect;
                }
            }
            finally
            {
                CoTaskMemFree(id);
            }
        }

        if (primaryId is null)
        {
            return null;
        }

        string? path = null;
        fixed (char* monitor = primaryId)
        {
            char* p = null;
            if (getWallpaper(w, monitor, &p) >= 0 && p is not null)
            {
                path = new string(p);
                CoTaskMemFree(p);
            }
        }

        uint color = 0;
        int position = (int)WallpaperFit.Fill;
        int status = 0;
        _ = getBackground(w, &color);
        _ = getPosition(w, &position);
        _ = getStatus(w, &status);

        // A slideshow or Windows Spotlight can name a file that has moved on; Windows keeps what it shows in
        // TranscodedWallpaper. A solid-color background has no picture at all (TranscodedWallpaper may still hold an
        // old one, so it is only used when Windows says a picture is shown).
        int type = BackgroundType();
        bool picture = type is 0 or 2 or 3 || (type < 0 && !string.IsNullOrEmpty(path));
        if (!picture)
        {
            path = null;
        }
        else if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            string transcoded = TranscodedPath;
            path = File.Exists(transcoded) ? transcoded : null;
        }

        uint rgb = ((color & 0xFF) << 16) | (color & 0xFF00) | ((color >> 16) & 0xFF); // COLORREF is 0x00BBGGRR
        var fit = Enum.IsDefined((WallpaperFit)position) ? (WallpaperFit)position : WallpaperFit.Fill;
        return new WallpaperInfo(path, fit, rgb, primary, desktop, (status & DSS_SLIDESHOW) != 0);
    }

    /// <summary>Windows' own record of the background kind: 0 picture, 1 solid color, 2 slideshow, 3 Spotlight; -1 unknown.</summary>
    private static int BackgroundType()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers");
            return key?.GetValue("BackgroundType") is int type ? type : -1;
        }
        catch
        {
            return -1;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, void** instance);

    [LibraryImport("ole32.dll")]
    private static partial void CoTaskMemFree(void* memory);
}
