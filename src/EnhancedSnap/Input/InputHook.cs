using System.Runtime.InteropServices;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Interop;
using EnhancedSnap.Engine.Native;
using EnhancedSnap.Engine.Platform;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Input;

/// <summary>Decisions the input thread asks the engine for (must be cheap and thread-safe).</summary>
internal interface IInputPolicy
{
    /// <summary>Win + Arrow for this window belongs to Enhanced Snap.</summary>
    bool ShouldHandleWinArrow(nint hwnd, int vk);

    /// <summary>Win + Z (snap layouts) for this window belongs to Enhanced Snap.</summary>
    bool ShouldHandleWinZ(nint hwnd);

    /// <summary>Watch for the pointer resting on maximize buttons.</summary>
    bool WatchMaximizeHover { get; }

    /// <summary>A flyout or Snap Assist is open: route navigation keys and report outside clicks.</summary>
    bool PopupOpen { get; }

    /// <summary>The snap group proxy standing for this window in Alt + Tab and Task View, or 0.</summary>
    nint SwitcherProxy(nint hwnd);
}

/// <summary>
/// Low-level keyboard and mouse hooks on one dedicated HighQoS thread that does nothing but pump them,
/// so system-wide input never waits on snapping work. Callbacks only classify the event and post to the
/// engine thread. Hooks are installed only for enabled features.
/// <list type="bullet">
/// <item>Keyboard: Win + Arrow, Win + Z, and Esc/Enter/arrows/Tab while an Enhanced Snap popup is open. Alt + Tab and
/// Win + Tab pressed in a snap group window: the first Tab is held back until the engine has put the group first.</item>
/// <item>Mouse: one timer reset per move; after the pointer rests 300 ms, a single WM_NCHITTEST asks the window
/// under it whether that's its maximize button (HTMAXBUTTON) — the same contract Windows' own flyout uses.</item>
/// </list>
/// </summary>
internal sealed unsafe class InputHook : IDisposable
{
    public const uint WM_APP_WINARROW = WM_APP + 2;
    public const uint WM_APP_WINZ = WM_APP + 3;
    public const uint WM_APP_HOVERMAX = WM_APP + 4;
    public const uint WM_APP_POPUPKEY = WM_APP + 5;
    public const uint WM_APP_POINTERDOWN = WM_APP + 6;
    public const uint WM_APP_SWITCHER = WM_APP + 7;    // wParam 1 = Alt + Tab, 0 = Win + Tab; lParam = the group's proxy
    public const uint WM_APP_SWITCHEREND = WM_APP + 8; // Alt / Win released after a switcher we held a Tab for

    /// <summary>dwExtraInfo tag on keys we inject ourselves ("ESNP").</summary>
    public const nuint InjectedMarker = 0x45534E50;

    private const nuint HoverTimerId = 7;
    private const uint HoverDelayMs = 300;
    private static InputHook? _instance;

    private readonly nint _target;
    private readonly IInputPolicy _policy;
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;
    private uint _threadId;
    private nint _keyboardHook;
    private nint _mouseHook;
    private nint _timerWindow;
    private int _swallowedVk;
    private bool _switcherOpen; // Alt + Tab / Win + Tab is up while its modifier is held: later Tabs just cycle
    private bool _switcherOurs; // ...and the engine put a group first
    private POINT _lastPoint;
    private bool _wantKeyboard;
    private bool _wantMouse;

    public InputHook(nint target, IInputPolicy policy)
    {
        _target = target;
        _policy = policy;
    }

    public bool IsRunning => _thread is not null;

    /// <summary>(Re)start with the hooks the current settings need; stops the thread when none are needed.</summary>
    public void Configure(bool keyboard, bool mouse)
    {
        if (_thread is not null && keyboard == _wantKeyboard && mouse == _wantMouse)
        {
            return;
        }

        Stop();
        _wantKeyboard = keyboard;
        _wantMouse = mouse;
        if (!keyboard && !mouse)
        {
            return;
        }

        _instance = this;
        _ready.Reset();
        _thread = new Thread(Run) { IsBackground = true, Name = "Enhanced Snap input hooks" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(2));
    }

    public void Stop()
    {
        if (_thread is null)
        {
            return;
        }

        PostThreadMessage(_threadId, WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _swallowedVk = 0;
    }

    public void Dispose()
    {
        Stop();
        _ready.Dispose();
    }

    private void Run()
    {
        EfficiencyMode.SetCurrentThreadHighQos(true);
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, 0, 0, 0, PM_NOREMOVE);
        nint module = GetModuleHandle(null);
        if (_wantKeyboard)
        {
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, &KeyboardProc, module, 0);
            if (_keyboardHook == 0)
            {
                Log.Error($"Keyboard hook could not be installed (error {Marshal.GetLastPInvokeError()})");
            }
        }

        if (_wantMouse)
        {
            // Timer owner for hover detection (thread timers would need fresh ids on every reset).
            _timerWindow = CreateWindowEx(0, "STATIC", "Enhanced Snap hover", 0, 0, 0, 0, 0, Win32.HWND_MESSAGE, 0, module, 0);
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, &MouseProc, module, 0);
            if (_mouseHook == 0)
            {
                Log.Error($"Mouse hook could not be installed (error {Marshal.GetLastPInvokeError()})");
            }
        }

        Log.Info($"Input hooks installed (keyboard={_keyboardHook != 0}, mouse={_mouseHook != 0})");
        _ready.Set();
        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            if (msg.message == WM_TIMER && msg.hwnd == _timerWindow && msg.wParam == HoverTimerId)
            {
                KillTimer(_timerWindow, HoverTimerId);
                CheckHover();
                continue;
            }

            TranslateMessage(msg);
            DispatchMessage(msg);
        }

        if (_keyboardHook != 0)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }

        if (_mouseHook != 0)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }

        if (_timerWindow != 0)
        {
            DestroyWindow(_timerWindow);
            _timerWindow = 0;
        }

        Log.Info("Input hooks removed");
    }

    // ------------------------------------------------------------------ keyboard

    [UnmanagedCallersOnly]
    private static nint KeyboardProc(int nCode, nuint wParam, nint lParam)
    {
        var self = _instance;
        if (nCode == HC_ACTION && self is not null)
        {
            try
            {
                if (self.ProcessKey((uint)wParam, (KBDLLHOOKSTRUCT*)lParam))
                {
                    return 1; // swallow
                }
            }
            catch (Exception ex)
            {
                Log.Error("Keyboard hook callback failed", ex);
            }
        }

        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    private bool ProcessKey(uint message, KBDLLHOOKSTRUCT* key)
    {
        if ((key->flags & LLKHF_INJECTED) != 0 && key->dwExtraInfo == InjectedMarker)
        {
            return false;
        }

        int vk = (int)key->vkCode;
        bool isDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
        bool win = IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN);

        // Popup navigation (Snap Assist / flyouts) — only plain keys, never shortcuts.
        if (_policy.PopupOpen && !win && vk is VK_ESCAPE or VK_RETURN or VK_SPACE or VK_TAB or VK_LEFT or VK_RIGHT or VK_UP or VK_DOWN)
        {
            if (isDown)
            {
                Win32.PostMessage(_target, WM_APP_POPUPKEY, (nuint)vk, IsKeyDown(VK_SHIFT) ? 1 : 0);
            }

            return true;
        }

        if (vk == VK_TAB)
        {
            return ProcessSwitcherTab(isDown, win);
        }

        if (!isDown && _switcherOpen && vk is VK_MENU or VK_LMENU or VK_RMENU or VK_LWIN or VK_RWIN)
        {
            _switcherOpen = false;
            if (_switcherOurs)
            {
                _switcherOurs = false;
                Win32.PostMessage(_target, WM_APP_SWITCHEREND, 0, 0);
            }
        }

        bool isArrow = vk is VK_LEFT or VK_RIGHT or VK_UP or VK_DOWN;
        bool isZ = vk == VK_Z;
        if (!isArrow && !isZ)
        {
            return false;
        }

        if (!isDown)
        {
            if (_swallowedVk == vk)
            {
                _swallowedVk = 0; // swallow the matching key-up as well
                return true;
            }

            return false;
        }

        if (_swallowedVk == vk)
        {
            return true; // auto-repeat: one action per key press
        }

        if (!win || IsKeyDown(VK_SHIFT) || IsKeyDown(VK_CONTROL) || IsKeyDown(VK_MENU))
        {
            return false; // Win+Shift+Arrow (move to monitor) etc. stay with Windows
        }

        nint foreground = GetForegroundWindow();
        if (isZ)
        {
            if (!_policy.ShouldHandleWinZ(foreground))
            {
                return false;
            }

            _swallowedVk = vk;
            Win32.PostMessage(_target, WM_APP_WINZ, 0, foreground);
            SendDummyKey();
            return true;
        }

        if (!_policy.ShouldHandleWinArrow(foreground, vk))
        {
            return false;
        }

        _swallowedVk = vk;
        Win32.PostMessage(_target, WM_APP_WINARROW, (nuint)vk, foreground);
        SendDummyKey();
        return true;
    }

    /// <summary>
    /// The first Tab of plain Alt + Tab or Win + Tab in a snap group window is held back and handed to the engine, which
    /// activates the group's proxy (Alt + Tab and Task View order windows by activation) and then replays it.
    /// </summary>
    private bool ProcessSwitcherTab(bool isDown, bool win)
    {
        // UPDATE: Task View opened from the taskbar button or a touchpad gesture never passes through here, so the group
        // is listed by when it was last used; the button's position (UI Automation) would let the mouse hook do the same.
        if (!isDown)
        {
            if (_swallowedVk == VK_TAB)
            {
                _swallowedVk = 0;
                return true;
            }

            return false;
        }

        if (_swallowedVk == VK_TAB)
        {
            return true; // auto-repeat of the held-back press
        }

        bool alt = IsKeyDown(VK_MENU);
        if (alt == win || IsKeyDown(VK_CONTROL) || IsKeyDown(VK_SHIFT) || _switcherOpen)
        {
            return false; // other shortcuts, or already cycling through the switcher
        }

        _switcherOpen = true;
        nint proxy = _policy.SwitcherProxy(GetForegroundWindow());
        if (proxy == 0)
        {
            return false;
        }

        _switcherOurs = true;
        _swallowedVk = VK_TAB;
        Win32.PostMessage(_target, WM_APP_SWITCHER, alt ? 1u : 0u, proxy);
        return true;
    }

    /// <summary>
    /// A swallowed Win + key would otherwise look like a lone Win tap and open Start on release.
    /// An unassigned key (0xFF) in between tells the shell a shortcut was used. Sending it also makes this process the
    /// source of the last input, which Windows requires before it may take the foreground.
    /// </summary>
    public static void SendDummyKey()
    {
        INPUT* inputs = stackalloc INPUT[2];
        inputs[0] = new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = 0xFF, dwExtraInfo = InjectedMarker } };
        inputs[1] = new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = 0xFF, dwFlags = KEYEVENTF_KEYUP, dwExtraInfo = InjectedMarker } };
        SendInput(2, inputs, sizeof(INPUT));
    }

    // ------------------------------------------------------------------ mouse

    [UnmanagedCallersOnly]
    private static nint MouseProc(int nCode, nuint wParam, nint lParam)
    {
        var self = _instance;
        if (nCode == HC_ACTION && self is not null)
        {
            try
            {
                self.ProcessMouse((uint)wParam, (MSLLHOOKSTRUCT*)lParam);
            }
            catch (Exception ex)
            {
                Log.Error("Mouse hook callback failed", ex);
            }
        }

        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    private void ProcessMouse(uint message, MSLLHOOKSTRUCT* info)
    {
        switch (message)
        {
            case WM_MOUSEMOVE:
                if (info->pt.X == _lastPoint.X && info->pt.Y == _lastPoint.Y)
                {
                    return;
                }

                _lastPoint = info->pt;
                if (_policy.WatchMaximizeHover && !_policy.PopupOpen)
                {
                    SetTimer(_timerWindow, HoverTimerId, HoverDelayMs, 0); // resets the same timer
                }

                break;
            case WM_LBUTTONDOWN:
            case WM_RBUTTONDOWN:
            case WM_MBUTTONDOWN:
                KillTimer(_timerWindow, HoverTimerId);
                if (_policy.PopupOpen)
                {
                    Win32.PostMessage(_target, WM_APP_POINTERDOWN, 0, Pack(info->pt));
                }

                break;
        }
    }

    private void CheckHover()
    {
        if (!_policy.WatchMaximizeHover || _policy.PopupOpen || IsKeyDown(VK_LBUTTON))
        {
            return;
        }

        POINT point = _lastPoint;
        nint hit = WindowFromPoint(point);
        nint window = hit == 0 ? 0 : GetAncestor(hit, GA_ROOT);
        if (window == 0 || (GetStyle(window) & WS_MAXIMIZEBOX) == 0)
        {
            return;
        }

        Win32.GetWindowThreadProcessId(window, out uint pid);
        if (pid == (uint)Environment.ProcessId)
        {
            return;
        }

        if (HitsMaximizeButton(window, point))
        {
            Win32.PostMessage(_target, WM_APP_HOVERMAX, 0, window);
        }
    }

    /// <summary>Asks the window itself (WM_NCHITTEST) whether a screen point is its maximize button — the same
    /// contract custom-frame apps (Edge, VS Code, WinUI) implement for Windows' own snap flyout.</summary>
    public static bool HitsMaximizeButton(nint window, POINT point)
    {
        // Unaware/system-aware apps expect logical coordinates in WM_NCHITTEST.
        POINT logical = point;
        PhysicalToLogicalPointForPerMonitorDPI(window, ref logical);
        return SendMessageTimeout(window, WM_NCHITTEST, 0, Pack(logical), SMTO_ABORTIFHUNG, 60, out nint result) != 0 && result == HTMAXBUTTON;
    }

    private static nint Pack(POINT p) => (nint)(((p.Y & 0xFFFF) << 16) | (p.X & 0xFFFF));
}
