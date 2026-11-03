using System.Reflection;

namespace EnhancedSnap.Core;

/// <summary>Product identity, well-known paths and names shared by every process.</summary>
public static class AppInfo
{
    public const string ProductName = "Enhanced Snap";
    public const string EngineExeName = "EnhancedSnap.exe";
    public const string SettingsExeName = "EnhancedSnap.Settings.exe";

    /// <summary>Hidden top-level window owned by the engine. Top-level (not message-only) so it
    /// receives broadcasts such as WM_DISPLAYCHANGE, WM_SETTINGCHANGE and TaskbarCreated.</summary>
    public const string EngineWindowClass = "EnhancedSnap.Engine.Host";

    /// <summary>Message-only window owned by the settings app (found with FindWindowEx(HWND_MESSAGE)).</summary>
    public const string SettingsWindowClass = "EnhancedSnap.Settings.Host";

    public const string EngineMutexName = @"Local\EnhancedSnap.Engine.5B1E2C47-9A0D-4C63-8E7F-2D4B6A1C9E30";
    public const string SettingsMutexName = @"Local\EnhancedSnap.Settings.5B1E2C47-9A0D-4C63-8E7F-2D4B6A1C9E31";

    /// <summary>Set this environment variable to isolate settings and logs (used by tests and the probe tool).</summary>
    public const string DataDirOverrideVariable = "ENHANCEDSNAP_DATA_DIR";

    /// <summary>Lowest supported OS build. 22000 = Windows 11 21H2.</summary>
    public const int MinimumWindowsBuild = 22000;

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "1.0.0";

    /// <summary>The MSIX package's family name when running from it (Microsoft Store), otherwise null.</summary>
    public static string? PackageFamilyName { get; } = ReadPackageFamilyName();

    /// <summary>
    /// Running from the MSIX package. A package has no uninstall hook and its HKCU writes and new AppData files are
    /// private to it, so: sign-in start goes through the package's startup task, the Windows Snap takeover is never
    /// written to the user profile, and administrator mode (a scheduled task outside the package) is not offered.
    /// </summary>
    public static bool IsPackaged => PackageFamilyName is not null;

    public static string DataDirectory { get; } = ResolveDataDirectory();

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static string BaseDirectory => AppContext.BaseDirectory;

    public static string EngineExePath => Path.Combine(BaseDirectory, EngineExeName);

    public static string SettingsExePath => Path.Combine(BaseDirectory, SettingsExeName);

    /// <summary>
    /// The folder File Explorer must open to show <paramref name="path"/> (under %LOCALAPPDATA%). In the package,
    /// folders Enhanced Snap created there live in the package's LocalCache, and Explorer runs outside the package.
    /// </summary>
    public static string ExplorerPath(string path)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (PackageFamilyName is null || !path.StartsWith(local + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        string redirected = Path.Combine(local, "Packages", PackageFamilyName, "LocalCache", "Local", path[(local.Length + 1)..]);
        return Directory.Exists(redirected) ? redirected : path;
    }

    private static unsafe string? ReadPackageFamilyName()
    {
        uint length = 0;
        if (Interop.Win32.GetCurrentPackageFamilyName(ref length, null) == Interop.Win32.APPMODEL_ERROR_NO_PACKAGE || length == 0)
        {
            return null;
        }

        char* buffer = stackalloc char[(int)length];
        return Interop.Win32.GetCurrentPackageFamilyName(ref length, buffer) == 0 ? new string(buffer, 0, (int)length - 1) : null;
    }

    private static string ResolveDataDirectory()
    {
        string? overrideDir = Environment.GetEnvironmentVariable(DataDirOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            return Path.GetFullPath(overrideDir);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EnhancedSnap");
    }
}

/// <summary>
/// Window messages exchanged between the engine and the settings app.
/// Both sides own the target window class, so private WM_APP values are safe.
/// </summary>
public static class IpcMessages
{
    private const uint WM_APP = 0x8000;

    /// <summary>Settings → engine: settings.json changed, reload and re-apply.</summary>
    public const uint EngineSettingsChanged = WM_APP + 0x101;

    /// <summary>Settings → engine: flash the zones of a monitor. lParam = HMONITOR (0 = all monitors).</summary>
    public const uint EnginePreviewLayout = WM_APP + 0x102;

    /// <summary>
    /// Settings → engine: "Snap apps running as administrator" was turned off. An elevated engine removes its
    /// scheduled task and restarts without administrator rights.
    /// </summary>
    public const uint EngineDropElevation = WM_APP + 0x103;

    /// <summary>Engine → settings: settings.json was changed by the engine (for example tray "Pause").</summary>
    public const uint SettingsReload = WM_APP + 0x201;

    /// <summary>Second settings instance → first: bring the window forward. wParam = <see cref="SettingsPage"/>.</summary>
    public const uint SettingsActivate = WM_APP + 0x202;
}

/// <summary>Pages of the settings app that can be opened directly (tray menu, command line).</summary>
public enum SettingsPage
{
    General = 0,
    Layouts = 1,
    Appearance = 2,
    Keyboard = 3,
    Apps = 4,
    About = 5,
}
