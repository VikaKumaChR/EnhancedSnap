<#
.SYNOPSIS
  Snap-group / divider / gap-filling / taskbar-preview / Snap Assist test against a running engine in TEST MODE
  (ENHANCEDSNAP_TEST_WINDOW_TITLE="EnhancedSnap Test Window"). Uses engine debug messages and read-only queries:
  no synthetic mouse or keyboard input, Windows Snap is not touched. Not shipped.
#>
param(
    [Parameter(Mandatory)] [string]$OutDir,
    [string]$TestWindowExe,
    [switch]$ReleaseCheck   # also wait until the popups' graphics devices are released (60 s) and show Snap Assist again
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 leaves $PSScriptRoot empty in the parameter defaults of a script with [Parameter()].
if (-not $TestWindowExe) { $TestWindowExe = "$PSScriptRoot\TestWindow\bin\Debug\net10.0-windows10.0.26100.0\EnhancedSnap.TestWindow.exe" }
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class EsGroup {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; public override string ToString() { return "[" + L + "," + T + " " + (R - L) + "x" + (B - T) + "]"; } }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
    public static string ClassAt(int x, int y) { var p = new POINT { X = x, Y = y }; var s = new StringBuilder(256); GetClassName(WindowFromPoint(p), s, 256); return s.ToString(); }
}
'@ -ErrorAction SilentlyContinue
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
[void][EsGroup]::SetProcessDpiAwarenessContext([IntPtr](-4))
$engine = [EsGroup]::FindWindow('EnhancedSnap.Engine.Host', [NullString]::Value)
if ($engine -eq [IntPtr]::Zero) { throw 'engine is not running' }

$procs = @(); $windows = @()
foreach ($i in 0..3) {  # windows 0–2 refuse to get smaller than 700 x 450; the 4th stays floating for Snap Assist
    $file = Join-Path $OutDir "hwnd-$i.txt"; Remove-Item $file -ErrorAction SilentlyContinue
    $min = if ($i -lt 3) { 700, 450 } else { 200, 150 }
    $procs += Start-Process $TestWindowExe -ArgumentList '"EnhancedSnap Test Window"', (400 + 60 * $i), (300 + 40 * $i), 900, 600, "`"$file`"", $min[0], $min[1], 180 -PassThru
    for ($t = 0; $t -lt 50 -and -not (Test-Path $file); $t++) { Start-Sleep -Milliseconds 100 }
    $windows += [IntPtr][long](Get-Content $file)
}
Start-Sleep -Milliseconds 400

function Frame([IntPtr]$h) { $r = New-Object EsGroup+RECT; [void][EsGroup]::DwmGetWindowAttribute($h, 9, [ref]$r, 16); $r }
function Debug([int]$command, [int]$argument, [IntPtr]$h, [int]$waitMs = 450) { [void][EsGroup]::PostMessage($engine, 0x81F0 + $command, [IntPtr]$argument, $h); Start-Sleep -Milliseconds $waitMs }
function Shot([string]$name) { & "$PSScriptRoot\probe.ps1" -Action capture -Capture (Join-Path $OutDir $name) | Out-Null }
function Visible([string]$class) { $f = [EsGroup]::FindWindow($class, [NullString]::Value); if ($f -ne [IntPtr]::Zero -and [EsGroup]::IsWindowVisible($f)) { Frame $f } else { 'none' } }
function TaskbarButton {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $tray = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'Shell_TrayWnd')))
    ($tray.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.AutomationId -like 'Appid:*TestWindow*' } | Select-Object -First 1).Current.Name
}

"--- group of two (quarters TL + TR)"
Debug 7 0 $windows[0]; Debug 7 1 $windows[1] 800
$a = Frame $windows[0]; $b = Frame $windows[1]
"A=$a  B=$b  base=$(Visible 'EnhancedSnap.GroupFrame')  dividers=$(Visible 'EnhancedSnap.GroupDividers')"
$gapX = [int](($a.R + $b.L) / 2); $probeY = $a.T + 40   # near the top: the floating test windows cover the middle
"hit: margin -> $([EsGroup]::ClassAt($a.L - 4, $a.T - 4))   gap -> $([EsGroup]::ClassAt($gapX, $probeY))   inside A near the gap -> $([EsGroup]::ClassAt($a.R - 3, $probeY))"
Shot 'g1-two.png'

"--- hover bar of the divider"
Debug 3 200000 $windows[0] 500
Shot 'g2-hoverbar.png'
Debug 8 0 $windows[0] 300

"--- divider drag held at 60 % (placeholders), then released"
Debug 3 100600 $windows[0] 600
Shot 'g3-drag.png'
Debug 8 0 $windows[0] 900
"A=$(Frame $windows[0])  B=$(Frame $windows[1])"
Shot 'g4-after.png'

"--- third window joins (BL); dividers clamped by the 700 x 450 minimum"
Debug 7 2 $windows[2] 800
Debug 3 100 $windows[0] 900       # vertical divider to 10 %
Debug 3 1100 $windows[0] 900      # horizontal divider to 10 %
$a = Frame $windows[0]
"A=$a  B=$(Frame $windows[1])  C=$(Frame $windows[2])  -> A is $($a.R - $a.L) x $($a.B - $a.T) (minimum 700 x 450 incl. borders)"
Shot 'g5-three.png'

"--- Snap Assist for the empty zone"
Debug 5 0 $windows[0] 900
Shot 'g6-assist.png'
Debug 8 0 $windows[0] 300

"--- fill the empty zone with the floating window (drop preview, then drop)"
Debug 10 0 $windows[3] 800
Shot 'g7-gap-preview.png'
Debug 10 1 $windows[3] 1000
"D (filled)=$(Frame $windows[3])  base=$(Visible 'EnhancedSnap.GroupFrame')"
Shot 'g8-filled.png'

"--- taskbar preview"
"taskbar button: $(TaskbarButton)"
Debug 6 0 $windows[0] 1200

if ($ReleaseCheck) {
    "--- Snap Assist again after the popup devices were released"
    Start-Sleep -Seconds 62
    Debug 5 0 $windows[0] 900
    Shot 'g9-assist-recreated.png'
    Debug 8 0 $windows[0] 300
}

"--- close one member: group shrinks, base follows"
Stop-Process -Id $procs[2].Id; Start-Sleep -Milliseconds 900
"base after close=$(Visible 'EnhancedSnap.GroupFrame')"
foreach ($p in $procs) { Stop-Process -Id $p.Id -ErrorAction SilentlyContinue }
Start-Sleep -Milliseconds 900
"base after all closed=$(Visible 'EnhancedSnap.GroupFrame')  dividers=$(Visible 'EnhancedSnap.GroupDividers')  taskbar button: $(TaskbarButton)"
