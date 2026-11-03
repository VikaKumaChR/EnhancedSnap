using System.Diagnostics;
using EnhancedSnap.Core;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Interop;

namespace EnhancedSnap.Settings.Services;

/// <summary>Talks to the background engine through its hidden window (no sockets, no polling).</summary>
internal static class EngineClient
{
    private const uint WM_CLOSE = 0x0010;

    public static bool IsRunning => FindEngine() != 0;

    public static void NotifySettingsChanged() => Post(IpcMessages.EngineSettingsChanged, 0);

    /// <summary>Flash the zones on a monitor (HMONITOR handles are valid across processes; 0 = all).</summary>
    public static void PreviewZones(nint monitor) => Post(IpcMessages.EnginePreviewLayout, monitor);

    public static bool Start()
    {
        try
        {
            if (!File.Exists(AppInfo.EngineExePath))
            {
                Log.Warn($"Engine not found at {AppInfo.EngineExePath}");
                return false;
            }

            Process.Start(new ProcessStartInfo(AppInfo.EngineExePath, "--startup")
            {
                UseShellExecute = false,
                WorkingDirectory = AppInfo.BaseDirectory,
            })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not start the engine", ex);
            return false;
        }
    }

    /// <summary>
    /// Administrator mode on: run the engine elevated once (UAC) to register its scheduled task and restart elevated.
    /// False when the prompt was declined or the account can't elevate itself (over-the-shoulder credentials).
    /// </summary>
    public static async Task<bool> EnableElevationAsync()
    {
        try
        {
            var start = new ProcessStartInfo(AppInfo.EngineExePath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppInfo.BaseDirectory,
            };
            start.ArgumentList.Add("--register-elevated");
            start.ArgumentList.Add("--user");
            start.ArgumentList.Add($@"{Environment.UserDomainName}\{Environment.UserName}");
            using var process = Process.Start(start);
            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync();
            Log.Info($"Administrator mode registration exited with {process.ExitCode}");
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false; // the UAC prompt was declined
        }
        catch (Exception ex)
        {
            Log.Error("Administrator mode registration failed", ex);
            return false;
        }
    }

    /// <summary>Administrator mode off: an elevated engine drops its task and restarts without administrator rights.</summary>
    public static void DropElevation() => Post(IpcMessages.EngineDropElevation, 0);

    public static void Quit()
    {
        nint engine = FindEngine();
        if (engine != 0)
        {
            Win32.PostMessage(engine, WM_CLOSE, 0, 0);
        }
    }

    private static void Post(uint message, nint lParam)
    {
        nint engine = FindEngine();
        if (engine != 0)
        {
            Win32.PostMessage(engine, message, 0, lParam);
        }
    }

    private static nint FindEngine() => Win32.FindWindow(AppInfo.EngineWindowClass, null);
}
