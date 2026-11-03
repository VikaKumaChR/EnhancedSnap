using EnhancedSnap.Core.Diagnostics;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace EnhancedSnap.Core.Platform;

/// <summary>
/// "Launch at sign-in" through the per-user Run key. Also honours the Task Manager "Startup apps"
/// switch (StartupApproved), so turning it back on in Enhanced Snap really re-enables it.
/// <para>
/// The Store package can't use the Run key (its HKCU writes are private to the package): it declares a startup task
/// (packaging/AppxManifest.xml), which Windows lists in Settings → Apps → Startup. Once the user turns that off there,
/// only they can turn it back on.
/// </para>
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "EnhancedSnap";

    /// <summary>TaskId of the package's windows.startupTask extension.</summary>
    public const string PackagedTaskId = "EnhancedSnapStartup";

    // With administrator mode on, this unelevated sign-in start hands over to the engine's scheduled task.

    public static bool IsEnabled()
    {
        if (AppInfo.IsPackaged)
        {
            return TaskState() is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }

        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            if (run?.GetValue(ValueName) is not string)
            {
                return false;
            }

            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath);
            // First byte: even = enabled (02/06), odd = disabled by the user in Task Manager (03/07).
            return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } data || (data[0] & 1) == 0;
        }
        catch (Exception ex)
        {
            Log.Warn($"Startup registration check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>The package's startup task was turned off in Windows Settings (or by policy): the app can't turn it on.</summary>
    public static bool IsBlocked() =>
        AppInfo.IsPackaged && TaskState() is StartupTaskState.DisabledByUser or StartupTaskState.DisabledByPolicy;

    public static void SetEnabled(bool enabled)
    {
        if (AppInfo.IsPackaged)
        {
            SetTask(enabled);
            return;
        }

        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
            {
                run.SetValue(ValueName, $"\"{AppInfo.EngineExePath}\" --startup", RegistryValueKind.String);
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: true);
                approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            else
            {
                run.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to update startup registration", ex);
        }
    }

    // StartupTask completes on a thread-pool thread and shows no prompt for desktop apps, so waiting is safe
    // from the UI thread (the call takes a few milliseconds).
    private static StartupTaskState TaskState()
    {
        try
        {
            return Task.Run(async () => (await StartupTask.GetAsync(PackagedTaskId)).State).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Warn($"Startup task check failed: {ex.Message}");
            return StartupTaskState.Disabled;
        }
    }

    private static void SetTask(bool enabled)
    {
        try
        {
            var state = Task.Run(async () =>
            {
                var task = await StartupTask.GetAsync(PackagedTaskId);
                if (!enabled)
                {
                    task.Disable();
                    return task.State;
                }

                return await task.RequestEnableAsync();
            }).GetAwaiter().GetResult();
            Log.Info($"Startup task: {state}");
        }
        catch (Exception ex)
        {
            Log.Error("Failed to update the startup task", ex);
        }
    }
}
