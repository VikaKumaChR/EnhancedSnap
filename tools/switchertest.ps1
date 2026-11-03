<#
.SYNOPSIS
  Snap suggestions, group peek, Alt + Tab / Task View order and X-closes-the-group test against a running engine in
  TEST MODE (ENHANCEDSNAP_TEST_WINDOW_TITLE="EnhancedSnap Test Window", started with --verbose for the log lines).
  Unlike grouptest.ps1 it injects keys (Alt + Tab, Win + Tab, Esc) because that is what it tests; the switchers only
  ever pick the disposable test windows. The peek is started the way the taskbar starts it (DwmpActivateLivePreview).
  Windows Snap is not touched. Not shipped.
#>
param(
    [Parameter(Mandatory)] [string]$OutDir,
    [string]$TestWindowExe
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 leaves $PSScriptRoot empty in the parameter defaults of a script with [Parameter()].
if (-not $TestWindowExe) { $TestWindowExe = "$PSScriptRoot\TestWindow\bin\Debug\net10.0-windows10.0.26100.0\EnhancedSnap.TestWindow.exe" }
New-Item -ItemType Directory -Force $OutDir | Out-Null
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class EsSwitch {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; public override string ToString() { return "[" + L + "," + T + " " + (R - L) + "x" + (B - T) + "]"; } }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern void SwitchToThisWindow(IntPtr h, bool altTab);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr LoadLibrary(string name);
    [DllImport("kernel32.dll")] public static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int LivePreview(int on, IntPtr peek, IntPtr top, int type, UIntPtr reserved);
    public static int Peek(int on, IntPtr window) {
        var activate = (LivePreview)Marshal.GetDelegateForFunctionPointer(GetProcAddress(LoadLibrary("dwmapi.dll"), (IntPtr)113), typeof(LivePreview));
        return activate(on, window, FindWindow("Shell_TrayWnd", null), 1, UIntPtr.Zero);
    }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] public struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }
    [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
    public static void Key(int vk, bool up) { var i = new INPUT(); i.type = 1; i.ki.wVk = (ushort)vk; i.ki.dwFlags = up ? 2u : 0u; SendInput(1, new[] { i }, 40); }
}
'@ -ErrorAction SilentlyContinue
[void][EsSwitch]::SetProcessDpiAwarenessContext([IntPtr](-4))
$engine = [EsSwitch]::FindWindow('EnhancedSnap.Engine.Host', [NullString]::Value)
if ($engine -eq [IntPtr]::Zero) { throw 'engine is not running' }

function Debug([int]$command, [int]$argument, [IntPtr]$hwnd, [int]$waitMs = 700) { [void][EsSwitch]::PostMessage($engine, 0x81F0 + $command, [IntPtr]$argument, $hwnd); Start-Sleep -Milliseconds $waitMs }
function Frame([IntPtr]$hwnd) { $r = New-Object EsSwitch+RECT; [void][EsSwitch]::DwmGetWindowAttribute($hwnd, 9, [ref]$r, 16); $r }
function Shot([string]$name) { & "$PSScriptRoot\probe.ps1" -Action capture -Capture (Join-Path $OutDir $name) | Out-Null }
function Press([int[]]$keys) { foreach ($k in $keys) { [EsSwitch]::Key($k, $false); [EsSwitch]::Key($k, $true) } }
# A dummy key first: Windows lets the process that sent the last input switch the foreground window.
function Focus([IntPtr]$hwnd) { Press 0xFF; [EsSwitch]::SwitchToThisWindow($hwnd, $true); Start-Sleep -Milliseconds 500 }
function Name([IntPtr]$hwnd) { $i = [Array]::IndexOf($windows, $hwnd); if ($i -ge 0) { 'ABCD'[$i] } else { '0x{0:X}' -f $hwnd.ToInt64() } }

$procs = @(); $windows = @()
foreach ($i in 3, 2, 1, 0) {  # created D, C, B, A: A ends on top, then B and C, the apps snap suggestions offer
    $file = Join-Path $OutDir "hwnd-$i.txt"; Remove-Item $file -ErrorAction SilentlyContinue
    $procs += Start-Process $TestWindowExe -ArgumentList '"EnhancedSnap Test Window"', (700 + 60 * $i), (300 + 50 * $i), 900, 560, "`"$file`"", 200, 150, 150 -PassThru
    for ($t = 0; $t -lt 50 -and -not (Test-Path $file); $t++) { Start-Sleep -Milliseconds 100 }
}
foreach ($i in 0..3) { $windows += [IntPtr][long](Get-Content (Join-Path $OutDir "hwnd-$i.txt")) }
Start-Sleep -Milliseconds 800

try {
    '--- layouts flyout for A: two snap suggestions on top, then halves, 2/3 + 1/3, half + two stacked, quarters'
    Debug 4 0 $windows[0] 1000
    Shot 'flyout.png'
    '--- choose the second suggestion: A | B over C'
    Debug 12 1 $windows[0] 1500
    "A=$(Frame $windows[0])  B=$(Frame $windows[1])  C=$(Frame $windows[2])"
    $proxy = [EsSwitch]::FindWindowEx([IntPtr]::Zero, [IntPtr]::Zero, 'EnhancedSnap.TaskbarGroup', [NullString]::Value)

    '--- peek at the group (the windows in place over the desktop)'
    Start-Sleep -Milliseconds 1500
    [void][EsSwitch]::Peek(1, $proxy); Start-Sleep -Milliseconds 300; Shot 'peek.png'
    [void][EsSwitch]::Peek(0, $proxy); Start-Sleep -Milliseconds 600
    '--- peek at the floating window D: the group base must not stay behind'
    [void][EsSwitch]::Peek(1, $windows[3]); Start-Sleep -Milliseconds 600; Shot 'peek-other.png'
    [void][EsSwitch]::Peek(0, $windows[3]); Start-Sleep -Milliseconds 600

    '--- Win + Tab from A: Task View lists the group first; Esc brings the group back'
    Focus $windows[3]; Focus $windows[0]
    [EsSwitch]::Key(0x5B, $false); Press 0x09; Start-Sleep -Milliseconds 1800
    Shot 'taskview.png'
    [EsSwitch]::Key(0x5B, $true); Start-Sleep -Milliseconds 400
    Press 0x1B; Start-Sleep -Milliseconds 1500
    "foreground: $(Name ([EsSwitch]::GetForegroundWindow())) (expected a group window)"

    '--- Alt + Tab from A: the group first, and a quick switch still lands on D, the window used before A'
    Focus $windows[3]; Focus $windows[0]
    [EsSwitch]::Key(0x12, $false); Press 0x09; Start-Sleep -Milliseconds 900
    Shot 'alttab.png'
    [EsSwitch]::Key(0x12, $true); Start-Sleep -Milliseconds 900
    "foreground: $(Name ([EsSwitch]::GetForegroundWindow())) (expected D)"

    '--- Alt + Tab from A, then Esc: back to the group'
    Focus $windows[0]
    [EsSwitch]::Key(0x12, $false); Press 0x09; Start-Sleep -Milliseconds 700
    Press 0x1B; [EsSwitch]::Key(0x12, $true); Start-Sleep -Milliseconds 1000
    "foreground: $(Name ([EsSwitch]::GetForegroundWindow())) (expected a group window)"

    '--- X on the group (SC_CLOSE to its proxy, as the taskbar, Alt + Tab and Task View send it)'
    [void][EsSwitch]::PostMessage($proxy, 0x0112, [IntPtr]0xF060, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 2000
    "open: A=$([EsSwitch]::IsWindow($windows[0])) B=$([EsSwitch]::IsWindow($windows[1])) C=$([EsSwitch]::IsWindow($windows[2])) D=$([EsSwitch]::IsWindow($windows[3])) (expected only D)"
}
finally {
    foreach ($p in $procs) { Stop-Process -Id $p.Id -ErrorAction SilentlyContinue }
}
