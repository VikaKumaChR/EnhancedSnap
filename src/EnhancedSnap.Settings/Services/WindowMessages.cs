using System.Runtime.InteropServices;
using EnhancedSnap.Core.Diagnostics;

namespace EnhancedSnap.Settings.Services;

/// <summary>
/// A message Windows broadcasts to top-level windows that the settings window answers while it is open, through a window
/// subclass (comctl32): a new desktop wallpaper is announced as WM_SETTINGCHANGE with SPI_SETDESKWALLPAPER, and the
/// desktop previews read it at once, not only on the next activation. (Whether a slideshow or Windows Spotlight announces
/// every picture this way hasn't been checked; the previews read the wallpaper again on activation either way.)
/// The settings app's message-only host window gets no broadcasts, so this is on the main window. Windows sends the
/// broadcast (SendMessageTimeout to HWND_BROADCAST), which calls the window procedure chain directly, this subclass
/// included. WM_SETTINGCHANGE can't be posted at all: user32 treats it as a message carrying a pointer and refuses
/// PostMessage with ERROR_MESSAGE_SYNC_ONLY even when lParam is 0, so tests send it too.
/// </summary>
internal static unsafe partial class WindowMessages
{
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint WM_NCDESTROY = 0x0082;
    private const nuint SPI_SETDESKWALLPAPER = 0x0014;
    private const nuint SubclassId = 1;
    private static Action? _wallpaperChanged;

    public static void WatchWallpaper(nint hwnd, Action changed)
    {
        _wallpaperChanged = changed;
        if (hwnd == 0 || !SetWindowSubclass(hwnd, &Proc, SubclassId, 0))
        {
            Log.Warn("Wallpaper changes are picked up on activation only (no window subclass)");
        }
    }

    [UnmanagedCallersOnly]
    private static nint Proc(nint hwnd, uint msg, nuint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            if (msg == WM_SETTINGCHANGE && wParam == SPI_SETDESKWALLPAPER)
            {
                _wallpaperChanged?.Invoke();
            }
            else if (msg == WM_NCDESTROY)
            {
                RemoveWindowSubclass(hwnd, &Proc, id);
                _wallpaperChanged = null;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Window message 0x{msg:X} failed", ex);
        }

        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(nint hwnd, delegate* unmanaged<nint, uint, nuint, nint, nuint, nuint, nint> proc, nuint id, nuint data);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveWindowSubclass(nint hwnd, delegate* unmanaged<nint, uint, nuint, nint, nuint, nuint, nint> proc, nuint id);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(nint hwnd, uint msg, nuint wParam, nint lParam);
}
