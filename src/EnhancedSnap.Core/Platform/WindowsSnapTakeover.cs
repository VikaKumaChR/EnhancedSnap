using EnhancedSnap.Core.Diagnostics;

namespace EnhancedSnap.Core.Platform;

/// <summary>
/// The bookkeeping of taking over from Windows Snap: whether Enhanced Snap turned the switch off, the marker file that
/// outlives a crash, and the order of the steps. The switch itself (SPI_SETWINARRANGING) is passed in, so the order can
/// be tested without touching the system setting.
/// <para>
/// Setting the switch broadcasts WM_SETTINGCHANGE, and the engine's own window gets it while the set call is still
/// running. So <see cref="Restore"/> drops the takeover before it sets the switch, and the notification that comes back
/// finds nothing left to give back; and <see cref="OnSwitchChanged"/>, which stands down when someone else turns
/// Windows Snap back on, never sets the switch itself. Restoring first and dropping the takeover afterwards made each
/// broadcast restore again from inside the last one, 22 to 31 levels deep, and held the engine for 3 to 13 seconds at
/// pause and about 5 at uninstall and shutdown.
/// </para>
/// </summary>
public sealed class WindowsSnapTakeover
{
    private readonly string _markerPath;
    private readonly Func<bool> _isWindowsSnapOn;
    private readonly Func<bool, bool> _setWindowsSnap;

    /// <param name="markerPath">File that records the takeover until Windows Snap is back on.</param>
    /// <param name="isWindowsSnapOn">Reads the switch.</param>
    /// <param name="setWindowsSnap">Sets the switch and broadcasts the change; false when it could not be set.</param>
    public WindowsSnapTakeover(string markerPath, Func<bool> isWindowsSnapOn, Func<bool, bool> setWindowsSnap)
    {
        _markerPath = markerPath;
        _isWindowsSnapOn = isWindowsSnapOn;
        _setWindowsSnap = setWindowsSnap;
    }

    /// <summary>
    /// Enhanced Snap holds the takeover: set once it has turned Windows Snap off; dropped just before it gives the switch
    /// back, or when someone else turns Windows Snap on.
    /// </summary>
    public bool IsApplied { get; private set; }

    /// <summary>Turn Windows Snap off if it is on. No-op when the user already had it off.</summary>
    public void Apply()
    {
        if (IsApplied)
        {
            return;
        }

        if (!_isWindowsSnapOn())
        {
            Log.Info("Windows Snap is already off; nothing to take over");
            return;
        }

        try
        {
            if (Path.GetDirectoryName(_markerPath) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_markerPath, DateTime.Now.ToString("O"));
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not write the Windows Snap marker ({ex.Message}); not taking over");
            return;
        }

        if (_setWindowsSnap(false))
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
    public void Restore()
    {
        if (!IsApplied && !File.Exists(_markerPath))
        {
            return;
        }

        // Dropped before the switch is set, because the broadcast reaches the engine's window inside the set call. The
        // marker is deleted only after the set call returns, so a crash in between still restores at the next start.
        IsApplied = false;
        _setWindowsSnap(true);
        TryDeleteMarker();
        Log.Info("Windows Snap restored");
    }

    /// <summary>At startup: a marker without a live owner means the previous run did not get to restore.</summary>
    public void RecoverAfterCrash()
    {
        if (File.Exists(_markerPath))
        {
            Log.Warn("Previous run did not restore Windows Snap; restoring now");
            Restore();
        }
    }

    /// <summary>
    /// The switch changed (WM_SETTINGCHANGE with SPI_SETWINARRANGING): set by Windows Settings, another program or
    /// Enhanced Snap itself. Windows Snap on while the takeover is still held means someone else switched it back on, so
    /// only the takeover and its marker are dropped: the switch already has the value it should have, and setting it
    /// again would broadcast once more. (The engine takes over again at its next settings reload.)
    /// </summary>
    public void OnSwitchChanged()
    {
        if (!IsApplied || !_isWindowsSnapOn())
        {
            return;
        }

        IsApplied = false;
        TryDeleteMarker();
        Log.Info("Windows Snap was re-enabled externally; Enhanced Snap stops replacing it for this session");
    }

    private void TryDeleteMarker()
    {
        try
        {
            File.Delete(_markerPath);
        }
        catch
        {
            // Harmless: the next start will restore again, which is idempotent.
        }
    }
}
