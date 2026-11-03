using System.Runtime.InteropServices;
using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Core.Interop;
using EnhancedSnap.Core.Settings;
using EnhancedSnap.Engine.Native;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Snapping;

internal enum PlacementResult
{
    Ok,
    Hung,
    AccessDenied,
    Failed,
}

/// <summary>
/// Positions another app's window so that its <em>visible frame</em> matches a zone exactly.
/// <list type="bullet">
/// <item>Windows 10/11 windows have invisible resize borders; we measure them (window rect vs
/// DWMWA_EXTENDED_FRAME_BOUNDS) so Fluent gaps are pixel-exact.</item>
/// <item>Maximized / minimized / Windows-Snap "arranged" windows are restored first — those states are
/// exactly what makes Windows draw square corners.</item>
/// <item>A second pass corrects windows that change DPI (and border size) when moved across monitors.</item>
/// </list>
/// </summary>
internal static unsafe class WindowPlacer
{
    private const uint MoveFlags = SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER;

    public static PlacementResult Place(nint hwnd, RectI frameTarget, CornerStyle corners)
    {
        if (IsHungAppWindow(hwnd))
        {
            return PlacementResult.Hung;
        }

        bool resizable = WindowRules.IsResizable(hwnd);
        var placement = new WINDOWPLACEMENT { length = (uint)sizeof(WINDOWPLACEMENT) };
        GetWindowPlacement(hwnd, ref placement);
        bool needsRestore = placement.showCmd is SW_SHOWMAXIMIZED or SW_SHOWMINIMIZED
            || IsZoomed(hwnd) || IsIconic(hwnd) || IsWindowArranged(hwnd);

        if (needsRestore)
        {
            var estimate = Borders.Estimate(hwnd);
            placement.showCmd = SW_RESTORE;
            placement.flags &= ~WPF_RESTORETOMAXIMIZED;
            placement.rcNormalPosition = RECT.From(ToWorkspace(hwnd, estimate.Expand(frameTarget)));
            if (!SetWindowPlacement(hwnd, placement) && Marshal.GetLastPInvokeError() == ERROR_ACCESS_DENIED)
            {
                return PlacementResult.AccessDenied;
            }
        }

        for (int pass = 0; pass < 3; pass++)
        {
            RectI desired;
            uint flags = MoveFlags;
            if (resizable)
            {
                desired = Borders.Measure(hwnd).Expand(frameTarget);
            }
            else
            {
                desired = CenterKeepingSize(hwnd, frameTarget);
                flags |= SWP_NOSIZE;
            }

            if (!SetWindowPos(hwnd, 0, desired.Left, desired.Top, desired.Width, desired.Height, flags))
            {
                return Marshal.GetLastPInvokeError() == ERROR_ACCESS_DENIED ? PlacementResult.AccessDenied : PlacementResult.Failed;
            }

            if (!resizable || (TryGetFrameBounds(hwnd, out var frame) && frame.ToRectI().NearlyEquals(frameTarget, 1)))
            {
                break;
            }
        }

        ApplyCorners(hwnd, corners);
        return PlacementResult.Ok;
    }

    /// <summary>
    /// Non-blocking move used while a divider is dragged: the target app processes it on its own schedule,
    /// so a slow app never stalls the drag. The final position is applied with <see cref="Place"/>.
    /// </summary>
    public static void PlaceAsync(nint hwnd, RectI frameTarget)
    {
        if (IsZoomed(hwnd) || IsIconic(hwnd) || !WindowRules.IsResizable(hwnd))
        {
            return;
        }

        var desired = Borders.Measure(hwnd).Expand(frameTarget);
        SetWindowPos(hwnd, 0, desired.Left, desired.Top, desired.Width, desired.Height, MoveFlags | 0x4000 /* SWP_ASYNCWINDOWPOS */);
    }

    /// <summary>
    /// Gives a window back its floating size while keeping the grabbed point of the title bar under the cursor
    /// (same feel as dragging a Windows-snapped window off its half).
    /// </summary>
    public static void RestoreFloatingSize(nint hwnd, int frameWidth, int frameHeight, PointI cursor)
    {
        if (!TryGetFrameBounds(hwnd, out var frameRect))
        {
            return;
        }

        var frame = frameRect.ToRectI();
        double ratioX = frame.Width > 0 ? Math.Clamp((cursor.X - frame.Left) / (double)frame.Width, 0, 1) : 0.5;
        int left = cursor.X - (int)Math.Round(ratioX * frameWidth);
        int top = frame.Top;
        var target = new RectI(left, top, left + frameWidth, top + frameHeight);
        var desired = Borders.Measure(hwnd).Expand(target);
        SetWindowPos(hwnd, 0, desired.Left, desired.Top, desired.Width, desired.Height, MoveFlags);
    }

    /// <summary>Unsnap with the keyboard: floating size, centred on the zone it leaves.</summary>
    public static void MoveFloating(nint hwnd, RectI around, int frameWidth, int frameHeight)
    {
        int left = around.Left + ((around.Width - frameWidth) / 2);
        int top = around.Top + Math.Max(0, (around.Height - frameHeight) / 2);
        var desired = Borders.Measure(hwnd).Expand(new RectI(left, top, left + frameWidth, top + frameHeight));
        SetWindowPos(hwnd, 0, desired.Left, desired.Top, desired.Width, desired.Height, MoveFlags);
    }

    public static void ApplyCorners(nint hwnd, CornerStyle corners)
    {
        // Works across processes for normal (same-integrity) windows; failure is harmless.
        if (corners != CornerStyle.System)
        {
            SetDwmInt(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, corners == CornerStyle.Round ? DWMWCP_ROUND : DWMWCP_ROUNDSMALL);
        }
    }

    public static RectI? GetFrame(nint hwnd) => TryGetFrameBounds(hwnd, out var r) ? r.ToRectI() : null;

    /// <summary>
    /// The smallest visible frame the app accepts. The window manager marshals WM_GETMINMAXINFO across processes;
    /// the struct starts with the system defaults (what DefWindowProc would leave), the app raises them if it has a
    /// minimum of its own. A hung app keeps the system default.
    /// </summary>
    public static (int Width, int Height) GetMinimumFrameSize(nint hwnd)
    {
        uint dpi = GetDpiForWindow(hwnd);
        dpi = dpi == 0 ? 96 : dpi;
        var info = new MINMAXINFO
        {
            ptMinTrackSize = new POINT(GetSystemMetricsForDpi(SM_CXMINTRACK, dpi), GetSystemMetricsForDpi(SM_CYMINTRACK, dpi)),
            ptMaxTrackSize = new POINT(GetSystemMetricsForDpi(SM_CXMAXTRACK, dpi), GetSystemMetricsForDpi(SM_CYMAXTRACK, dpi)),
        };
        var minimum = info.ptMinTrackSize;
        if (!IsHungAppWindow(hwnd) && SendMessageTimeout(hwnd, WM_GETMINMAXINFO, 0, (nint)(&info), SMTO_ABORTIFHUNG, 50, out _) != 0)
        {
            minimum = info.ptMinTrackSize;
        }

        var b = Borders.Measure(hwnd);
        return (Math.Max(0, minimum.X - b.Left - b.Right), Math.Max(0, minimum.Y - b.Top - b.Bottom));
    }

    /// <summary>Size of the visible frame when the window is restored (works for maximized windows too).</summary>
    public static (int Width, int Height) GetRestoredFrameSize(nint hwnd)
    {
        // Maximized, minimized and Windows-Snap "arranged" windows keep their floating rect in rcNormalPosition.
        if (IsZoomed(hwnd) || IsIconic(hwnd) || IsWindowArranged(hwnd))
        {
            var placement = new WINDOWPLACEMENT { length = (uint)sizeof(WINDOWPLACEMENT) };
            if (GetWindowPlacement(hwnd, ref placement))
            {
                var estimate = Borders.Estimate(hwnd);
                var normal = placement.rcNormalPosition.ToRectI();
                return (normal.Width - estimate.Left - estimate.Right, normal.Height - estimate.Top - estimate.Bottom);
            }
        }

        var frame = GetFrame(hwnd);
        return frame is { } f ? (f.Width, f.Height) : (800, 600);
    }

    /// <summary>WINDOWPLACEMENT uses workspace coordinates (relative to the work area) for normal app windows.</summary>
    public static RectI ToWorkspace(nint hwnd, RectI screenRect)
    {
        if ((GetExStyle(hwnd) & WS_EX_TOOLWINDOW) != 0)
        {
            return screenRect;
        }

        nint monitor = Win32.MonitorFromRect(RECT.From(screenRect), Win32.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFOEXW { cbSize = sizeof(MONITORINFOEXW) };
        if (!Win32.GetMonitorInfo(monitor, ref info))
        {
            return screenRect;
        }

        return screenRect.Offset(-(info.rcWork.Left - info.rcMonitor.Left), -(info.rcWork.Top - info.rcMonitor.Top));
    }

    private static RectI CenterKeepingSize(nint hwnd, RectI zone)
    {
        GetWindowRect(hwnd, out var window);
        var w = window.ToRectI();
        var b = Borders.Measure(hwnd);
        int frameWidth = w.Width - b.Left - b.Right;
        int frameHeight = w.Height - b.Top - b.Bottom;
        int left = zone.Left + ((zone.Width - frameWidth) / 2) - b.Left;
        int top = zone.Top + Math.Max(0, (zone.Height - frameHeight) / 2) - b.Top;
        return new RectI(left, top, left + w.Width, top + w.Height);
    }

    /// <summary>Invisible resize borders around the visible frame.</summary>
    private readonly record struct Borders(int Left, int Top, int Right, int Bottom)
    {
        public RectI Expand(RectI frame) => frame.Inflate(Left, Top, Right, Bottom);

        public static Borders Measure(nint hwnd)
        {
            if (!GetWindowRect(hwnd, out var window) || !TryGetFrameBounds(hwnd, out var frame))
            {
                return default;
            }

            return new Borders(
                Math.Clamp(frame.Left - window.Left, 0, 32),
                Math.Clamp(frame.Top - window.Top, 0, 32),
                Math.Clamp(window.Right - frame.Right, 0, 32),
                Math.Clamp(window.Bottom - frame.Bottom, 0, 32));
        }

        /// <summary>Typical Windows 11 borders for windows we cannot measure yet (maximized).</summary>
        public static Borders Estimate(nint hwnd)
        {
            if (!WindowRules.IsResizable(hwnd))
            {
                return default;
            }

            uint dpi = GetDpiForWindow(hwnd);
            int b = (int)Math.Round(7 * (dpi == 0 ? 1.0 : dpi / 96.0));
            return new Borders(b, 0, b, b);
        }
    }
}
