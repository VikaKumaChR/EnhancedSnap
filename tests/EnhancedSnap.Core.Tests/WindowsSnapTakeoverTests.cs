using EnhancedSnap.Core.Platform;

namespace EnhancedSnap.Core.Tests;

/// <summary>
/// The order of the Windows Snap takeover. Setting the real switch broadcasts WM_SETTINGCHANGE, and the engine's own
/// window gets it while the set call is still running; the fake switch here does the same, calling
/// <see cref="WindowsSnapTakeover.OnSwitchChanged"/> from inside the set as the engine host does. Nothing here touches
/// the system setting.
/// </summary>
public sealed class WindowsSnapTakeoverTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "EnhancedSnap.Tests", Guid.NewGuid().ToString("N"));

    private string MarkerPath => Path.Combine(_directory, "windows-snap-replaced.marker");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public void Restore_sets_the_switch_once_although_its_broadcast_comes_back()
    {
        var (takeover, windowsSnap) = Create(on: true);
        takeover.Apply();
        windowsSnap.Sets.Clear();

        takeover.Restore();

        Assert.Equal([true], windowsSnap.Sets);
        Assert.Equal(1, windowsSnap.DeepestSet);
        Assert.True(windowsSnap.On);
        Assert.False(takeover.IsApplied);
        Assert.False(File.Exists(MarkerPath));
    }

    [Fact]
    public void Restore_drops_the_takeover_before_the_broadcast_and_keeps_the_marker_until_the_set_returns()
    {
        var (takeover, windowsSnap) = Create(on: true);
        takeover.Apply();
        bool? appliedDuringSet = null;
        bool? markerAfterBroadcast = null;
        windowsSnap.BeforeBroadcast = () => appliedDuringSet = takeover.IsApplied;
        windowsSnap.AfterBroadcast = () => markerAfterBroadcast = File.Exists(MarkerPath);

        takeover.Restore();

        // Our own broadcast must not read as "switched back on elsewhere", and a crash inside the set call must still
        // find the marker at the next start.
        Assert.False(appliedDuringSet);
        Assert.True(markerAfterBroadcast);
        Assert.False(File.Exists(MarkerPath));
    }

    [Fact]
    public void Switched_back_on_elsewhere_drops_the_takeover_without_setting_the_switch()
    {
        var (takeover, windowsSnap) = Create(on: true);
        takeover.Apply();
        windowsSnap.Sets.Clear();

        windowsSnap.On = true; // Windows Settings → Multitasking → Snap windows
        takeover.OnSwitchChanged();

        Assert.Empty(windowsSnap.Sets);
        Assert.False(takeover.IsApplied);
        Assert.False(File.Exists(MarkerPath));

        takeover.Restore(); // pause or exit afterwards: nothing left to give back
        Assert.Empty(windowsSnap.Sets);
    }

    [Fact]
    public void A_change_that_leaves_Windows_Snap_off_keeps_the_takeover()
    {
        var (takeover, windowsSnap) = Create(on: true);
        takeover.Apply();
        windowsSnap.Sets.Clear();

        takeover.OnSwitchChanged(); // another program sets SPI_SETWINARRANGING again, or turns it off as well

        Assert.True(takeover.IsApplied);
        Assert.True(File.Exists(MarkerPath));
        Assert.Empty(windowsSnap.Sets);

        takeover.Restore(); // and it is still given back
        Assert.Equal([true], windowsSnap.Sets);
        Assert.True(windowsSnap.On);
        Assert.False(File.Exists(MarkerPath));
    }

    [Fact]
    public void Apply_keeps_the_takeover_through_its_own_broadcast()
    {
        var (takeover, windowsSnap) = Create(on: true);

        takeover.Apply();

        Assert.Equal([false], windowsSnap.Sets);
        Assert.Equal(1, windowsSnap.DeepestSet);
        Assert.False(windowsSnap.On);
        Assert.True(takeover.IsApplied);
        Assert.True(File.Exists(MarkerPath));
    }

    [Fact]
    public void Apply_does_nothing_when_Windows_Snap_is_already_off()
    {
        var (takeover, windowsSnap) = Create(on: false);

        takeover.Apply();
        takeover.Restore();

        Assert.Empty(windowsSnap.Sets);
        Assert.False(takeover.IsApplied);
        Assert.False(File.Exists(MarkerPath));
    }

    [Fact]
    public void Apply_removes_the_marker_when_the_switch_cannot_be_set()
    {
        var (takeover, windowsSnap) = Create(on: true);
        windowsSnap.Fails = true;

        takeover.Apply();

        Assert.False(takeover.IsApplied);
        Assert.False(File.Exists(MarkerPath));
    }

    [Fact]
    public void Apply_does_not_take_over_when_the_marker_cannot_be_written()
    {
        Directory.CreateDirectory(_directory);
        string file = Path.Combine(_directory, "not-a-folder");
        File.WriteAllText(file, "");
        var windowsSnap = new FakeWindowsSnap { On = true };
        var takeover = new WindowsSnapTakeover(Path.Combine(file, "windows-snap-replaced.marker"), () => windowsSnap.On, windowsSnap.Set);
        windowsSnap.Takeover = takeover;

        takeover.Apply();

        Assert.Empty(windowsSnap.Sets);
        Assert.False(takeover.IsApplied);
        Assert.True(windowsSnap.On);
    }

    [Fact]
    public void A_marker_left_by_a_crash_is_restored_once_at_the_next_start()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(MarkerPath, "2026-11-22T12:19:17");
        var (takeover, windowsSnap) = Create(on: false);

        takeover.RecoverAfterCrash();
        takeover.RecoverAfterCrash();

        Assert.Equal([true], windowsSnap.Sets);
        Assert.Equal(1, windowsSnap.DeepestSet);
        Assert.True(windowsSnap.On);
        Assert.False(File.Exists(MarkerPath));
    }

    [Fact]
    public void Nothing_to_recover_without_a_marker()
    {
        var (takeover, windowsSnap) = Create(on: true);

        takeover.RecoverAfterCrash();

        Assert.Empty(windowsSnap.Sets);
    }

    private (WindowsSnapTakeover Takeover, FakeWindowsSnap WindowsSnap) Create(bool on)
    {
        var windowsSnap = new FakeWindowsSnap { On = on };
        var takeover = new WindowsSnapTakeover(MarkerPath, () => windowsSnap.On, windowsSnap.Set);
        windowsSnap.Takeover = takeover;
        return (takeover, windowsSnap);
    }

    /// <summary>The SPI_SETWINARRANGING switch, with its broadcast reaching the engine's window inside the call.</summary>
    private sealed class FakeWindowsSnap
    {
        private const int Limit = 8; // the real broadcast nested 22 to 31 levels deep in 1.0.1 and earlier
        private int _depth;

        public bool On { get; set; }

        public bool Fails { get; set; }

        public List<bool> Sets { get; } = [];

        public int DeepestSet { get; private set; }

        public WindowsSnapTakeover? Takeover { get; set; }

        public Action? BeforeBroadcast { get; set; }

        public Action? AfterBroadcast { get; set; }

        public bool Set(bool enabled)
        {
            Sets.Add(enabled);
            if (Fails)
            {
                return false;
            }

            _depth++;
            DeepestSet = Math.Max(DeepestSet, _depth);
            try
            {
                Assert.True(_depth <= Limit, "setting the switch nested inside its own broadcast");
                On = enabled;
                BeforeBroadcast?.Invoke();
                Takeover!.OnSwitchChanged(); // WM_SETTINGCHANGE reaching the engine's window
                AfterBroadcast?.Invoke();
                return true;
            }
            finally
            {
                _depth--;
            }
        }
    }
}
