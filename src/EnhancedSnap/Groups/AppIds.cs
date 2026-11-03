using System.Runtime.InteropServices;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Interop;
using EnhancedSnap.Engine.Native;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Groups;

/// <summary>
/// AppUserModelIDs: how the taskbar decides which button a window belongs to. A window's ID is the one it set on
/// itself (browsers, PWAs, Store app frames), its package's, or one derived from its executable path. Only IDs that
/// a running taskbar button actually shows — read through UI Automation — are ever used, so a wrong guess (apps
/// that set an ID for their whole process can't be read from outside) skips the preview instead of creating a stray
/// taskbar button.
/// </summary>
internal static unsafe class AppIds
{
    private const string ButtonClass = "Taskbar.TaskListButtonAutomationPeer";
    private const string AutomationIdPrefix = "Appid: ";
    private static readonly Guid IID_IPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly Guid CLSID_CUIAutomation = new("FF48DBA4-60EF-4201-AA87-54103EEF594E");
    private static readonly Guid IID_IUIAutomation = new("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE");

    // Known folders the shell abbreviates in path-derived IDs, most specific first.
    private static readonly Guid[] KnownFolderIds =
    [
        new("5CD7AEE2-2219-4A67-B85D-6C9CE15660CB"), // UserProgramFiles (%LOCALAPPDATA%\Programs)
        new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091"), // LocalAppData
        new("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D"), // RoamingAppData
        new("6D809377-6AF0-444B-8957-A3773F02200E"), // ProgramFilesX64
        new("7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E"), // ProgramFilesX86
        new("1AC14E77-02E7-4E5D-B744-2EB1AE5198B7"), // System
        new("D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27"), // SystemX86
        new("F38BF404-1D43-42F2-9305-67DE0B28FC23"), // Windows
    ];

    private static (string Prefix, string Path)[]? _knownFolders;
    private static HashSet<string> _taskbar = new(StringComparer.OrdinalIgnoreCase);
    private static long _taskbarRead;
    private static bool _taskbarLoaded;

    /// <summary>The ID of the taskbar button the window is on (exact taskbar spelling), or null.</summary>
    public static string? Resolve(nint hwnd, HashSet<string> taskbarIds)
    {
        foreach (string candidate in Candidates(hwnd))
        {
            if (taskbarIds.TryGetValue(candidate, out string? actual))
            {
                return actual;
            }
        }

        return null;
    }

    /// <summary>For the log: the IDs tried for a window and whether the taskbar shows one of them.</summary>
    public static string Describe(nint hwnd, HashSet<string> taskbarIds) =>
        $"0x{hwnd:X}: [{string.Join(" | ", Candidates(hwnd))}] -> {Resolve(hwnd, taskbarIds) ?? "no taskbar button"} (taskbar shows {taskbarIds.Count} apps)";

    /// <summary>IDs of the running and pinned taskbar buttons, read at most every two seconds.</summary>
    public static HashSet<string> TaskbarIds()
    {
        if (!_taskbarLoaded || Environment.TickCount64 - _taskbarRead > 2000)
        {
            _taskbar = ReadTaskbarIds();
            _taskbarRead = Environment.TickCount64;
            _taskbarLoaded = true;
        }

        return _taskbar;
    }

    /// <summary>Gives one of our windows an explicit AppUserModelID (before it is shown).</summary>
    public static bool Set(nint hwnd, string id)
    {
        if (SHGetPropertyStoreForWindow(hwnd, in IID_IPropertyStore, out nint store) < 0 || store == 0)
        {
            return false;
        }

        try
        {
            var key = PKEY_AppUserModel_ID;
            var value = new PropVariant { vt = VT_LPWSTR, value = Marshal.StringToCoTaskMemUni(id) };
            // IPropertyStore::SetValue (slot 6), Commit (slot 7)
            int hr = ((delegate* unmanaged<nint, PropertyKey*, PropVariant*, int>)Slot(store, 6))(store, &key, &value);
            PropVariantClear(&value);
            return hr >= 0 && ((delegate* unmanaged<nint, int>)Slot(store, 7))(store) >= 0;
        }
        finally
        {
            Marshal.Release(store);
        }
    }

    // UPDATE: Apps that call SetCurrentProcessExplicitAppUserModelID (VS Code, Electron apps) can't be read from outside;
    // map their executable to an ID through the Start menu's AppsFolder (System.Link.TargetParsingPath → AUMID).
    private static IEnumerable<string> Candidates(nint hwnd)
    {
        if (WindowProperty(hwnd) is { } explicitId)
        {
            yield return explicitId;
        }

        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        nint process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
        {
            yield break;
        }

        string? packaged;
        string? path;
        try
        {
            packaged = PackageId(process);
            path = ImagePath(process);
        }
        finally
        {
            CloseHandle(process);
        }

        if (packaged is not null)
        {
            yield return packaged;
        }

        if (path is null)
        {
            yield break;
        }

        foreach (var (prefix, folder) in KnownFolders())
        {
            if (path.Length > folder.Length && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && path[folder.Length] == '\\')
            {
                yield return prefix + path[folder.Length..];
            }
        }

        yield return path;
    }

    private static string? WindowProperty(nint hwnd)
    {
        if (SHGetPropertyStoreForWindow(hwnd, in IID_IPropertyStore, out nint store) < 0 || store == 0)
        {
            return null;
        }

        try
        {
            var key = PKEY_AppUserModel_ID;
            PropVariant value = default;
            // IPropertyStore::GetValue (slot 5)
            int hr = ((delegate* unmanaged<nint, PropertyKey*, PropVariant*, int>)Slot(store, 5))(store, &key, &value);
            string? id = hr >= 0 && value.vt == VT_LPWSTR && value.value != 0 ? new string((char*)value.value) : null;
            PropVariantClear(&value);
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
        finally
        {
            Marshal.Release(store);
        }
    }

    private static string? PackageId(nint process)
    {
        char* buffer = stackalloc char[256];
        uint length = 256;
        return GetApplicationUserModelId(process, ref length, buffer) == 0 && length > 1 ? new string(buffer, 0, (int)length - 1) : null;
    }

    private static string? ImagePath(nint process)
    {
        char* buffer = stackalloc char[1024];
        uint length = 1024;
        return QueryFullProcessImageName(process, 0, buffer, ref length) ? new string(buffer, 0, (int)length) : null;
    }

    private static (string Prefix, string Path)[] KnownFolders()
    {
        if (_knownFolders is not null)
        {
            return _knownFolders;
        }

        var folders = new List<(string, string)>();
        foreach (var id in KnownFolderIds)
        {
            if (SHGetKnownFolderPath(in id, 0, 0, out char* path) >= 0 && path != null)
            {
                folders.Add(("{" + id.ToString("D").ToUpperInvariant() + "}", new string(path).TrimEnd('\\')));
                CoTaskMemFree(path);
            }
        }

        return _knownFolders = [.. folders];
    }

    /// <summary>
    /// The AutomationId of every taskbar button ("Appid: …"), on the primary and secondary taskbars.
    /// IUIAutomation slots per UIAutomationClient.h: ElementFromHandle 6, CreatePropertyCondition 23;
    /// IUIAutomationElement: FindAll 6, get_CurrentAutomationId 29; IUIAutomationElementArray: get_Length 3, GetElement 4.
    /// </summary>
    private static HashSet<string> ReadTaskbarIds()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        nint automation = 0;
        nint condition = 0;
        try
        {
            int hr = CoCreateInstance(in CLSID_CUIAutomation, 0, CLSCTX_INPROC_SERVER, in IID_IUIAutomation, out automation);
            if (hr < 0 || automation == 0)
            {
                Log.Warn($"UI Automation unavailable (0x{hr:X8}); no taskbar previews");
                return ids;
            }

            var className = new PropVariant { vt = VT_BSTR, value = SysAllocString(ButtonClass) };
            try
            {
                ((delegate* unmanaged<nint, int, PropVariant, nint*, int>)Slot(automation, 23))(automation, UIA_ClassNamePropertyId, className, &condition);
            }
            finally
            {
                SysFreeString(className.value);
            }

            if (condition == 0)
            {
                Log.Warn("UI Automation condition failed; no taskbar previews");
                return ids;
            }

            CollectAll(automation, condition, ids);
            if (ids.Count == 0)
            {
                // Some builds expose the buttons only to an unfiltered search: walk everything, keep "Appid: …".
                ReleaseCom(ref condition);
                ((delegate* unmanaged<nint, nint*, int>)Slot(automation, 21))(automation, &condition); // CreateTrueCondition
                if (condition != 0)
                {
                    CollectAll(automation, condition, ids);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Reading taskbar buttons failed: {ex.Message}");
        }
        finally
        {
            ReleaseCom(ref condition);
            ReleaseCom(ref automation);
        }

        return ids;
    }

    private static void CollectAll(nint automation, nint condition, HashSet<string> ids)
    {
        nint tray = Win32.FindWindow("Shell_TrayWnd", null);
        while (tray != 0)
        {
            CollectButtons(automation, tray, condition, ids);
            tray = Win32.FindWindowEx(0, tray, "Shell_SecondaryTrayWnd", null);
        }
    }

    private static void CollectButtons(nint automation, nint tray, nint condition, HashSet<string> ids)
    {
        nint element = 0;
        nint array = 0;
        try
        {
            int hr = ((delegate* unmanaged<nint, nint, nint*, int>)Slot(automation, 6))(automation, tray, &element);
            if (hr < 0 || element == 0)
            {
                Log.Warn($"Taskbar automation element unavailable (0x{hr:X8})");
                return;
            }

            hr = ((delegate* unmanaged<nint, int, nint, nint*, int>)Slot(element, 6))(element, TreeScope_Descendants, condition, &array);
            if (hr < 0 || array == 0)
            {
                Log.Warn($"Taskbar button search failed (0x{hr:X8})");
                return;
            }

            int length = 0;
            ((delegate* unmanaged<nint, int*, int>)Slot(array, 3))(array, &length);
            for (int i = 0; i < length; i++)
            {
                nint button = 0;
                if (((delegate* unmanaged<nint, int, nint*, int>)Slot(array, 4))(array, i, &button) < 0 || button == 0)
                {
                    continue;
                }

                try
                {
                    nint bstr = 0;
                    if (((delegate* unmanaged<nint, nint*, int>)Slot(button, 29))(button, &bstr) >= 0 && bstr != 0)
                    {
                        string id = Marshal.PtrToStringBSTR(bstr);
                        SysFreeString(bstr);
                        if (id.StartsWith(AutomationIdPrefix, StringComparison.Ordinal))
                        {
                            ids.Add(id[AutomationIdPrefix.Length..]);
                        }
                    }
                }
                finally
                {
                    Marshal.Release(button);
                }
            }
        }
        finally
        {
            ReleaseCom(ref array);
            ReleaseCom(ref element);
        }
    }
}
