<#
.SYNOPSIS
  Placement / keyboard-navigation test against a running engine in TEST MODE
  (engine started with ENHANCEDSNAP_TEST_WINDOW_TITLE="EnhancedSnap Test Window").
  Uses engine debug messages instead of synthetic input, so the user's pointer and keyboard are untouched.
  Not shipped.
#>
param(
    [string]$OutDir = $env:TEMP,
    [string]$TestWindowExe = "$PSScriptRoot\TestWindow\bin\Debug\net10.0-windows10.0.26100.0\EnhancedSnap.TestWindow.exe"
)

$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class EsPlace {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; public override string ToString() { return "[" + L + "," + T + " " + (R - L) + "x" + (B - T) + "]"; } }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
}
'@ -ErrorAction SilentlyContinue
[void][EsPlace]::SetProcessDpiAwarenessContext([IntPtr](-4))

$engine = [EsPlace]::FindWindow('EnhancedSnap.Engine.Host', [NullString]::Value)
if ($engine -eq [IntPtr]::Zero) { throw 'engine is not running' }

$proc = Start-Process $TestWindowExe -ArgumentList '"EnhancedSnap Test Window"' -PassThru
$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 50 -and $hwnd -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 100; $hwnd = [EsPlace]::FindWindow([NullString]::Value, 'EnhancedSnap Test Window') }
if ($hwnd -eq [IntPtr]::Zero) { throw 'test window did not appear' }
Start-Sleep -Milliseconds 500

function Frame { $r = New-Object EsPlace+RECT; [void][EsPlace]::DwmGetWindowAttribute($hwnd, 9, [ref]$r, 16); $r }
function Debug([int]$command, [int]$argument, [int]$waitMs = 350) {
    [void][EsPlace]::PostMessage($engine, 0x81F0 + $command, [IntPtr]$argument, $hwnd)
    Start-Sleep -Milliseconds $waitMs
}
function Check([string]$name, [int[]]$expected) {
    $f = Frame
    $ok = ($f.L -eq $expected[0]) -and ($f.T -eq $expected[1]) -and ($f.R -eq $expected[2]) -and ($f.B -eq $expected[3])
    "{0,-34} frame={1,-24} expected=[{2},{3} {4}x{5}]  {6}" -f $name, $f, $expected[0], $expected[1], ($expected[2] - $expected[0]), ($expected[3] - $expected[1]), $(if ($ok) { 'PASS' } else { 'FAIL' })
}

$floating = Frame
"floating frame                     $floating"

Debug 1 0 450   # highlight zone 0 under the window
& "$PSScriptRoot\probe.ps1" -Action capture -Capture (Join-Path $OutDir 'place-highlight.png') | Out-Null
Start-Sleep -Milliseconds 1500

Debug 0 0 ; Check 'snap zone 0 (left)' @(12, 12, 1274, 1516)
& "$PSScriptRoot\probe.ps1" -Action capture -Capture (Join-Path $OutDir 'place-left.png') | Out-Null
Debug 2 0x27 ; Check 'Win+Right -> right zone' @(1286, 12, 2548, 1516)
Debug 2 0x27 ; Check 'Win+Right at edge (single monitor)' @(1286, 12, 2548, 1516)
Debug 2 0x26 ; Check 'Win+Up -> fill (rounded maximize)' @(12, 12, 2548, 1516)
& "$PSScriptRoot\probe.ps1" -Action capture -Capture (Join-Path $OutDir 'place-fill.png') | Out-Null
Debug 2 0x28 ; Check 'Win+Down -> back to previous zone' @(1286, 12, 2548, 1516)
Debug 2 0x25 ; Check 'Win+Left -> left zone' @(12, 12, 1274, 1516)
Debug 2 0x28
$f = Frame
"{0,-34} frame={1,-24} size {2}  {3}" -f 'Win+Down -> unsnap', $f, "$($f.R-$f.L)x$($f.B-$f.T) vs floating $($floating.R-$floating.L)x$($floating.B-$floating.T)", $(if ((($f.R-$f.L) -eq ($floating.R-$floating.L)) -and (($f.B-$f.T) -eq ($floating.B-$floating.T))) { 'PASS' } else { 'FAIL' })

Stop-Process -Id $proc.Id -ErrorAction SilentlyContinue
