using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using EnhancedSnap.Core;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Interop;
using EnhancedSnap.Engine.Native;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Platform;

/// <summary>
/// "Snap apps running as administrator". Windows only lets a process move windows of its own integrity level or lower,
/// so arranging an elevated window (an admin terminal) needs an elevated engine — the same reason Windows Snap works
/// there and PowerToys offers "run as administrator". Elevation goes through a scheduled task with highest
/// privileges: registered once (one UAC prompt, from the settings app), it then starts the engine elevated without
/// prompting, at sign-in too. The settings app and anything launched from the tray stay unelevated.
/// Not offered in the Store package (<see cref="AppInfo.IsPackaged"/>): the task would start the engine outside it.
/// </summary>
// UPDATE: Store version: an elevated start with package identity (allowElevation capability, needs Store approval).
internal static unsafe class Elevation
{
    public static bool IsElevated { get; } = Environment.IsPrivilegedProcess;

    private static string TaskName => $"Enhanced Snap (administrator) - {Environment.UserName}";

    private static string CurrentUser => $@"{Environment.UserDomainName}\{Environment.UserName}";

    /// <summary>Registers the task that starts this executable elevated, on demand only (no trigger).</summary>
    public static bool RegisterTask()
    {
        string exe = AppInfo.EngineExePath;
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.3" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>{SecurityElement.Escape(AppInfo.ProductName)}</Author>
                <Description>Starts Enhanced Snap with administrator rights so it can snap windows of apps running as administrator.</Description>
              </RegistrationInfo>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(CurrentUser)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>"{SecurityElement.Escape(exe)}"</Command>
                  <Arguments>--startup --elevated</Arguments>
                  <WorkingDirectory>{SecurityElement.Escape(AppInfo.BaseDirectory)}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;

        string file = Path.Combine(Path.GetTempPath(), $"EnhancedSnap-task-{Environment.ProcessId}.xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode); // UTF-16 with BOM, as declared
            int exit = Schtasks("/create", "/tn", TaskName, "/xml", file, "/f");
            Log.Info($"Elevated task registration: exit {exit}");
            return exit == 0;
        }
        finally
        {
            File.Delete(file);
        }
    }

    public static bool RunTask() => Schtasks("/run", "/tn", TaskName) == 0;

    public static bool TaskExists() => Schtasks("/query", "/tn", TaskName) == 0;

    /// <summary>Best effort: needs administrator rights when the task was registered elevated.</summary>
    public static bool UnregisterTask() => TaskExists() && Schtasks("/delete", "/tn", TaskName, "/f") == 0;

    /// <summary>The account the elevated helper must run as: over-the-shoulder elevation (another account) can't work.</summary>
    public static bool IsSameUser(string? expected) =>
        expected is null || string.Equals(expected, CurrentUser, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Starts a program without administrator rights from an elevated process, with the shell's token (the technique
    /// PowerToys uses). Returns false when that is not possible; callers then fall back to a normal start.
    /// </summary>
    public static bool StartUnelevated(string path, string arguments)
    {
        nint shell = GetShellWindow();
        if (shell == 0)
        {
            return false;
        }

        Win32.GetWindowThreadProcessId(shell, out uint pid);
        nint process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
        {
            return false;
        }

        nint token = 0;
        nint primary = 0;
        try
        {
            if (!OpenProcessToken(process, TOKEN_DUPLICATE, out token)
                || !DuplicateTokenEx(token, TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_ASSIGN_PRIMARY | TOKEN_ADJUST_DEFAULT | TOKEN_ADJUST_SESSIONID, 0, SecurityImpersonation, TokenPrimary, out primary))
            {
                return false;
            }

            var startup = new STARTUPINFOW { cb = (uint)sizeof(STARTUPINFOW) };
            char[] commandLine = $"\"{path}\" {arguments}\0".ToCharArray(); // the API may write to it
            fixed (char* command = commandLine)
            {
                if (!CreateProcessWithTokenW(primary, 0, path, command, 0, 0, Path.GetDirectoryName(path), &startup, out var info))
                {
                    Log.Warn($"Unelevated start failed ({Marshal.GetLastPInvokeError()})");
                    return false;
                }

                CloseHandle(info.hThread);
                CloseHandle(info.hProcess);
                return true;
            }
        }
        finally
        {
            if (primary != 0)
            {
                CloseHandle(primary);
            }

            if (token != 0)
            {
                CloseHandle(token);
            }

            CloseHandle(process);
        }
    }

    /// <summary>Lets unelevated processes (the settings app, the taskbar, the uninstaller) reach an elevated window.</summary>
    public static void AllowMessages(nint hwnd, params ReadOnlySpan<uint> messages)
    {
        if (!IsElevated)
        {
            return;
        }

        foreach (uint message in messages)
        {
            ChangeWindowMessageFilterEx(hwnd, message, MSGFLT_ALLOW, 0);
        }
    }

    private static int Schtasks(params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)!;
            process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit(15000);
            if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error) && arguments[0] != "/query")
            {
                Log.Warn($"schtasks {arguments[0]}: {error.Trim()}");
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Warn($"schtasks failed: {ex.Message}");
            return -1;
        }
    }
}
