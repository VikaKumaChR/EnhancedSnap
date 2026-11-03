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
