<#
.SYNOPSIS
  Snap-group / divider / gap-filling / taskbar-preview / Snap Assist test against a running engine in TEST MODE
  (ENHANCEDSNAP_TEST_WINDOW_TITLE="EnhancedSnap Test Window"). Uses engine debug messages and read-only queries:
  no synthetic mouse or keyboard input, Windows Snap is not touched. Not shipped.
  The hit probes are placed in effective pixels for the default spacing, so the script measures it (the scale from
  GetDpiForWindow, the gap and the margin) and stops unless gap = margin = 8 epx. The test windows are closed on every
  way out, an error or Ctrl+C included. Exit code = failed hit checks (1 when the test could not run).
#>
param(
    [Parameter(Mandatory)] [string]$OutDir,
    [string]$TestWindowExe,
    [switch]$ReleaseCheck   # also wait until the popups' graphics devices are released (60 s) and show Snap Assist again
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null   # the test windows write their handles here
# Windows PowerShell 5.1 leaves $PSScriptRoot empty in the parameter defaults of a script with [Parameter()].
if (-not $TestWindowExe) { $TestWindowExe = "$PSScriptRoot\TestWindow\bin\Debug\net10.0-windows10.0.26100.0\EnhancedSnap.TestWindow.exe" }
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class EsGroup {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; public override string ToString() { return "[" + L + "," + T + " " + (R - L) + "x" + (B - T) + "]"; } }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT Monitor; public RECT Work; public uint Flags; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
    public static string ClassAt(int x, int y) { var p = new POINT { X = x, Y = y }; var s = new StringBuilder(256); GetClassName(WindowFromPoint(p), s, 256); return s.ToString(); }
    public static RECT Work(IntPtr h) {
        var mi = new MONITORINFO(); mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        GetMonitorInfo(MonitorFromWindow(h, 2), ref mi); return mi.Work;
    }
}
'@ -ErrorAction SilentlyContinue
# Add-Type can't replace a type, so a session that ran an older grouptest keeps that EsGroup, without the newer members.
foreach ($member in 'ShowWindow', 'GetDpiForWindow', 'Work') {
    if (-not [EsGroup].GetMethod($member)) { throw "an older EsGroup type (without $member) is loaded in this session: run grouptest in a new PowerShell" }
}
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
[void][EsGroup]::SetProcessDpiAwarenessContext([IntPtr](-4))
$engine = [EsGroup]::FindWindow('EnhancedSnap.Engine.Host', [NullString]::Value)
if ($engine -eq [IntPtr]::Zero) { throw 'engine is not running' }

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

# Minimize / restore a floating test window that would be in the way of a hit probe.
function Park([IntPtr]$h) { [void][EsGroup]::ShowWindow($h, 7); Start-Sleep -Milliseconds 300 }   # SW_SHOWMINNOACTIVE: focus stays put
function Unpark([IntPtr]$h) { [void][EsGroup]::ShowWindow($h, 4); Start-Sleep -Milliseconds 300 }
# Who gets the pointer at a point: 'divider' (the group's divider input window) or 'window' (a test window).
function ExpectHit([string]$label, [int]$x, [int]$y, [string]$want) {
    $class = [EsGroup]::ClassAt($x, $y)
    $got = if ($class -eq 'EnhancedSnap.GroupDividers') { 'divider' } elseif ($class -like 'WindowsForms10.Window*') { 'window' } else { "other ($class)" }
    $ok = if ($want -eq 'not divider') { $got -ne 'divider' } else { $got -eq $want }
    if (-not $ok) { $script:hitFailures++ }
    '  {0} {1} at {2},{3}: {4}' -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $label, $x, $y, $got
}

$script:hitFailures = 0
$procs = @(); $windows = @()
try {
    foreach ($i in 0..3) {  # windows 0-2 refuse to get smaller than 700 x 450; the 4th stays floating for Snap Assist
        $file = Join-Path $OutDir "hwnd-$i.txt"; Remove-Item $file -ErrorAction SilentlyContinue
        $min = if ($i -lt 3) { 700, 450 } else { 200, 150 }
        $procs += Start-Process $TestWindowExe -ArgumentList '"EnhancedSnap Test Window"', (400 + 60 * $i), (300 + 40 * $i), 900, 600, "`"$file`"", $min[0], $min[1], 180 -PassThru
        for ($t = 0; $t -lt 200 -and -not (Test-Path $file); $t++) { Start-Sleep -Milliseconds 100 }   # a cold .NET start can take seconds
        if (-not (Test-Path $file)) { throw "test window $i did not start within 20 s" }
        $windows += [IntPtr][long](Get-Content $file)
    }
    Start-Sleep -Milliseconds 400

    "--- group of two (quarters TL + TR)"
    Debug 7 0 $windows[0]; Debug 7 1 $windows[1] 800
    $a = Frame $windows[0]; $b = Frame $windows[1]
    "A=$a  B=$b  base=$(Visible 'EnhancedSnap.GroupFrame')  dividers=$(Visible 'EnhancedSnap.GroupDividers')"
    # The probes below are placed in effective pixels for the default spacing, so measure it instead of assuming it:
    # the scale of the test window (Per-Monitor V2, so its monitor's DPI), the gap between A and B, the margin above A.
    $dpiScale = [EsGroup]::GetDpiForWindow($windows[0]) / 96.0
    $work = [EsGroup]::Work($windows[0])
    $spacing = [int][math]::Round(8 * $dpiScale)   # 8 epx, rounded as ZoneMath.Inner rounds it
    $gap = $b.L - $a.R; $margin = $a.T - $work.T
    "scale $dpiScale, gap $gap px, margin $margin px (8 epx = $spacing px)"
    if ($gap -ne $spacing -or $margin -ne $spacing) {
        throw "grouptest needs gap = margin = 8 epx ($spacing px here) but measured gap $gap px and margin $margin px: set Settings > Appearance > Space between windows and Space around the edges to 8, then run it again"
    }
    $gapX = [int](($a.R + $b.L) / 2); $probeY = $a.T + 40   # near the top: the floating test windows cover the middle
    "hit: margin -> $([EsGroup]::ClassAt($a.L - 4, $a.T - 4))   gap -> $([EsGroup]::ClassAt($gapX, $probeY))   inside A near the gap -> $([EsGroup]::ClassAt($a.R - 3, $probeY))"
    # V_Prob04: the ends of the seam belong to the divider (caps), never to one window's corner; window content never does.
    ExpectHit 'gap' $gapX $probeY 'divider'
    ExpectHit 'top end of the seam, in the margin' $gapX ($a.T - 2) 'divider'
    ExpectHit 'above A''s top-right corner' ($a.R - [int](12 * $dpiScale)) ($a.T - 2) 'divider'
    ExpectHit 'above B''s top-left corner' ($b.L + [int](12 * $dpiScale)) ($b.T - 2) 'divider'
    Park $windows[2]; Park $windows[3]   # the floating windows cover the middle of the screen
    ExpectHit 'bottom end of the seam (empty half below)' $gapX ($a.B + 3) 'divider'
    Unpark $windows[2]; Unpark $windows[3]
    ExpectHit 'inside A beside the gap' ($a.R - 3) $probeY 'window'
    ExpectHit 'inside A at its top-right corner' ($a.R - 3) ($a.T + 3) 'window'
    Shot 'g1-two.png'

    "--- hover bar of the divider: only drawn, so the window edge under it stays the window's"
    Debug 3 200000 $windows[0] 500
    ExpectHit 'gap while the bar shows' $gapX $probeY 'divider'
    ExpectHit 'on the bar, over A''s edge' ($a.R - [int](2 * $dpiScale)) $probeY 'window'
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
    # An L-shaped corner next to the empty quarter: covered, but the empty quarter itself stays free.
    Park $windows[3]   # the floating window would cover the corner
    $b = Frame $windows[1]; $c = Frame $windows[2]
    $jointX = [int](($a.R + $b.L) / 2); $jointY = [int](($a.B + $c.T) / 2)
    ExpectHit 'joint of the two gaps' $jointX $jointY 'divider'
    # The corner next to the empty space is C's top-right one: the gap between B and C ends there.
    ExpectHit 'beside C''s top-right corner, under B' ($c.R + [int](5 * $dpiScale)) ($b.B + [int](3 * $dpiScale)) 'divider'
    ExpectHit 'deep in the empty space' ($c.R + [int](120 * $dpiScale)) ($b.B + [int](120 * $dpiScale)) 'not divider'
    Unpark $windows[3]
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

    "--- a window that can't shrink to its zone reaches into the gap: that part of the gap stays the window's"
    # E's minimum width leaves F less room than any member may get, so no divider position fits both: E keeps its size
    # and grows right, over the gap.
    $wide = ($work.R - $work.L) - 2 * $margin - $gap - 100
    foreach ($i in 4, 5) {
        $file = Join-Path $OutDir "hwnd-$i.txt"; Remove-Item $file -ErrorAction SilentlyContinue
        $min = if ($i -eq 4) { $wide, 150 } else { 200, 150 }
        $procs += Start-Process $TestWindowExe -ArgumentList '"EnhancedSnap Test Window"', (160 + 60 * $i), (160 + 40 * $i), 900, 600, "`"$file`"", $min[0], $min[1], 120 -PassThru
        for ($t = 0; $t -lt 200 -and -not (Test-Path $file); $t++) { Start-Sleep -Milliseconds 100 }
        if (-not (Test-Path $file)) { throw "test window $i did not start within 20 s" }
        $windows += [IntPtr][long](Get-Content $file)
    }
    Start-Sleep -Milliseconds 400
    Debug 7 0 $windows[4]; Debug 7 1 $windows[5] 800
    $e = Frame $windows[4]; $f = Frame $windows[5]
    $seamX = $f.L - [int][math]::Floor($gap / 2)   # F sits in its zone; the gap is the $gap px left of it
    "E=$e (minimum $wide px wide)  F=$f  -> E's right edge is $($e.R - ($f.L - $gap)) px past the start of the gap"
    if ($e.R -le $seamX) {
        '  FAIL E did not reach into the gap, so the check could not run'; $script:hitFailures++
    } else {
        ExpectHit 'gap where E reaches into it' $seamX ($e.T + 40) 'window'
        ExpectHit 'top end of the seam, above E' $seamX ($e.T - 2) 'divider'
    }
    Shot 'g10-overflow.png'
    foreach ($p in $procs) { Stop-Process -Id $p.Id -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 600
    "hit checks failed: $script:hitFailures"
} finally {
    # Also after an error, a refusal or Ctrl+C: never leave a topmost test window behind. Plain method calls, so the
    # cleanup doesn't depend on running a cmdlet while Ctrl+C stops the pipeline.
    foreach ($p in $procs) { try { if (-not $p.HasExited) { $p.Kill() } } catch { } }
}
exit $script:hitFailures
