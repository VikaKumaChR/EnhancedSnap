using EnhancedSnap.Core;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Core.Interop;
using EnhancedSnap.Core.Localization;
using EnhancedSnap.Engine.Native;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Tray;

/// <summary>Notification-area icon (NOTIFYICON_VERSION_4) with a themed Win32 context menu.</summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    public const uint CallbackMessage = WM_APP + 1;
    public const int CommandSettings = 1;
    public const int CommandLayouts = 2;
    public const int CommandPause = 3;
    public const int CommandQuit = 4;
    private const uint IconId = 1;

    private readonly nint _hwnd;
    private nint _icon;
    private nint _pausedIcon;
    private bool _paused;
    private bool _added;

    public TrayIcon(nint hwnd)
    {
        _hwnd = hwnd;
        EnableThemedMenus();
        LoadIcons();
    }

    // Snap groups live in the taskbar previews of their apps (TaskbarGroups); the tray icon only shows its tooltip.
    public void Add()
    {
        var data = Create(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _added = Shell_NotifyIcon(NIM_ADD, &data);
        data.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, &data);
        if (!_added)
        {
            Log.Warn("Shell_NotifyIcon(NIM_ADD) failed; will retry when the taskbar is recreated");
        }
    }

    /// <summary>Explorer restarted (TaskbarCreated) or DPI changed: re-create the icon.</summary>
    public void Recreate()
    {
        if (_added)
        {
            var data = Create(0);
            Shell_NotifyIcon(NIM_DELETE, &data);
        }

        DestroyIcons();
        LoadIcons();
        Add();
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        if (_added)
        {
            var data = Create(NIF_ICON | NIF_TIP | NIF_SHOWTIP);
            Shell_NotifyIcon(NIM_MODIFY, &data);
        }
    }

    public void ShowNotification(string title, string text)
    {
        if (!_added)
        {
            return;
        }

        var data = Create(NIF_INFO);
        Win32.WriteFixed(data.szInfoTitle, 64, title);
        Win32.WriteFixed(data.szInfo, 256, text);
        data.dwInfoFlags = NIIF_WARNING | NIIF_RESPECT_QUIET_TIME;
        Shell_NotifyIcon(NIM_MODIFY, &data);
    }

    /// <summary>Shows the context menu at the icon anchor and returns the chosen command (0 = none).</summary>
    public int ShowMenu(int x, int y, bool paused)
    {
        var s = Strings.Current;
        nint menu = CreatePopupMenu();
        AppendMenu(menu, MF_STRING, CommandSettings, s.Tray_Settings);
        AppendMenu(menu, MF_STRING, CommandLayouts, s.Tray_Layouts);
        AppendMenu(menu, MF_SEPARATOR, 0, null);
        AppendMenu(menu, MF_STRING | (paused ? MF_CHECKED : 0), CommandPause, s.Tray_Pause);
        AppendMenu(menu, MF_SEPARATOR, 0, null);
        AppendMenu(menu, MF_STRING, CommandQuit, s.Tray_Quit);
        SetMenuDefaultItem(menu, CommandSettings, 0);

        // Required so the menu closes when the user clicks elsewhere (KB135788).
        Win32.SetForegroundWindow(_hwnd);
        uint align = GetSystemMetrics(SM_MENUDROPALIGNMENT) != 0 ? TPM_RIGHTALIGN : 0;
        int command = TrackPopupMenuEx(menu, align | TPM_BOTTOMALIGN | TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, x, y, _hwnd, 0);
        DestroyMenu(menu);
        Win32.PostMessage(_hwnd, WM_NULL, 0, 0);
        return command;
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = Create(0);
            Shell_NotifyIcon(NIM_DELETE, &data);
            _added = false;
        }

        DestroyIcons();
    }

    private NOTIFYICONDATAW Create(uint flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _paused && _pausedIcon != 0 ? _pausedIcon : _icon,
        };
        var s = Strings.Current;
        Win32.WriteFixed(data.szTip, 128, _paused ? s.Tray_TooltipPaused : s.Tray_Tooltip);
        return data;
    }

    private void LoadIcons()
    {
        uint dpi = GetDpiForWindow(_hwnd);
        int size = GetSystemMetricsForDpi(SM_CXSMICON, dpi == 0 ? 96 : dpi);
        string assets = Path.Combine(AppInfo.BaseDirectory, "Assets");
        _icon = LoadImage(0, Path.Combine(assets, "Tray.ico"), IMAGE_ICON, size, size, LR_LOADFROMFILE);
        _pausedIcon = LoadImage(0, Path.Combine(assets, "TrayPaused.ico"), IMAGE_ICON, size, size, LR_LOADFROMFILE);
        if (_icon == 0)
        {
            _icon = LoadImage(0, Path.Combine(assets, "EnhancedSnap.ico"), IMAGE_ICON, size, size, LR_LOADFROMFILE);
        }

        if (_icon == 0)
        {
            _icon = LoadIcon(0, IDI_APPLICATION);
        }
    }

    private void DestroyIcons()
    {
        if (_icon != 0)
        {
            DestroyIcon(_icon);
            _icon = 0;
        }

        if (_pausedIcon != 0)
        {
            DestroyIcon(_pausedIcon);
            _pausedIcon = 0;
        }
    }

    /// <summary>
    /// Lets Win32 menus follow the dark/light app mode (uxtheme ordinals 135/136, used by Windows' own apps).
    /// </summary>
    private static void EnableThemedMenus()
    {
        // UPDATE: Replace the Win32 menu with a Fluent flyout (acrylic, 8 px radius) once a lightweight host exists.
        try
        {
            nint uxtheme = LoadLibrary("uxtheme.dll");
            if (uxtheme == 0)
            {
                return;
            }

            var setPreferredAppMode = (delegate* unmanaged<int, int>)GetProcAddress(uxtheme, 135);
            var flushMenuThemes = (delegate* unmanaged<void>)GetProcAddress(uxtheme, 136);
            if (setPreferredAppMode != null)
            {
                setPreferredAppMode(1); // AllowDark: follow the "app mode" setting
            }

            if (flushMenuThemes != null)
            {
                flushMenuThemes();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Themed menus unavailable: {ex.Message}");
        }
    }
}
