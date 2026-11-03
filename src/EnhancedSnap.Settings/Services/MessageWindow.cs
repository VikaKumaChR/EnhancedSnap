using System.Runtime.InteropServices;
using EnhancedSnap.Core;
using EnhancedSnap.Core.Interop;

namespace EnhancedSnap.Settings.Services;

/// <summary>
/// Message-only window that lets the engine and a second launch of the settings app reach this instance:
/// "settings changed on disk" (tray Pause) and "bring yourself forward on page X".
/// </summary>
internal sealed unsafe partial class MessageWindow : IDisposable
{
    private static MessageWindow? _instance;
    private readonly Action<uint, nuint> _handler;
    private nint _hwnd;

    public MessageWindow(Action<uint, nuint> handler)
    {
        _handler = handler;
        _instance = this;
        fixed (char* className = AppInfo.SettingsWindowClass)
        {
            var wc = new WndClassEx
            {
                cbSize = (uint)sizeof(WndClassEx),
                lpfnWndProc = &WndProc,
                hInstance = GetModuleHandleW(null),
                lpszClassName = className,
            };
            RegisterClassExW(&wc);
            _hwnd = CreateWindowExW(0, className, null, 0, 0, 0, 0, 0, Win32.HWND_MESSAGE, 0, wc.hInstance, 0);
        }
    }

    public void Dispose()
    {
        if (_hwnd != 0)
        {
            DestroyWindow(_hwnd);
            _hwnd = 0;
        }

        _instance = null;
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg is IpcMessages.SettingsReload or IpcMessages.SettingsActivate && _instance is { } self)
        {
            try
            {
                self._handler(msg, wParam);
            }
            catch
            {
                // Never let an exception cross the native boundary.
            }

            return 0;
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged<nint, uint, nuint, nint, nint> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public nint hIconSm;
    }

    [LibraryImport("user32.dll")]
    private static partial ushort RegisterClassExW(WndClassEx* lpwcx);

    [LibraryImport("user32.dll")]
    private static partial nint CreateWindowExW(uint exStyle, char* className, char* windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hwnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetModuleHandleW(char* moduleName);
}
