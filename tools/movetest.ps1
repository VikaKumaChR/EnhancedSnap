<#
.SYNOPSIS
  End-to-end drag test against a running engine, without synthetic mouse input.
  Opens a disposable WinForms window in a separate process, starts the system's *keyboard* move loop on it
  (WM_SYSCOMMAND/SC_MOVE), steers it with posted arrow keys, drops with Enter, then checks that the window's
  visible frame landed exactly on the expected zone. The engine must use Activation = Always for this test.

  Not shipped. The pointer jumps to the test window while the move loop runs.
#>
param(
    [int]$ArrowRight = 0,
    [int]$ArrowLeft = 0,
    [int]$ArrowDown = 0,
    [string]$CaptureDuring,
    [string]$CaptureAfter,
    [switch]$KeepWindow
)

$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class EsMove {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
}
'@ -ErrorAction SilentlyContinue
[void][EsMove]::SetProcessDpiAwarenessContext([IntPtr](-4))

$title = 'EnhancedSnap Test Window'
$formScript = @"
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Application]::EnableVisualStyles()
# The process is started hidden, and Windows applies STARTUPINFO's SW_HIDE to the first ShowWindow call
# of the process. Spend that call on a throwaway form so the real test window becomes visible.
`$d = New-Object System.Windows.Forms.Form; `$d.ShowInTaskbar = `$false; `$d.Opacity = 0; `$d.Show(); `$d.Close()
`$f = New-Object System.Windows.Forms.Form
`$f.Text = '$title'
`$f.StartPosition = 'Manual'
`$f.Location = New-Object System.Drawing.Point(320, 260)
`$f.Size = New-Object System.Drawing.Size(900, 560)
`$f.BackColor = [System.Drawing.Color]::FromArgb(250, 250, 250)
`$f.TopMost = `$true
`$l = New-Object System.Windows.Forms.Label
`$l.Text = 'Enhanced Snap test window - safe to ignore'
`$l.AutoSize = `$true; `$l.Location = New-Object System.Drawing.Point(24, 24)
`$l.Font = New-Object System.Drawing.Font('Segoe UI', 12)
`$f.Controls.Add(`$l)
[System.Windows.Forms.Application]::Run(`$f)
"@
$proc = Start-Process powershell -ArgumentList '-NoProfile', '-Command', $formScript -PassThru -WindowStyle Hidden

$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 60 -and $hwnd -eq [IntPtr]::Zero; $i++) {
    Start-Sleep -Milliseconds 100
    $hwnd = [EsMove]::FindWindow([NullString]::Value, $title)
}
if ($hwnd -eq [IntPtr]::Zero) { throw 'test window did not appear' }
Start-Sleep -Milliseconds 400
[void][EsMove]::SetForegroundWindow($hwnd)

function Frame([IntPtr]$h) { $r = New-Object EsMove+RECT; [void][EsMove]::DwmGetWindowAttribute($h, 9, [ref]$r, 16); $r }
$before = Frame $hwnd
"before: L=$($before.L) T=$($before.T) R=$($before.R) B=$($before.B)"

# Keyboard move loop: WM_SYSCOMMAND + SC_MOVE, then arrow keys (WM_KEYDOWN), Enter to drop.
[void][EsMove]::PostMessage($hwnd, 0x0112, [IntPtr]0xF010, [IntPtr]::Zero)
Start-Sleep -Milliseconds 300
function Key([int]$vk, [int]$count) { for ($k = 0; $k -lt $count; $k++) { [void][EsMove]::PostMessage($hwnd, 0x0100, [IntPtr]$vk, [IntPtr]::Zero); Start-Sleep -Milliseconds 15 } }
Key 0x27 $ArrowRight
Key 0x25 $ArrowLeft
Key 0x28 $ArrowDown
Start-Sleep -Milliseconds 450
if ($CaptureDuring) { & "$PSScriptRoot\probe.ps1" -Action capture -Capture $CaptureDuring }
[void][EsMove]::PostMessage($hwnd, 0x0100, [IntPtr]0x0D, [IntPtr]::Zero)
Start-Sleep -Milliseconds 700

$after = Frame $hwnd
"after:  L=$($after.L) T=$($after.T) R=$($after.R) B=$($after.B)  (w=$($after.R-$after.L) h=$($after.B-$after.T))"
if ($CaptureAfter) { & "$PSScriptRoot\probe.ps1" -Action capture -Capture $CaptureAfter }
if (-not $KeepWindow) { Stop-Process -Id $proc.Id -ErrorAction SilentlyContinue }
else { "test window kept (pid $($proc.Id), hwnd 0x{0:X})" -f $hwnd.ToInt64() }
