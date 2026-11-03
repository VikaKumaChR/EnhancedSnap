using System.Runtime.InteropServices;

namespace EnhancedSnap.Settings.Services;

/// <summary>
/// The settings window should cost almost nothing while it sits in the background. A few seconds after it loses focus
/// the process switches to EcoQoS (Task Manager's "efficiency mode"), compacts the managed heap and hands its working
/// set back to Windows; activating the window reverses the QoS, and pages come back on demand. Without this an idle
/// WinUI window keeps 100+ MB resident — as much as Phone Link.
/// </summary>
internal static partial class BackgroundFootprint
{
    private const int ProcessPowerThrottling = 4;
    private const uint PowerThrottlingExecutionSpeed = 0x1;
    private static readonly nint CurrentProcess = -1;

    public static void Enter()
    {
        SetEcoQos(true);
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        SetProcessWorkingSetSizeEx(CurrentProcess, -1, -1, 0); // (-1, -1): trim the working set
    }

    public static void Leave() => SetEcoQos(false);

    private static void SetEcoQos(bool on)
    {
        var state = new PowerThrottlingState
        {
            Version = 1,
            ControlMask = PowerThrottlingExecutionSpeed,
            StateMask = on ? PowerThrottlingExecutionSpeed : 0,
        };
        SetProcessInformation(CurrentProcess, ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<PowerThrottlingState>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessInformation(nint process, int informationClass, ref PowerThrottlingState information, uint size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessWorkingSetSizeEx(nint process, nint minimum, nint maximum, uint flags);
}
