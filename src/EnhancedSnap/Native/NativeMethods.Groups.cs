using System.Runtime.InteropServices;
using EnhancedSnap.Core.Interop;

namespace EnhancedSnap.Engine.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct MINMAXINFO
{
    public POINT ptReserved;
    public POINT ptMaxSize;
    public POINT ptMaxPosition;
    public POINT ptMinTrackSize;
    public POINT ptMaxTrackSize;
}

/// <summary>PROPVARIANT / VARIANT for the few string and bool values used here (24 bytes on x64).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort vt;
    public ushort reserved1;
    public ushort reserved2;
    public ushort reserved3;
    public nint value;
    public nint value2;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPOS
{
    public nint hwnd;
    public nint hwndInsertAfter;
    public int x;
    public int y;
    public int cx;
    public int cy;
    public uint flags;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct STARTUPINFOW
{
    public uint cb;
    public char* lpReserved;
    public char* lpDesktop;
    public char* lpTitle;
    public uint dwX;
    public uint dwY;
    public uint dwXSize;
    public uint dwYSize;
    public uint dwXCountChars;
    public uint dwYCountChars;
    public uint dwFillAttribute;
    public uint dwFlags;
    public ushort wShowWindow;
    public ushort cbReserved2;
    public nint lpReserved2;
    public nint hStdInput;
    public nint hStdOutput;
    public nint hStdError;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROCESS_INFORMATION
{
    public nint hProcess;
    public nint hThread;
    public uint dwProcessId;
    public uint dwThreadId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey(Guid fmtid, uint pid)
{
    public Guid FormatId = fmtid;
    public uint PropertyId = pid;
}

/// <summary>Interop for group frames, dividers, taskbar group previews and elevation.</summary>
internal static unsafe partial class NativeMethods
{
    public const uint WM_SIZE = 0x0005;
    public const uint WM_ACTIVATE = 0x0006;
    public const uint WM_GETMINMAXINFO = 0x0024;
    public const uint WM_WINDOWPOSCHANGING = 0x0046;
    public const uint WM_WINDOWPOSCHANGED = 0x0047;
    public const uint WM_SETICON = 0x0080;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const uint WM_DWMSENDICONICTHUMBNAIL = 0x0323;
    public const uint WM_DWMSENDICONICLIVEPREVIEWBITMAP = 0x0326;
    public const uint SC_RESTORE = 0xF120;
    public const uint SC_CLOSE = 0xF060;
    public const int WA_INACTIVE = 0;
    public const int SM_CXMINTRACK = 34;
    public const int SM_CYMINTRACK = 35;
    public const int SM_CXMAXTRACK = 59;
    public const int SM_CYMAXTRACK = 60;
    public const uint GW_HWNDPREV = 3;
    public const int RGN_OR = 2;
    public const int SW_SHOWMINNOACTIVE = 7;
    public const uint WS_SYSMENU = 0x00080000;
    public const uint WS_MINIMIZEBOX = 0x00020000;
    public const uint EVENT_OBJECT_CLOAKED = 0x8017;
    public const uint EVENT_OBJECT_UNCLOAKED = 0x8018;
    public const uint DWMWA_FORCE_ICONIC_REPRESENTATION = 7;
    public const uint DWMWA_HAS_ICONIC_BITMAP = 10;
    public const uint DWMWA_DISALLOW_PEEK = 11;
    public const uint PW_RENDERFULLCONTENT = 0x2;
    public const int HALFTONE = 4;
    public const uint SRCCOPY = 0x00CC0020;
    public const uint LWA_ALPHA = 0x2;
    public const ushort VT_BOOL = 11;
    public const ushort VT_BSTR = 8;
    public const ushort VT_LPWSTR = 31;
    public const uint CLSCTX_INPROC_SERVER = 0x1;
    public const int UIA_ClassNamePropertyId = 30012;
    public const int TreeScope_Descendants = 4;
    public const int APPMODEL_ERROR_NO_APPLICATION = 15703;

    public const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    public const uint TOKEN_DUPLICATE = 0x0002;
    public const uint TOKEN_QUERY = 0x0008;
    public const uint TOKEN_ADJUST_DEFAULT = 0x0080;
    public const uint TOKEN_ADJUST_SESSIONID = 0x0100;
    public const int SecurityImpersonation = 2;
    public const int TokenPrimary = 1;
    public const uint MSGFLT_ALLOW = 1;

    /// <summary>PKEY_AppUserModel_ID and PKEY_AppUserModel_PreventPinning.</summary>
    public static readonly PropertyKey PKEY_AppUserModel_ID = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    public static readonly PropertyKey PKEY_AppUserModel_PreventPinning = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 9);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateRectRgn(int left, int top, int right, int bottom);

    [LibraryImport("gdi32.dll")]
    public static partial int CombineRgn(nint hrgnDst, nint hrgnSrc1, nint hrgnSrc2, int fnCombineMode);

    [LibraryImport("user32.dll")]
    public static partial int SetWindowRgn(nint hWnd, nint hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PrintWindow(nint hwnd, nint hdcBlt, uint nFlags);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool StretchBlt(nint hdcDest, int xDest, int yDest, int wDest, int hDest, nint hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, uint rop);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BitBlt(nint hdcDest, int xDest, int yDest, int width, int height, nint hdcSrc, int xSrc, int ySrc, uint rop);

    [LibraryImport("gdi32.dll")]
    public static partial int SetStretchBltMode(nint hdc, int mode);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetBrushOrgEx(nint hdc, int x, int y, nint lppt);

    [LibraryImport("user32.dll")]
    public static partial nint CopyIcon(nint hIcon);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetIconicThumbnail(nint hwnd, nint hbmp, uint dwSITFlags);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmInvalidateIconicBitmaps(nint hwnd);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetIconicLivePreviewBitmap(nint hwnd, nint hbmp, POINT* pptClient, uint dwSITFlags);

    /// <summary>What Alt + Tab itself uses to switch: allowed from a background process (SetForegroundWindow is not).</summary>
    [LibraryImport("user32.dll")]
    public static partial void SwitchToThisWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fAltTab);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GdiFlush();

    [LibraryImport("shell32.dll")]
    public static partial int SHGetPropertyStoreForWindow(nint hwnd, in Guid riid, out nint ppv);

    [LibraryImport("shell32.dll")]
    public static partial int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, nint hToken, out char* ppszPath);

    [LibraryImport("ole32.dll")]
    public static partial int PropVariantClear(PropVariant* pvar);

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    [LibraryImport("ole32.dll")]
    public static partial void CoTaskMemFree(void* pv);

    [LibraryImport("kernel32.dll")]
    public static partial int GetApplicationUserModelId(nint hProcess, ref uint applicationUserModelIdLength, char* applicationUserModelId);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint hWnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowText(nint hWnd, string lpString);

    [LibraryImport("oleaut32.dll")]
    public static partial nint SysAllocString([MarshalAs(UnmanagedType.LPWStr)] string psz);

    [LibraryImport("oleaut32.dll")]
    public static partial void SysFreeString(nint bstrString);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ChangeWindowMessageFilterEx(nint hwnd, uint message, uint action, nint pChangeFilterStruct);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DuplicateTokenEx(nint existingToken, uint desiredAccess, nint tokenAttributes, int impersonationLevel, int tokenType, out nint newToken);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateProcessWithTokenW(nint token, uint logonFlags, string applicationName, char* commandLine, uint creationFlags, nint environment, string? currentDirectory, STARTUPINFOW* startupInfo, out PROCESS_INFORMATION processInformation);

    /// <summary>Calls slot <paramref name="index"/> of a COM object's vtable.</summary>
    public static nint Slot(nint comObject, int index) => (*(nint**)comObject)[index];

    public static void ReleaseCom(ref nint comObject)
    {
        if (comObject != 0)
        {
            Marshal.Release(comObject);
            comObject = 0;
        }
    }
}
