using System.Runtime.InteropServices;
using EnhancedSnap.Core;
using EnhancedSnap.Core.Diagnostics;

namespace EnhancedSnap.Settings.Services;

/// <summary>
/// The settings window's taskbar identity when Enhanced Snap is installed (not packaged), so its button and jump list
/// read "Enhanced Snap" (Prob14) and not the executable's name: the process's explicit AppUserModelID (set in Program
/// before any UI) on the window as well, and the relaunch properties a pinned button starts from — the engine with
/// --settings, which opens this window and keeps snapping running — with the product's name and icon. Learn
/// (System.AppUserModel.RelaunchCommand): it and RelaunchDisplayNameResource "must always be set together", and they are
/// used "only if a window has an explicit Application User Model ID". The name is plain text, which Learn allows for a
/// name not from MUI resources: "Enhanced Snap" isn't translated. The Store package needs none of this: its windows
/// carry the package's ID.
/// </summary>
internal static unsafe partial class TaskbarIdentity
{
    private const ushort VT_EMPTY = 0;
    private const ushort VT_LPWSTR = 31;
    private const uint PidRelaunchCommand = 2;
    private const uint PidRelaunchIconResource = 3;
    private const uint PidRelaunchDisplayNameResource = 4;
    private const uint PidId = 5;
    private static readonly Guid IID_IPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly Guid AppUserModelKeys = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"); // PKEY_AppUserModel_*

    public static void Apply(nint hwnd)
    {
        if (AppInfo.IsPackaged || hwnd == 0)
        {
            return;
        }

        string engine = AppInfo.EngineExePath;
        bool ok = Update(hwnd, store =>
            Set(store, PidId, AppInfo.AppUserModelId)
            && Set(store, PidRelaunchCommand, $"\"{engine}\" --settings")
            && Set(store, PidRelaunchDisplayNameResource, AppInfo.ProductName)
            && Set(store, PidRelaunchIconResource, $"{engine},0"));
        if (!ok)
        {
            Log.Warn("The settings window's taskbar identity could not be set");
        }
    }

    /// <summary>
    /// Takes the properties off again as the window closes. Learn (SHGetPropertyStoreForWindow): "A window's properties
    /// must be removed before the window is closed", by setting them to VT_EMPTY, or their resources aren't returned.
    /// </summary>
    public static void Clear(nint hwnd)
    {
        if (AppInfo.IsPackaged || hwnd == 0)
        {
            return;
        }

        Update(hwnd, static store =>
            Empty(store, PidRelaunchCommand) & Empty(store, PidRelaunchDisplayNameResource) & Empty(store, PidRelaunchIconResource) & Empty(store, PidId));
    }

    private static bool Update(nint hwnd, Func<nint, bool> change)
    {
        if (SHGetPropertyStoreForWindow(hwnd, in IID_IPropertyStore, out nint store) < 0 || store == 0)
        {
            return false;
        }

        try
        {
            return change(store); // stored on the window by SetValue itself: no Commit needed (Learn)
        }
        finally
        {
            Marshal.Release(store);
        }
    }

    private static bool Set(nint store, uint pid, string text)
    {
        var key = new PropertyKey { FormatId = AppUserModelKeys, PropertyId = pid };
        var value = new PropVariant { Vt = VT_LPWSTR, Value = Marshal.StringToCoTaskMemUni(text) };
        try
        {
            // IPropertyStore::SetValue (slot 6)
            return ((delegate* unmanaged<nint, PropertyKey*, PropVariant*, int>)Slot(store, 6))(store, &key, &value) >= 0;
        }
        finally
        {
            PropVariantClear(&value);
        }
    }

    private static bool Empty(nint store, uint pid)
    {
        var key = new PropertyKey { FormatId = AppUserModelKeys, PropertyId = pid };
        var value = new PropVariant { Vt = VT_EMPTY };
        return ((delegate* unmanaged<nint, PropertyKey*, PropVariant*, int>)Slot(store, 6))(store, &key, &value) >= 0;
    }

    private static nint Slot(nint comObject, int index) => (*(nint**)comObject)[index];

    [LibraryImport("shell32.dll")]
    private static partial int SHGetPropertyStoreForWindow(nint hwnd, in Guid riid, out nint ppv);

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(PropVariant* pvar);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    /// <summary>PROPVARIANT for a string (24 bytes on x64).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Vt;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public nint Value;
        public nint Value2;
    }
}
