using EnhancedSnap.Engine.Native;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Platform;

/// <summary>
/// Windows 11 "Efficiency mode" (EcoQoS). The engine is idle almost all the time, so the process runs with
/// EcoQoS: when it does wake up for bookkeeping, Windows schedules it on efficient cores at low frequency.
/// Latency-sensitive work (an active drag, the keyboard hook thread) opts its thread back into HighQoS.
/// </summary>
internal static unsafe class EfficiencyMode
{
    private const uint CurrentVersion = 1;

    public static void EnableForProcess()
    {
        var state = new POWER_THROTTLING_STATE
        {
            Version = CurrentVersion,
            ControlMask = POWER_THROTTLING_EXECUTION_SPEED | POWER_THROTTLING_IGNORE_TIMER_RESOLUTION,
            StateMask = POWER_THROTTLING_EXECUTION_SPEED | POWER_THROTTLING_IGNORE_TIMER_RESOLUTION,
        };
        SetProcessInformation(CurrentProcess, ProcessPowerThrottling, &state, (uint)sizeof(POWER_THROTTLING_STATE));
    }

    /// <summary>Opt the calling thread out of (or back into) EcoQoS.</summary>
    public static void SetCurrentThreadHighQos(bool highQos)
    {
        var state = new POWER_THROTTLING_STATE
        {
            Version = CurrentVersion,
            ControlMask = POWER_THROTTLING_EXECUTION_SPEED,
            StateMask = highQos ? 0 : POWER_THROTTLING_EXECUTION_SPEED,
        };
        SetThreadInformation(CurrentThread, ThreadPowerThrottling, &state, (uint)sizeof(POWER_THROTTLING_STATE));
    }
}
