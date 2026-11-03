using System.Text;

namespace EnhancedSnap.Core.Diagnostics;

/// <summary>
/// Tiny append-only file logger (one file per process, rolled at 512 KB).
/// Writes are synchronous but rare: the engine only logs lifecycle events and failures,
/// never per-mouse-move data, so the disk stays idle during normal use.
/// </summary>
public static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly Lock Gate = new();
    private static string? _path;
    private static bool _verbose;

    public static void Initialize(string processName, bool verbose = false)
    {
        _verbose = verbose;
        try
        {
            Directory.CreateDirectory(AppInfo.LogDirectory);
            _path = Path.Combine(AppInfo.LogDirectory, processName + ".log");
            Info($"---- {AppInfo.ProductName} {AppInfo.Version} ({processName}) on {Environment.OSVersion.VersionString}");
        }
        catch
        {
            _path = null; // Logging must never take the app down.
        }
    }

    public static void Debug(string message)
    {
        if (_verbose)
        {
            Write("DBG", message);
        }
    }

    public static void Info(string message) => Write("INF", message);

    public static void Warn(string message) => Write("WRN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    private static void Write(string level, string message)
    {
        if (_path is null)
        {
            return;
        }

        lock (Gate)
        {
            try
            {
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(_path, _path + ".old", overwrite: true);
                }

                File.AppendAllText(
                    _path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
            catch
            {
                // Ignore: disk full, file locked by an editor, etc.
            }
        }
    }
}
