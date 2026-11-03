using System.Runtime.InteropServices;
using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Core.Interop;

namespace EnhancedSnap.Core.Displays;

/// <summary>A connected monitor. All rectangles are physical pixels (the processes are Per-Monitor V2 aware).</summary>
public sealed record DisplayInfo(
    nint Handle,
    string Id,
    string GdiDeviceName,
    string FriendlyName,
    RectI Bounds,
    RectI WorkArea,
    int Dpi,
    bool IsPrimary,
    bool IsInternal,
    int Number)
{
    public double Scale => Dpi / 96.0;

    /// <summary>Work-area size in effective (DPI independent) pixels, for UI previews.</summary>
    public double WorkWidthEpx => WorkArea.Width / Scale;

    public double WorkHeightEpx => WorkArea.Height / Scale;
}

/// <summary>Enumerates monitors with stable identifiers (monitor device path) and friendly names.</summary>
public static unsafe class DisplayEnumerator
{
    private const int PathInfoSize = 72;  // sizeof(DISPLAYCONFIG_PATH_INFO)
    private const int ModeInfoSize = 64;  // sizeof(DISPLAYCONFIG_MODE_INFO)

    public static List<DisplayInfo> GetDisplays()
    {
        var handles = new List<nint>();
        var gch = GCHandle.Alloc(handles);
        try
        {
            Win32.EnumDisplayMonitors(0, null, &EnumCallback, GCHandle.ToIntPtr(gch));
        }
        finally
        {
            gch.Free();
        }

        var targets = QueryTargets();
        var displays = new List<DisplayInfo>(handles.Count);
        foreach (nint handle in handles)
        {
            var info = Describe(handle, targets);
            if (info is not null)
            {
                displays.Add(info);
            }
        }

        // Stable order: left-to-right, then top-to-bottom (what users see in Settings > Display).
        displays.Sort(static (a, b) => a.Bounds.Left != b.Bounds.Left
            ? a.Bounds.Left.CompareTo(b.Bounds.Left)
            : a.Bounds.Top.CompareTo(b.Bounds.Top));
        return displays;
    }

    public static DisplayInfo? Describe(nint monitor) => Describe(monitor, QueryTargets());

    private static DisplayInfo? Describe(nint handle, Dictionary<string, TargetInfo> targets)
    {
        var mi = new MONITORINFOEXW { cbSize = sizeof(MONITORINFOEXW) };
        if (!Win32.GetMonitorInfo(handle, ref mi))
        {
            return null;
        }

        string gdiName = Win32.ReadFixed(mi.szDevice, 32);
        uint dpi = 96;
        if (Win32.GetDpiForMonitor(handle, Win32.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
        {
            dpi = dpiX;
        }

        targets.TryGetValue(gdiName, out var target);
        string id = !string.IsNullOrEmpty(target.DevicePath)
            ? target.DevicePath
            : GetInterfaceName(gdiName) ?? gdiName;

        return new DisplayInfo(
            handle,
            id.ToLowerInvariant(),
            gdiName,
            target.FriendlyName ?? "",
            mi.rcMonitor.ToRectI(),
            mi.rcWork.ToRectI(),
            (int)dpi,
            (mi.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0,
            target.IsInternal,
            ParseDisplayNumber(gdiName));
    }

    [UnmanagedCallersOnly]
    private static int EnumCallback(nint monitor, nint hdc, RECT* rect, nint data)
    {
        if (GCHandle.FromIntPtr(data).Target is List<nint> list)
        {
            list.Add(monitor);
        }

        return 1;
    }

    private static int ParseDisplayNumber(string gdiName)
    {
        // "\\.\DISPLAY2" → 2
        int i = gdiName.Length;
        while (i > 0 && char.IsAsciiDigit(gdiName[i - 1]))
        {
            i--;
        }

        return int.TryParse(gdiName.AsSpan(i), out int n) ? n : 0;
    }

    private static string? GetInterfaceName(string gdiName)
    {
        var dd = new DISPLAY_DEVICEW { cb = sizeof(DISPLAY_DEVICEW) };
        if (Win32.EnumDisplayDevices(gdiName, 0, ref dd, Win32.EDD_GET_DEVICE_INTERFACE_NAME))
        {
            string id = Win32.ReadFixed(dd.DeviceID, 128);
            return string.IsNullOrEmpty(id) ? null : id;
        }

        return null;
    }

    private record struct TargetInfo(string? FriendlyName, string? DevicePath, bool IsInternal);

    /// <summary>Maps GDI device names (\\.\DISPLAYn) to monitor friendly names and device paths via DisplayConfig.</summary>
    private static Dictionary<string, TargetInfo> QueryTargets()
    {
        var result = new Dictionary<string, TargetInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Win32.GetDisplayConfigBufferSizes(Win32.QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0)
            {
                return result;
            }

            var paths = new byte[Math.Max(1, pathCount) * PathInfoSize];
            var modes = new byte[Math.Max(1, modeCount) * ModeInfoSize];
            fixed (byte* pPaths = paths)
            fixed (byte* pModes = modes)
            {
                if (Win32.QueryDisplayConfig(Win32.QDC_ONLY_ACTIVE_PATHS, ref pathCount, pPaths, ref modeCount, pModes, 0) != 0)
                {
                    return result;
                }

                for (int i = 0; i < pathCount; i++)
                {
                    byte* p = pPaths + (i * PathInfoSize);
                    var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                    source.header.type = Win32.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                    source.header.size = sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME);
                    source.header.adapterId = *(LUID*)p;
                    source.header.id = *(uint*)(p + 8);
                    if (Win32.DisplayConfigGetDeviceInfo(&source.header) != 0)
                    {
                        continue;
                    }

                    var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                    target.header.type = Win32.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                    target.header.size = sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME);
                    target.header.adapterId = *(LUID*)(p + 20);
                    target.header.id = *(uint*)(p + 28);
                    if (Win32.DisplayConfigGetDeviceInfo(&target.header) != 0)
                    {
                        continue;
                    }

                    uint technology = *(uint*)(p + 36);
                    bool isInternal = technology is Win32.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
                        or Win32.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED
                        or Win32.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED;

                    string gdi = Win32.ReadFixed(source.viewGdiDeviceName, 32);
                    string friendly = Win32.ReadFixed(target.monitorFriendlyDeviceName, 64);
                    string path = Win32.ReadFixed(target.monitorDevicePath, 128);
                    result.TryAdd(gdi, new TargetInfo(
                        string.IsNullOrWhiteSpace(friendly) ? null : friendly,
                        string.IsNullOrWhiteSpace(path) ? null : path,
                        isInternal));
                }
            }
        }
        catch
        {
            // DisplayConfig is unavailable in some remote sessions; fall back to GDI names.
        }

        return result;
    }
}
