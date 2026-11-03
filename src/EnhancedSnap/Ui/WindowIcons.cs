using System.Runtime.InteropServices;
using EnhancedSnap.Core.Interop;
using EnhancedSnap.Engine.Native;
using EnhancedSnap.Engine.Snapping;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Ui;

/// <summary>Window titles, icons and the list of windows that Snap Assist can offer.</summary>
internal static unsafe class WindowIcons
{
    /// <summary>Icon for a window: WM_GETICON → class icon → the executable's shell icon. Caller must not destroy it
    /// unless <paramref name="owned"/> is true.</summary>
    public static nint GetIcon(nint hwnd, out bool owned)
    {
        owned = false;
        foreach (int kind in (ReadOnlySpan<int>)[ICON_BIG, ICON_SMALL2, ICON_SMALL])
        {
            if (SendMessageTimeout(hwnd, WM_GETICON, (nuint)kind, 96, SMTO_ABORTIFHUNG, 40, out nint icon) != 0 && icon != 0)
            {
                return icon;
            }
        }

        nint classIcon = GetClassLongPtr(hwnd, GCLP_HICON);
        if (classIcon == 0)
        {
            classIcon = GetClassLongPtr(hwnd, GCLP_HICONSM);
        }

        if (classIcon != 0)
        {
            return classIcon;
        }

        string? exe = ProcessPath(hwnd);
        if (exe is not null)
        {
            var info = default(SHFILEINFOW);
            if (SHGetFileInfo(exe, 0, &info, (uint)sizeof(SHFILEINFOW), SHGFI_ICON | SHGFI_LARGEICON) != 0 && info.hIcon != 0)
            {
                owned = true;
                return info.hIcon;
            }
        }

        return 0;
    }

    public static string GetTitle(nint hwnd)
    {
        int length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return WindowRules.GetProcessFileName(hwnd) is { } exe ? Path.GetFileNameWithoutExtension(exe) : string.Empty;
        }

        char* buffer = stackalloc char[Math.Min(length + 1, 512)];
        int read = GetWindowText(hwnd, buffer, Math.Min(length + 1, 512));
        return new string(buffer, 0, read);
    }

    /// <summary>Short app-style label for group names ("Edge", "Settings"): the part after the last " - ".</summary>
    public static string ShortName(nint hwnd)
    {
        string title = GetTitle(hwnd);
        int dash = title.LastIndexOf(" - ", StringComparison.Ordinal);
        string name = dash >= 0 && dash + 3 < title.Length ? title[(dash + 3)..] : title;
        name = name.Replace("Microsoft​ ", string.Empty, StringComparison.Ordinal);
        return name.Length > 28 ? name[..27] + "…" : name;
    }

    /// <summary>Top-level, visible, snappable windows in z-order (front to back).</summary>
    public static List<nint> SnappableWindows(IReadOnlyList<string> excludedApps, Func<nint, bool>? filter = null)
    {
        var windows = new List<nint>();
        var handle = GCHandle.Alloc(windows);
        try
        {
            EnumWindows(&Collect, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return windows
            .Where(h => GetWindow(h, GW_OWNER) == 0 || (GetExStyle(h) & WS_EX_APPWINDOW) != 0)
            .Where(h => WindowRules.IsSnappable(h, excludedApps)) // minimized windows keep WS_VISIBLE, so they are included
            .Where(h => filter?.Invoke(h) ?? true)
            .ToList();
    }

    /// <summary>Premultiplied BGRA pixels of an icon drawn at <paramref name="size"/>, or null.</summary>
    public static byte[]? ToPremultipliedBgra(nint icon, int size)
    {
        nint dc = CreateCompatibleDC(0);
        if (dc == 0)
        {
            return null;
        }

        try
        {
            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = size,
                biHeight = -size, // top-down
                biPlanes = 1,
                biBitCount = 32,
            };
            nint bitmap = CreateDIBSection(dc, &header, 0, out nint bits, 0, 0);
            if (bitmap == 0)
            {
                return null;
            }

            try
            {
                nint previous = SelectObject(dc, bitmap);
                // On a zeroed 32-bpp DIB, DrawIconEx alpha-blends: colour ends up premultiplied, alpha = icon alpha.
                DrawIconEx(dc, 0, 0, icon, size, size, 0, 0, DI_NORMAL);
                SelectObject(dc, previous);

                var pixels = new byte[size * size * 4];
                Marshal.Copy(bits, pixels, 0, pixels.Length);
                bool anyAlpha = false;
                for (int i = 3; i < pixels.Length; i += 4)
                {
                    if (pixels[i] != 0)
                    {
                        anyAlpha = true;
                        break;
                    }
                }

                if (!anyAlpha)
                {
                    // Legacy icon without an alpha channel: treat drawn pixels as opaque.
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        if (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0)
                        {
                            pixels[i + 3] = 255;
                        }
                    }
                }

                return pixels;
            }
            finally
            {
                DeleteObject(bitmap);
            }
        }
        finally
        {
            DeleteDC(dc);
        }
    }

    private static string? ProcessPath(nint hwnd)
    {
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        nint process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
        {
            return null;
        }

        try
        {
            char* buffer = stackalloc char[1024];
            uint size = 1024;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint hwnd, nint data)
    {
        if (GCHandle.FromIntPtr(data).Target is List<nint> list)
        {
            list.Add(hwnd);
        }

        return 1;
    }
}
