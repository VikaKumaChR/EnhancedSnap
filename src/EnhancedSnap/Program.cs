using EnhancedSnap.Core;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Interop;
using EnhancedSnap.Core.Localization;
using EnhancedSnap.Core.Settings;
using EnhancedSnap.Engine.Native;

namespace EnhancedSnap.Engine;

/// <summary>
/// EnhancedSnap.exe [--startup] [--first-run] [--settings] [--page &lt;name&gt;] [--quit] [--verbose]
/// Administrator mode: [--register-elevated [--user DOMAIN\name]] (run elevated by the settings app), [--elevated]
/// (set by the scheduled task), [--after &lt;pid&gt;] (wait for the previous engine to exit).
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool Has(string flag) => args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
        string? Value(string flag)
        {
            int index = Array.FindIndex(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        Log.Initialize("engine", verbose: Has("--verbose"));
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled exception", e.ExceptionObject as Exception);
            Platform.WindowsSnapOverride.Restore(); // never leave the user without Windows Snap
        };

        if (Has("--restore-snap"))
        {
            // Used by the uninstaller: put Windows Snap back if a previous run could not; drop the elevated task.
            Platform.WindowsSnapOverride.RecoverAfterCrash();
            Platform.Elevation.UnregisterTask();
            return 0;
        }

        if (Has("--register-elevated"))
        {
            return RegisterElevated(Value("--user"));
        }

        if (Value("--after") is { } previous && int.TryParse(previous, out int previousPid))
        {
            WaitForExit(previousPid); // an engine handing over (administrator mode on or off) must be gone first
        }
        if (Snapping.WindowRules.TestWindowTitle is { } testTitle)
        {
            Log.Warn($"TEST MODE: only the window titled \"{testTitle}\" will be snapped");
        }

        if (Environment.OSVersion.Version.Build < AppInfo.MinimumWindowsBuild)
        {
            var strings = Strings.Use(null);
            NativeMethods.MessageBox(0, strings.Notify_UnsupportedOs, AppInfo.ProductName, 0x10 /* MB_ICONERROR */);
            return 1;
        }

        if (Has("--quit"))
        {
            nint running = Win32.FindWindow(AppInfo.EngineWindowClass, null);
            if (running != 0)
            {
                Win32.PostMessage(running, NativeMethods.WM_CLOSE, 0, 0);
            }

            return 0;
        }

        // Administrator mode: an unelevated start (sign-in, Start menu) hands over to the elevated task.
        // Not in the Store package (settings carried over from the MSI may still have it on).
        if (!Platform.Elevation.IsElevated && !Has("--elevated") && Snapping.WindowRules.TestWindowTitle is null && !AppInfo.IsPackaged
            && Win32.FindWindow(AppInfo.EngineWindowClass, null) == 0 && SettingsStore.Load().RunAsAdministrator)
        {
            if (Platform.Elevation.RunTask())
            {
                Log.Info("Started the elevated engine through its scheduled task");
                return 0;
            }

            Log.Warn("Administrator mode is on but its scheduled task did not start; running without administrator rights");
        }

        var page = ParsePage(args);
        Mutex? mutex = null;
        bool isFirstInstance;
        try
        {
            mutex = new Mutex(initiallyOwned: true, AppInfo.EngineMutexName, out isFirstInstance);
        }
        catch (UnauthorizedAccessException)
        {
            isFirstInstance = false; // held by an elevated engine
        }

        using var owned = mutex;
        if (!isFirstInstance)
        {
            // Already running: launching the app again (Start menu, desktop shortcut) opens Settings.
            if (!Has("--startup"))
            {
                EngineHost.LaunchSettings(page ?? SettingsPage.General);
            }

            return 0;
        }

        var settings = SettingsStore.Load(out bool existed);
        if (!existed)
        {
            SettingsStore.Save(settings);
        }

        Strings.Use(settings.Language);

        bool firstRun = !existed || Has("--first-run");
        SettingsPage? open = page
            ?? (Has("--settings") || (firstRun && !Has("--startup")) ? SettingsPage.General : null);

        try
        {
            using var host = new EngineHost(settings);
            return host.Run(open, welcome: firstRun);
        }
        catch (Exception ex)
        {
            Log.Error("Engine crashed", ex);
            return 2;
        }
        finally
        {
            Log.Info("Engine stopped");
        }
    }

    /// <summary>
    /// Run elevated (UAC) by the settings app: register the task, close the running engine, start the elevated one.
    /// Exit codes: 0 ok, 3 not elevated, 4 elevated as another account, 5 registration failed, 6 task did not start.
    /// </summary>
    private static int RegisterElevated(string? user)
    {
        if (!Platform.Elevation.IsElevated)
        {
            return 3;
        }

        if (!Platform.Elevation.IsSameUser(user))
        {
            Log.Warn("Elevated with a different account; administrator mode needs the signed-in user to be an administrator");
            return 4;
        }

        if (!Platform.Elevation.RegisterTask())
        {
            return 5;
        }

        nint running = Win32.FindWindow(AppInfo.EngineWindowClass, null);
        if (running != 0)
        {
            Win32.GetWindowThreadProcessId(running, out uint pid);
            Win32.PostMessage(running, NativeMethods.WM_CLOSE, 0, 0); // restores Windows Snap; the elevated engine takes over again
            WaitForExit((int)pid);
        }

        return Platform.Elevation.RunTask() ? 0 : 6;
    }

    private static void WaitForExit(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            process.WaitForExit(10_000);
        }
        catch (ArgumentException)
        {
            // already gone
        }
        catch (Exception ex)
        {
            Log.Warn($"Waiting for process {pid}: {ex.Message}");
        }
    }

    private static SettingsPage? ParsePage(string[] args)
    {
        int index = Array.FindIndex(args, a => string.Equals(a, "--page", StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + 1 < args.Length && Enum.TryParse<SettingsPage>(args[index + 1], ignoreCase: true, out var page))
        {
            return page;
        }

        return null;
    }
}
