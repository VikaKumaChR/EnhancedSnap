using EnhancedSnap.Core;
using EnhancedSnap.Core.Diagnostics;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Platform;

/// <summary>
/// Takes over from Windows Snap while Enhanced Snap runs. Flips the same switch as
/// Settings → System → Multitasking → "Snap windows" (SPI_SETWINARRANGING), which turns off every built-in
/// Snap surface at once: edge snapping, the maximize-button flyout, the snap bar, Snap Assist, snap groups
/// and Win + Arrow / Win + Z. The previous value is restored when Enhanced Snap pauses or exits.
/// <para>
/// Crash safety: a marker file records that the switch was turned off by us. If the process dies without
/// restoring, the next start (or <c>EnhancedSnap.exe --restore-snap</c>, run by the uninstaller) puts it back.
/// The Store package has no uninstall hook, so there the switch is changed for this sign-in only and never written
/// to the user profile: removing the app while it runs leaves Windows Snap off until the next sign-in at most.
/// </para>
/// </summary>
internal static unsafe class WindowsSnapOverride
{
    private static string MarkerPath => Path.Combine(AppInfo.DataDirectory, "windows-snap-replaced.marker");

    public static bool IsApplied { get; private set; }

    public static bool IsWindowsSnapOn()
    {
        int value = 1;
        return !SystemParametersInfo(SPI_GETWINARRANGING, 0, &value, 0) || value != 0;
    }

    /// <summary>Turn Windows Snap off if it is on. No-op when the user already had it off.</summary>
    public static void Apply()
    {
        if (IsApplied)
        {
            return;
        }

        if (!IsWindowsSnapOn())
        {
            Log.Info("Windows Snap is already off; nothing to take over");
            return;
        }

        try
        {
            Directory.CreateDirectory(AppInfo.DataDirectory);
            File.WriteAllText(MarkerPath, DateTime.Now.ToString("O"));
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not write the Windows Snap marker ({ex.Message}); not taking over");
            return;
        }

        if (Set(enabled: false))
        {
            IsApplied = true;
            Log.Info("Windows Snap turned off (Enhanced Snap replaces it)");
        }
        else
        {
            TryDeleteMarker();
        }
    }

    /// <summary>Give Windows Snap back (pause, exit, uninstall, crash recovery).</summary>
    public static void Restore()
    {
        if (!IsApplied && !File.Exists(MarkerPath))
        {
            return;
        }

        Set(enabled: true);
        IsApplied = false;
        TryDeleteMarker();
        Log.Info("Windows Snap restored");
    }

    /// <summary>At startup: a marker without a live owner means the previous run did not get to restore.</summary>
    public static void RecoverAfterCrash()
    {
        if (File.Exists(MarkerPath))
        {
            Log.Warn("Previous run did not restore Windows Snap; restoring now");
            Restore();
        }
    }

    private static bool Set(bool enabled)
    {
        // pvParam carries the BOOL for the SET variant; uiParam is passed too for older shells that read it.
        uint flags = AppInfo.IsPackaged ? SPIF_SENDCHANGE : SPIF_UPDATEINIFILE | SPIF_SENDCHANGE;
        bool ok = SystemParametersInfo(SPI_SETWINARRANGING, enabled ? 1u : 0u, (void*)(nint)(enabled ? 1 : 0), flags);
        if (!ok)
        {
            Log.Warn($"SPI_SETWINARRANGING({enabled}) failed");
        }

        return ok;
    }

    private static void TryDeleteMarker()
    {
        try
        {
            File.Delete(MarkerPath);
        }
        catch
        {
            // Harmless: the next start will restore again, which is idempotent.
        }
    }
}
