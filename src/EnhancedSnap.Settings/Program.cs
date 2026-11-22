using EnhancedSnap.Core;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Interop;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace EnhancedSnap.Settings;

/// <summary>
/// EnhancedSnap.Settings.exe [--page general|layouts|appearance|keyboard|apps|about] [--welcome]
/// Single instance: a second launch forwards its page to the first and exits.
/// </summary>
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Log.Initialize("settings");
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Installed (not packaged): the taskbar shows the window as "Enhanced Snap", grouped with the Start menu
        // shortcut, instead of under the executable's name. It has to be set before the app shows any UI (Learn,
        // Application User Model IDs). A package already has an ID of its own, which this would replace.
        if (!AppInfo.IsPackaged && Win32.SetCurrentProcessExplicitAppUserModelID(AppInfo.AppUserModelId) is int hr and < 0)
        {
            Log.Warn($"SetCurrentProcessExplicitAppUserModelID failed: 0x{hr:X8}");
        }

        var page = ParsePage(args);
        bool welcome = args.Any(a => string.Equals(a, "--welcome", StringComparison.OrdinalIgnoreCase));

        using var mutex = new Mutex(initiallyOwned: true, AppInfo.SettingsMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            nint host = Win32.FindWindowEx(Win32.HWND_MESSAGE, 0, AppInfo.SettingsWindowClass, null);
            if (host != 0)
            {
                Win32.AllowSetForegroundWindow(Win32.ASFW_ANY);
                Win32.PostMessage(host, IpcMessages.SettingsActivate, (nuint)(int)page, 0);
            }

            return 0;
        }

        Application.Start(initialization =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(page, welcome);
        });
        return 0;
    }

    private static SettingsPage ParsePage(string[] args)
    {
        int index = Array.FindIndex(args, a => string.Equals(a, "--page", StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length && Enum.TryParse<SettingsPage>(args[index + 1], ignoreCase: true, out var page)
            ? page
            : SettingsPage.General;
    }
}
