using System.Diagnostics;
using System.Runtime.InteropServices;
using EnhancedSnap.Core;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Core.Interop;
using EnhancedSnap.Core.Localization;
using EnhancedSnap.Core.Settings;
using EnhancedSnap.Engine.Input;
using EnhancedSnap.Engine.Native;
using EnhancedSnap.Engine.Overlay;
using EnhancedSnap.Engine.Platform;
using EnhancedSnap.Engine.Snapping;
using EnhancedSnap.Engine.Tray;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine;

/// <summary>
/// Hidden top-level window + message loop of the engine process. Receives broadcasts (display, work area,
/// theme, power), tray callbacks, IPC from the settings app and input-hook actions, and forwards them.
/// Between user actions the thread sleeps in GetMessage: no timers, no polling.
/// </summary>
internal sealed unsafe class EngineHost : IDisposable
{
    private const nuint DisplayDebounceTimerId = 21;
    private const uint DisplayDebounceMs = 400;
    private const uint SPI_SETCLIENTAREAANIMATION = 0x1043;
    private const uint SPI_SETHIGHCONTRAST = 0x0043;

    /// <summary>WM_APP+0x1F0..0x1FF: test-mode debug commands (see <see cref="SnapEngine.DebugCommand"/>).</summary>
    private const uint DebugCommandBase = WM_APP + 0x1F0;

    private static EngineHost? _instance;

    private readonly SystemState _system = new();
    private AppSettings _settings;
    private nint _hwnd;
    private uint _taskbarCreatedMessage;
    private SnapEngine? _engine;
    private TrayIcon? _tray;
    private InputHook? _input;
    private nint _energySaverNotification;
    private nint _batterySaverNotification;
    private bool _disposed;

    public EngineHost(AppSettings settings)
    {
        _settings = settings;
    }

    public int Run(SettingsPage? openPage, bool welcome)
    {
        _instance = this;
        CreateHostWindow();
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        Elevation.AllowMessages(
            _hwnd,
            TrayIcon.CallbackMessage,
            _taskbarCreatedMessage,
            IpcMessages.EngineSettingsChanged,
            IpcMessages.EnginePreviewLayout,
            IpcMessages.EngineDropElevation,
            WM_SETTINGCHANGE,
            WM_CLOSE);
        WindowsSnapOverride.RecoverAfterCrash();

        _system.Refresh();
        _energySaverNotification = RegisterPowerSettingNotification(_hwnd, SystemState.EnergySaverStatusGuid, DEVICE_NOTIFY_WINDOW_HANDLE);
        _batterySaverNotification = RegisterPowerSettingNotification(_hwnd, SystemState.PowerSavingStatusGuid, DEVICE_NOTIFY_WINDOW_HANDLE);

        var composition = new CompositionHost();
        _engine = new SnapEngine(_hwnd, _settings, _system, composition, OnElevatedWindow);
        _engine.Start();

        _tray = new TrayIcon(_hwnd);
        _tray.SetPaused(!_settings.Enabled);
        _tray.Add();

        _input = new InputHook(_hwnd, _engine);
        ApplyReplacement();
        UpdateInput();

        EfficiencyMode.EnableForProcess();
        Log.Info($"Engine ready (replace Windows Snap={WindowsSnapOverride.IsApplied}, administrator={Elevation.IsElevated}, energy saver={_system.EnergySaverActive}, transparency={_system.TransparencyEnabled}, animations={_system.AnimationsEnabled})");

        if (openPage is { } page)
        {
            LaunchSettings(page, welcome);
        }

        while (true)
        {
            int result = GetMessage(out var msg, 0, 0, 0);
            if (result <= 0)
            {
                break; // WM_QUIT (0) or error (-1)
            }

            TranslateMessage(msg);
            DispatchMessage(msg);
        }

        return 0;
    }

    public static void LaunchSettings(SettingsPage page, bool welcome = false)
    {
        string exe = AppInfo.SettingsExePath;
        if (!File.Exists(exe))
        {
            Log.Warn($"Settings app not found at {exe}");
            return;
        }

        // An elevated engine opens the settings app with normal rights.
        if (Elevation.IsElevated && Elevation.StartUnelevated(exe, $"--page {page.ToString().ToLowerInvariant()}{(welcome ? " --welcome" : "")}"))
        {
            return;
        }

        try
        {
            var start = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                WorkingDirectory = AppInfo.BaseDirectory,
            };
            start.ArgumentList.Add("--page");
            start.ArgumentList.Add(page.ToString().ToLowerInvariant());
            if (welcome)
            {
                start.ArgumentList.Add("--welcome");
            }

            Process.Start(start)?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Could not start the settings app", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        WindowsSnapOverride.Restore(); // first: Windows Snap must come back even if anything below fails
        _input?.Dispose();
        _input = null;
        _engine?.Dispose();
        _engine = null;
        _tray?.Dispose();
        _tray = null;
        if (_energySaverNotification != 0)
        {
            UnregisterPowerSettingNotification(_energySaverNotification);
            _energySaverNotification = 0;
        }

        if (_batterySaverNotification != 0)
        {
            UnregisterPowerSettingNotification(_batterySaverNotification);
            _batterySaverNotification = 0;
        }

        _instance = null;
    }

    private void CreateHostWindow()
    {
        fixed (char* className = AppInfo.EngineWindowClass)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = GetModuleHandle(null),
                lpszClassName = className,
            };
            if (RegisterClassExW(&wc) == 0)
            {
                throw new InvalidOperationException($"RegisterClassEx failed ({Marshal.GetLastPInvokeError()})");
            }
        }

        // Never shown. WS_EX_TOOLWINDOW keeps it out of Alt+Tab even if something calls ShowWindow on it.
        _hwnd = CreateWindowEx(WS_EX_TOOLWINDOW, AppInfo.EngineWindowClass, AppInfo.ProductName, WS_POPUP, 0, 0, 0, 0, 0, 0, GetModuleHandle(null), 0);
        if (_hwnd == 0)
        {
            throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastPInvokeError()})");
        }
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        var self = _instance;
        if (self is not null && self._hwnd == hwnd)
        {
            try
            {
                if (self.Handle(msg, wParam, lParam, out nint result))
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Message 0x{msg:X} failed", ex);
            }
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private bool Handle(uint msg, nuint wParam, nint lParam, out nint result)
    {
        result = 0;
        switch (msg)
        {
            case TrayIcon.CallbackMessage:
                OnTray((uint)(lParam & 0xFFFF), (short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                return true;

            case InputHook.WM_APP_WINARROW:
                _engine?.HandleWinArrow((int)wParam, lParam);
                return true;

            case InputHook.WM_APP_WINZ:
                _engine?.OnWinZ(lParam);
                return true;

            case InputHook.WM_APP_HOVERMAX:
                _engine?.OnMaximizeHover(lParam);
                return true;

            case InputHook.WM_APP_POPUPKEY:
                _engine?.OnPopupKey((int)wParam, lParam != 0);
                return true;

            case InputHook.WM_APP_POINTERDOWN:
                _engine?.OnPointerDown(new PointI((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF)));
                return true;

            case InputHook.WM_APP_SWITCHER:
                _engine?.OnSwitcher(wParam != 0, lParam);
                return true;

            case InputHook.WM_APP_SWITCHEREND:
                _engine?.OnSwitcherEnd();
                return true;

            case IpcMessages.EngineSettingsChanged:
                ReloadSettings();
                return true;

            case IpcMessages.EnginePreviewLayout:
                _engine?.Preview(lParam);
                return true;

            case IpcMessages.EngineDropElevation:
                // Administrator mode turned off: drop the task, hand over to an unelevated engine, quit.
                if (Elevation.IsElevated)
                {
                    Elevation.UnregisterTask();
                    if (Elevation.StartUnelevated(AppInfo.EngineExePath, $"--startup --after {Environment.ProcessId}"))
                    {
                        DestroyWindow(_hwnd);
                    }
                }

                return true;

            case >= DebugCommandBase and <= DebugCommandBase + 15:
                _engine?.DebugCommand(msg - DebugCommandBase, (int)wParam, lParam);
                return true;

            case WM_TIMER:
                if (wParam == DisplayDebounceTimerId)
                {
                    KillTimer(_hwnd, DisplayDebounceTimerId);
                    _engine?.OnDisplayChanged();
                }
                else
                {
                    _engine?.OnTimer(wParam);
                }

                return true;

            case WM_DISPLAYCHANGE:
                ScheduleDisplayRefresh();
                return false;

            case WM_DPICHANGED:
                _tray?.Recreate(); // crisp tray icon at the new scale
                return true;

            case WM_SETTINGCHANGE:
                OnSettingChange((uint)wParam, lParam);
                return false;

            case WM_DWMCOLORIZATIONCOLORCHANGED:
            case WM_THEMECHANGED:
            case WM_SYSCOLORCHANGE:
                RefreshVisualState();
                return false;

            case WM_POWERBROADCAST when wParam == PBT_POWERSETTINGCHANGE && lParam != 0:
                // POWERBROADCAST_SETTING { GUID PowerSetting; DWORD DataLength; UCHAR Data[] }
                var setting = *(Guid*)lParam;
                uint dataLength = *(uint*)(lParam + 16);
                uint value = dataLength >= 4 ? *(uint*)(lParam + 20) : 0;
                if (_system.OnPowerSetting(setting, value))
                {
                    _engine?.OnVisualStateChanged();
                }

                result = 1;
                return true;

            case WM_QUERYENDSESSION:
                result = 1;
                return true;

            case WM_ENDSESSION when wParam != 0:
                Dispose();
                return true;

            case WM_CLOSE:
                DestroyWindow(_hwnd);
                return true;

            case WM_DESTROY:
                PostQuitMessage(0);
                return true;
        }

        if (msg == _taskbarCreatedMessage && msg != 0)
        {
            _tray?.Recreate();
            return true;
        }

        return false;
    }

    private void OnTray(uint trayEvent, int x, int y)
    {
        switch (trayEvent)
        {
            case NIN_SELECT:
            case NIN_KEYSELECT:
                LaunchSettings(SettingsPage.General);
                break;
            case WM_CONTEXTMENU:
                int command = _tray?.ShowMenu(x, y, paused: !_settings.Enabled) ?? 0;
                switch (command)
                {
                    case TrayIcon.CommandSettings:
                        LaunchSettings(SettingsPage.General);
                        break;
                    case TrayIcon.CommandLayouts:
                        LaunchSettings(SettingsPage.Layouts);
                        break;
                    case TrayIcon.CommandPause:
                        TogglePause();
                        break;
                    case TrayIcon.CommandQuit:
                        DestroyWindow(_hwnd);
                        break;
                }

                break;
        }
    }

    private void OnSettingChange(uint action, nint lParam)
    {
        string? area = lParam != 0 ? new string((char*)lParam) : null;
        if (action == SPI_SETWORKAREA)
        {
            ScheduleDisplayRefresh(); // taskbar moved, resized or auto-hide toggled
        }

        if (area is "ImmersiveColorSet" or "WindowsThemeElement" || action is SPI_SETCLIENTAREAANIMATION or SPI_SETHIGHCONTRAST)
        {
            RefreshVisualState();
        }

        if (action == SPI_SETWINARRANGING && WindowsSnapOverride.IsApplied && WindowsSnapOverride.IsWindowsSnapOn())
        {
            // The user switched Windows Snap back on in Settings: respect it and stand down.
            Log.Info("Windows Snap was re-enabled externally; Enhanced Snap stops replacing it for this session");
            WindowsSnapOverride.Restore();
        }
    }

    private void RefreshVisualState()
    {
        if (_system.Refresh())
        {
            Log.Info($"Visual state changed: dark={_system.DarkMode}/{_system.AppDarkMode} transparency={_system.TransparencyEnabled} animations={_system.AnimationsEnabled} hc={_system.HighContrast}");
            _engine?.OnVisualStateChanged();
        }
    }

    private void ScheduleDisplayRefresh() => SetTimer(_hwnd, DisplayDebounceTimerId, DisplayDebounceMs, 0);

    private void ReloadSettings()
    {
        var settings = SettingsStore.Load();
        _settings = settings;
        Strings.Use(settings.Language);
        _engine?.ApplySettings(settings);
        _tray?.SetPaused(!settings.Enabled);
        ApplyReplacement();
        UpdateInput();
        Log.Info($"Settings reloaded (enabled={settings.Enabled}, replace={settings.ReplaceWindowsSnap}, gap={settings.Gap}, margin={settings.Margin})");
    }

    /// <summary>Windows Snap is off while Enhanced Snap is enabled and replacing it; otherwise it is restored.</summary>
    private void ApplyReplacement()
    {
        bool replace = _settings.Enabled && _settings.ReplaceWindowsSnap && WindowRules.TestWindowTitle is null;
        if (replace && !WindowsSnapOverride.IsApplied)
        {
            WindowsSnapOverride.Apply();
            if (WindowsSnapOverride.IsApplied)
            {
                _engine?.AdoptArrangedWindows(); // windows Windows Snap had arranged become rounded, spaced groups
            }
        }
        else if (!replace)
        {
            WindowsSnapOverride.Restore();
        }
    }

    private void TogglePause()
    {
        var settings = SettingsStore.Load();
        settings.Enabled = !settings.Enabled;
        SettingsStore.Save(settings);
        ReloadSettings();

        // Keep an open settings window in sync.
        nint settingsHost = Win32.FindWindowEx(Win32.HWND_MESSAGE, 0, AppInfo.SettingsWindowClass, null);
        if (settingsHost != 0)
        {
            Win32.PostMessage(settingsHost, IpcMessages.SettingsReload, 0, 0);
        }
    }

    private void UpdateInput()
    {
        if (_engine is null || _input is null)
        {
            return;
        }

        var (keyboard, mouse) = _engine.InputNeeds(_settings);
        _input.Configure(keyboard, mouse);
    }

    private void OnElevatedWindow(string processName)
    {
        var s = Strings.Current;
        string body = AppInfo.IsPackaged ? s.Notify_ElevatedBodyPackaged : s.Notify_ElevatedBody; // no administrator mode in the Store package
        _tray?.ShowNotification(s.Notify_ElevatedTitle, s.Format(body, processName));
    }
}
