using System.Collections.Concurrent;
using System.Collections.Frozen;
using EnhancedSnap.Core.Interop;
using EnhancedSnap.Core.Settings;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Snapping;

/// <summary>
/// Which windows Enhanced Snap may move. Thread-safe: also used by the keyboard-hook thread, so the
/// checks are cheap (style bits, cached process names) and never block on the target window.
/// </summary>
internal static unsafe class WindowRules
{
    private static readonly FrozenSet<string> IgnoredClasses = new[]
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow", "TaskListThumbnailWnd", "MultitaskingViewFrame", "ForegroundStaging",
        "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland", "Shell_InputSwitchTopLevelWindow",
        "#32768", "tooltips_class32", "SysShadow", "EnhancedSnap.Overlay",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<uint, (string? Name, long ExpiresAt)> ProcessNames = new();

    public static readonly uint OwnProcessId = (uint)Environment.ProcessId;

    /// <summary>
    /// Development/test guard: when ENHANCEDSNAP_TEST_WINDOW_TITLE is set, only the window with exactly that
    /// title is ever touched, so automated tests on a live desktop can't move the user's real windows.
    /// Never set in normal installs.
    /// </summary>
    public static readonly string? TestWindowTitle = Environment.GetEnvironmentVariable("ENHANCEDSNAP_TEST_WINDOW_TITLE");

    public static bool IsSnappable(nint hwnd, IReadOnlyList<string> excludedApps)
    {
        if (hwnd == 0 || !IsWindow(hwnd) || !IsWindowVisible(hwnd))
        {
            return false;
        }

        if (TestWindowTitle is not null && GetWindowTitle(hwnd) != TestWindowTitle)
        {
            return false;
        }

        if (GetAncestor(hwnd, GA_ROOT) != hwnd || hwnd == GetShellWindow())
        {
            return false;
        }

        uint style = GetStyle(hwnd);
        uint exStyle = GetExStyle(hwnd);
        if ((style & (WS_CHILD | WS_DISABLED)) != 0)
        {
            return false;
        }

        bool hasCaption = (style & WS_CAPTION) == WS_CAPTION;
        bool resizable = (style & WS_THICKFRAME) != 0;
        if (!hasCaption && !resizable)
        {
            return false; // splash screens, popups, notifications
        }

        if ((exStyle & WS_EX_TOOLWINDOW) != 0 && (exStyle & WS_EX_APPWINDOW) == 0)
        {
            return false; // floating palettes and tool windows
        }

        if (IsCloaked(hwnd) || IgnoredClasses.Contains(GetClassName(hwnd)))
        {
            return false;
        }

        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == OwnProcessId)
        {
            return false;
        }

        return excludedApps.Count == 0 || !AppExclusion.IsExcluded(GetProcessFileName(pid), excludedApps);
    }

    public static bool IsResizable(nint hwnd) => (GetStyle(hwnd) & WS_THICKFRAME) != 0;

    private static string GetWindowTitle(nint hwnd)
    {
        char* buffer = stackalloc char[256];
        int length = GetWindowText(hwnd, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    public static string? GetProcessFileName(nint hwnd)
    {
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        return GetProcessFileName(pid);
    }

    public static string? GetProcessFileName(uint pid)
    {
        long now = Environment.TickCount64;
        if (ProcessNames.TryGetValue(pid, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Name;
        }

        string? name = null;
        nint process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process != 0)
        {
            try
            {
                char* buffer = stackalloc char[1024];
                uint size = 1024;
                if (QueryFullProcessImageName(process, 0, buffer, ref size))
                {
                    name = Path.GetFileName(new string(buffer, 0, (int)size));
                }
            }
            finally
            {
                CloseHandle(process);
            }
        }

        if (ProcessNames.Count > 256)
        {
            ProcessNames.Clear(); // PIDs get recycled; a small cache is plenty.
        }

        ProcessNames[pid] = (name, now + 60_000);
        return name;
    }
}
