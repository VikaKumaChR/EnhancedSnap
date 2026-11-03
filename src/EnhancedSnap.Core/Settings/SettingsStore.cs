using System.Text.Json;
using EnhancedSnap.Core.Diagnostics;

namespace EnhancedSnap.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/>. Saving writes a temp file and atomically replaces
/// the target, so the engine never reads a half-written file while the settings app is saving.
/// </summary>
public static class SettingsStore
{
    public static AppSettings Load() => Load(out _);

    public static AppSettings Load(out bool existed)
    {
        string path = AppInfo.SettingsPath;
        existed = File.Exists(path);
        if (!existed)
        {
            return CreateDefault();
        }

        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var settings = JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings) ?? CreateDefault();
                settings.Normalize();
                return settings;
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(25); // Another process is replacing the file right now.
            }
            catch (Exception ex)
            {
                Log.Error("settings.json is invalid; using defaults", ex);
                TryBackupCorruptFile(path);
                return CreateDefault();
            }
        }

        return CreateDefault();
    }

    public static bool Save(AppSettings settings)
    {
        string path = AppInfo.SettingsPath;
        try
        {
            settings.Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(temp, path, overwrite: true);
                    return true;
                }
                catch (IOException) when (attempt < 5)
                {
                    Thread.Sleep(20);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save settings", ex);
            return false;
        }
    }

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        settings.Normalize();
        return settings;
    }

    private static void TryBackupCorruptFile(string path)
    {
        try
        {
            File.Copy(path, path + ".corrupt", overwrite: true);
        }
        catch
        {
            // Best effort only.
        }
    }
}
