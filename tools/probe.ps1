<#
.SYNOPSIS
  Developer probe for a running Enhanced Snap engine: preview zones, capture the screen, quit.
  Not shipped. Uses the same window class and IPC messages as the settings app.

.EXAMPLE
  ./tools/probe.ps1 -Action preview -Capture out.png
  ./tools/probe.ps1 -Action reload
  ./tools/probe.ps1 -Action quit
#>
param(
    [ValidateSet('preview', 'reload', 'quit', 'capture', 'capturewindow', 'status')]
    [string]$Action = 'status',
    [string]$Capture,
    [int]$DelayMs = 450,
    [string]$WindowClass = 'WinUIDesktopWin32WindowClass',
    [string]$WindowTitle = 'Enhanced Snap'
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class EsProbe {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
}
'@ -ErrorAction SilentlyContinue

[void][EsProbe]::SetProcessDpiAwarenessContext([IntPtr](-4))   # Per-Monitor V2: capture physical pixels
# [NullString]::Value: PowerShell would otherwise pass "" (a window with an empty title) instead of NULL.
$engine = [EsProbe]::FindWindow('EnhancedSnap.Engine.Host', [NullString]::Value)

function Save-Screen([string]$path, [int]$x = [EsProbe]::GetSystemMetrics(76), [int]$y = [EsProbe]::GetSystemMetrics(77), [int]$w = [EsProbe]::GetSystemMetrics(78), [int]$h = [EsProbe]::GetSystemMetrics(79)) {
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size, [System.Drawing.CopyPixelOperation]::SourceCopy)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output "captured ${w}x${h} -> $path"
}

switch ($Action) {
    'status'  { if ($engine -eq [IntPtr]::Zero) { 'engine: not running' } else { "engine: running (hwnd 0x{0:X})" -f $engine.ToInt64() } }
    'reload'  { [void][EsProbe]::PostMessage($engine, 0x8101, [IntPtr]::Zero, [IntPtr]::Zero); 'reload posted' }
    'quit'    { [void][EsProbe]::PostMessage($engine, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero); 'quit posted' }
    'preview' {
        if ($engine -eq [IntPtr]::Zero) { throw 'engine is not running' }
        [void][EsProbe]::PostMessage($engine, 0x8102, [IntPtr]::Zero, [IntPtr]::Zero)
        'preview posted'
        if ($Capture) { Start-Sleep -Milliseconds $DelayMs; Save-Screen $Capture }
    }
    'capture' { Save-Screen $Capture }
    'capturewindow' {
        $h = [EsProbe]::FindWindow($WindowClass, $WindowTitle)
        if ($h -eq [IntPtr]::Zero) { throw "window '$WindowTitle' not found" }
        [void][EsProbe]::SetForegroundWindow($h); Start-Sleep -Milliseconds $DelayMs
        $r = New-Object EsProbe+RECT; [void][EsProbe]::DwmGetWindowAttribute($h, 9, [ref]$r, 16)
        Save-Screen $Capture $r.L $r.T ($r.R - $r.L) ($r.B - $r.T)
    }
}
