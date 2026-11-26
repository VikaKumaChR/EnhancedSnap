using System.Diagnostics;
using System.Runtime.InteropServices;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Interop;

namespace EnhancedSnap.Core.Platform;

/// <summary>
/// What "Snap apps running as administrator" needs in order to work, read the same way by the engine and the settings
/// app. Windows only lets a process move windows of its own integrity level or lower: from an engine without
/// administrator rights, SetWindowPos on an elevated window returns FALSE with ERROR_ACCESS_DENIED. So the setting
/// alone proves nothing. An uninstall removes the scheduled task behind it but keeps settings.json, and so did the
/// development previews' uninstall. The engine then has to be checked, not the switch.
/// </summary>
public static unsafe partial class AdminMode
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20;

    /// <summary>The per-user scheduled task (highest privileges) that starts the engine elevated.</summary>
    public static string TaskName => $"Enhanced Snap (administrator) - {Environment.UserName}";

    /// <summary>The user's own task can be read without administrator rights.</summary>
    public static bool TaskExists() => RunSchtasks("/query", "/tn", TaskName) == 0;

    /// <summary>The running engine's process ID (from its host window), or 0.</summary>
    public static int EngineProcessId()
    {
        nint window = Win32.FindWindow(AppInfo.EngineWindowClass, null);
        if (window == 0)
        {
            return 0;
        }

        Win32.GetWindowThreadProcessId(window, out uint pid);
        return (int)pid;
    }

    /// <summary>
    /// Whether the process runs with an elevated token; null when that can't be read. Works across integrity levels:
    /// limited query access and TOKEN_QUERY are open to the same user.
    /// </summary>
    public static bool? IsElevated(int pid)
    {
        nint process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (process == 0)
        {
            return null;
        }

        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out nint token))
            {
                return null;
            }

            try
            {
                uint elevated = 0;
                return GetTokenInformation(token, TokenElevation, &elevated, sizeof(uint), out _) ? elevated != 0 : null;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>Full path of the process's executable, or null.</summary>
    public static string? ImagePath(int pid)
    {
        nint process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (process == 0)
        {
            return null;
        }

        try
        {
            char* buffer = stackalloc char[1024];
            uint length = 1024;
            return QueryFullProcessImageName(process, 0, buffer, ref length) ? new string(buffer, 0, (int)length) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>True when <paramref name="path"/> is this installation's engine (another copy doesn't count).</summary>
    public static bool IsThisEngine(string? path) =>
        path is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(AppInfo.EngineExePath), StringComparison.OrdinalIgnoreCase);

    /// <summary>Runs schtasks.exe without a window and returns its exit code (-1 when it can't run).</summary>
    public static int RunSchtasks(params string[] arguments)
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

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint tokenHandle, int tokenInformationClass, void* tokenInformation, uint tokenInformationLength, out uint returnLength);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageName(nint process, uint flags, char* exeName, ref uint size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
