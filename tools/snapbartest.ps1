<#
.SYNOPSIS
  Snap bar pop down test (suggestion00) against a running engine in TEST MODE started with --verbose
  (ENHANCEDSNAP_TEST_WINDOW_TITLE="EnhancedSnap Test Window"): the sets it offers (classic, horizontal, custom),
  switching by hover (only after the system hover time) and by the wheel, the set kept for the next drag, a drop into a
  custom layout's zone; while a window is dragged towards the top, the pop down peeking, sliding all the way down,
  back up and away (with a drag that starts at the top, or in the strip its edge peeks into, having to leave it first,
  and nothing for a title bar pressed without moving); simulated drag steps run the same code as a real drag; the
  maximize button's flyout
  offering the same sets, opening on the one picked last in either; and everything standing down while Windows Snap
  is (as if) switched back on. Driven by engine debug messages (13 pop down and simulated Windows Snap, 14 pointer and
  simulated drag steps, 15 wheel, 4 flyout, 8 hide) and read back from the engine's log: no synthetic mouse or keyboard
  input, Windows Snap is not touched.
  The engine's data folder must hold the settings written by -WriteSettings (a custom layout "custom-snapbartest",
  2:1 columns with the second split in two, and the custom set [custom-snapbartest, main-stack]); the spacing must be
  the default 8 epx. The test window is closed on every way out. Exit code = failed checks (1 when it could not run).
  Not shipped.
#>
param(
    [string]$OutDir,
    [string]$EngineLog,
    [string]$TestWindowExe,
    [string]$WriteSettings   # only write the settings.json this test expects into this folder, then exit
)

$ErrorActionPreference = 'Stop'
if ($WriteSettings) {
    New-Item -ItemType Directory -Force $WriteSettings | Out-Null
    @'
{
  "gap": 8,
  "margin": 8,
  "customLayouts": [
    { "id": "custom-snapbartest", "name": "Snap bar test", "orientation": "Columns",
      "tracks": [ { "weight": 2, "splits": 1 }, { "weight": 1, "splits": 2 } ] }
  ],
  "snapBarLayouts": { "startSet": "Classic", "showHorizontal": true, "customLayoutIds": [ "custom-snapbartest", "main-stack" ] }
}
'@ | Set-Content -Encoding ASCII (Join-Path $WriteSettings 'settings.json')
    exit 0
}

if (-not $OutDir -or -not $EngineLog) { throw 'give -OutDir and -EngineLog (or -WriteSettings)' }
New-Item -ItemType Directory -Force $OutDir | Out-Null
# Windows PowerShell 5.1 leaves $PSScriptRoot empty in the parameter defaults.
if (-not $TestWindowExe) { $TestWindowExe = "$PSScriptRoot\TestWindow\bin\Debug\net10.0-windows10.0.26100.0\EnhancedSnap.TestWindow.exe" }
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class EsBar {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; public override string ToString() { return "[" + L + "," + T + " " + (R - L) + "x" + (B - T) + "]"; } }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT Monitor; public RECT Work; public uint Flags; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint param, ref uint value, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    public static bool DisplayAt(int x, int y) { var p = new POINT(); p.X = x; p.Y = y; return MonitorFromPoint(p, 0) != IntPtr.Zero; }
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
    public static MONITORINFO Info(IntPtr h) {
        var mi = new MONITORINFO(); mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        GetMonitorInfo(MonitorFromWindow(h, 2), ref mi); return mi;
    }
    public static uint HoverTime() { uint ms = 0; return SystemParametersInfo(0x0066, 0, ref ms, 0) && ms > 0 ? ms : 300; }
}
'@ -ErrorAction SilentlyContinue
[void][EsBar]::SetProcessDpiAwarenessContext([IntPtr](-4))
$engine = [EsBar]::FindWindow('EnhancedSnap.Engine.Host', [NullString]::Value)
if ($engine -eq [IntPtr]::Zero) { throw 'engine is not running' }

function Frame([IntPtr]$h) { $r = New-Object EsBar+RECT; [void][EsBar]::DwmGetWindowAttribute($h, 9, [ref]$r, 16); $r }
function Debug([int]$command, [int]$argument, [IntPtr]$h, [int]$waitMs = 450) { [void][EsBar]::PostMessage($engine, 0x81F0 + $command, [IntPtr]$argument, $h); Start-Sleep -Milliseconds $waitMs }
function Shot([string]$name) { & "$PSScriptRoot\probe.ps1" -Action capture -Capture (Join-Path $OutDir $name) | Out-Null }
# Engine log lines written since the last call (the log is appended to, never rewritten while the engine runs).
$script:seen = @(Get-Content $EngineLog -Encoding UTF8 -ErrorAction SilentlyContinue).Count
function NewLines {
    $all = @(Get-Content $EngineLog -Encoding UTF8 -ErrorAction SilentlyContinue)
    $new = if ($all.Count -gt $script:seen) { $all[$script:seen..($all.Count - 1)] } else { @() }
    $script:seen = $all.Count
    $new
}
function Expect([string]$label, [bool]$ok, [string]$detail) {
    if (-not $ok) { $script:failures++ }
    '  {0} {1}{2}' -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $label, $(if ($detail) { ": $detail" } else { '' })
}
function Matching($lines, [string]$pattern) { , @($lines | Where-Object { $_ -match $pattern }) }   # always an array, even of one
# One step of a simulated drag (debug 14 + 1000): the pointer at the top centre, $epx below the top. Returns the logged state.
function Step([int]$epx, [int]$waitMs = 350) {
    Debug 14 (1000 + $epx) $window $waitMs
    $l = Matching (NewLines) 'Test: drag step '
    if ($l.Count) { $l[-1] -replace '^.*Test: drag step ', '' } else { 'no log line' }
}
# A step that should change the state; under a display above the pointer has to stay 300 ms first, so one more try then.
function StepTo([int]$epx, [string]$want) {
    $s = Step $epx 450
    if ($s -notmatch $want) { $s = Step $epx 450 }
    $s
}

$script:failures = 0
$proc = $null
try {
    $file = Join-Path $OutDir 'hwnd-bar.txt'; Remove-Item $file -ErrorAction SilentlyContinue
    $proc = Start-Process $TestWindowExe -ArgumentList '"EnhancedSnap Test Window"', 600, 400, 900, 600, "`"$file`"", 200, 150, 120 -PassThru
    for ($t = 0; $t -lt 200 -and -not (Test-Path $file); $t++) { Start-Sleep -Milliseconds 100 }
    if (-not (Test-Path $file)) { throw 'the test window did not start within 20 s' }
    $window = [IntPtr][long](Get-Content $file)
    Start-Sleep -Milliseconds 400
    $scale = [EsBar]::GetDpiForWindow($window) / 96.0
    $info = [EsBar]::Info($window)
    $work = $info.Work
    $screen = $info.Monitor
    $hover = [int][EsBar]::HoverTime()
    # Another display above the top centre: the band there is 12 epx deep and the pointer has to rest 300 ms first.
    $displayAbove = [EsBar]::DisplayAt([int](($screen.L + $screen.R) / 2), $screen.T - 2)
    "scale $scale, work area $($work.R - $work.L)x$($work.B - $work.T), hover time $hover ms, display above: $displayAbove"
    [void](NewLines)

    "--- the pop down opens on the classic set (default snap), with the horizontal and custom sets as tabs"
    Debug 13 0 $window 700
    $lines = NewLines
    $shown = Matching $lines 'Test: snap bar Classic \[(.+)\] of Classic/Horizontal/Custom'
    Expect 'classic set first, three tabs' ($shown.Count -eq 1) $(if ($shown) { $shown[0] -replace '^.*Test: ', '' } else { ($lines -join ' | ') })
    Shot 'b1-classic.png'

    "--- hover: a pointer passing over a tab doesn't switch; one resting there for the hover time does"
    $early = [math]::Min(120, [int]($hover / 3))
    Debug 14 1 $window $early
    $passing = Matching (NewLines) 'Snap bar set by hover'
    Expect "no switch $early ms after reaching the Horizontal tab" ($passing.Count -eq 0) ($passing -join ' | ')
    Start-Sleep -Milliseconds ($hover + 250)
    $rested = Matching (NewLines) 'Snap bar set by hover: Horizontal \[rows, top-two-thirds, top-main, three-rows\]'
    Expect 'the horizontal set after resting on its tab' ($rested.Count -eq 1)
    Shot 'b2-horizontal.png'

    "--- the wheel: down for the next set, round and round; up for the previous one"
    Debug 15 1 $window 350
    $custom = Matching (NewLines) 'Snap bar set by wheel: Custom \[custom-snapbartest, main-stack\]'
    Expect 'wheel down: the custom set, in the user''s order' ($custom.Count -eq 1)
    Shot 'b3-custom.png'
    Debug 15 1 $window 350
    Expect 'wheel down again: back round to classic' ((Matching (NewLines) 'Snap bar set by wheel: Classic').Count -eq 1)
    Debug 15 0 $window 350
    Expect 'wheel up: custom again' ((Matching (NewLines) 'Snap bar set by wheel: Custom').Count -eq 1)

    "--- the next drag opens on the set picked last"
    Debug 8 0 $window 300
    Debug 13 3 $window 700
    Expect 'opens on the custom set' ((Matching (NewLines) 'Test: snap bar Custom \[').Count -eq 1)

    "--- drop on the first zone of the custom layout"
    Debug 14 100 $window 300
    Expect 'pointer over custom-snapbartest zone 0' ((Matching (NewLines) 'over custom-snapbartest zone 0').Count -eq 1)
    Debug 13 9 $window 900
    $dropped = Matching (NewLines) 'Test: dropped on the snap bar into custom-snapbartest zone 0'
    $frame = Frame $window
    # ZoneMath: the usable area is the work area less 8 epx all round; a zone's right edge is round(f * (width + gap)) - gap.
    $spacing = [int][math]::Round(8 * $scale)
    $innerW = ($work.R - $work.L) - 2 * $spacing
    $innerH = ($work.B - $work.T) - 2 * $spacing
    $wantW = [int][math]::Round((2.0 / 3.0) * ($innerW + $spacing)) - $spacing
    $ok = $dropped.Count -eq 1 -and [math]::Abs($frame.L - ($work.L + $spacing)) -le 2 -and [math]::Abs($frame.T - ($work.T + $spacing)) -le 2 -and
        [math]::Abs(($frame.R - $frame.L) - $wantW) -le 3 -and [math]::Abs(($frame.B - $frame.T) - $innerH) -le 3
    Expect "window in the custom layout's first zone (two thirds, full height: want ${wantW}x$innerH)" $ok "frame $frame"
    Shot 'b4-dropped.png'
    Debug 8 0 $window 300

    "--- dragged towards the top: it peeks, slides all the way down at the top, back up when the pointer leaves, away beyond reach"
    [void](NewLines)
    $s = StepTo 150 'snap bar peeking'
    Expect 'peeks with the pointer 150 epx below the top' ($s -match '^150 epx below the top, snap bar peeking') $s
    Shot 'b5-peek.png'
    $s = StepTo 0 'snap bar expanded'
    Expect 'all the way down with the pointer at the top' ($s -match '^0 epx below the top, snap bar expanded.*has the pointer') $s
    Shot 'b6-expanded.png'
    $away = 230
    if ($s -match 'panel \[-?\d+,(-?\d+) \d+x(\d+)\]') {
        # Past the panel and the 56 epx it keeps the pointer in.
        $away = [int][math]::Ceiling(([int]$Matches[1] + [int]$Matches[2] - $screen.T) / $scale) + 56 + 8
    }
    $s = Step $away 450
    if ($away -le 224) {
        Expect "back up to peeking with the pointer $away epx down, past the panel" ($s -match 'snap bar peeking') $s
    } else {
        Expect "away with the pointer $away epx down, past the panel and beyond reach" ($s -match 'snap bar hidden') $s
    }
    $s = Step 300 450
    Expect 'away with the pointer 300 epx down' ($s -match 'snap bar hidden') $s

    "--- no flicker on the line: it peeks within 200 epx and goes only beyond 224 epx"
    $s = Step 210
    Expect 'still away at 210 epx' ($s -match 'snap bar hidden') $s
    $s = StepTo 190 'snap bar peeking'
    Expect 'peeks at 190 epx' ($s -match 'snap bar peeking') $s
    $s = Step 215
    Expect 'still peeking at 215 epx' ($s -match 'snap bar peeking') $s
    $s = Step 230 450
    Expect 'away at 230 epx' ($s -match 'snap bar hidden') $s

    "--- its peeking edge (11 epx) takes the pointer too, not only the top 4 epx"
    $s = StepTo 100 'snap bar peeking'
    Expect 'peeks at 100 epx' ($s -match 'snap bar peeking') $s
    $s = StepTo 8 'snap bar expanded'
    Expect 'all the way down with the pointer 8 epx down, on its edge' ($s -match '^8 epx below the top, snap bar expanded') $s
    Debug 13 8 $window 450

    "--- a drag that starts at the top (a maximized window taken by its title bar) has to leave the top first"
    [void](NewLines)
    $s = StepTo 0 'snap bar peeking'
    Expect 'starting at the top: it only peeks' ($s -match '^0 epx below the top, snap bar peeking') $s
    $s = Step 0
    Expect 'still only peeks while the pointer stays there' ($s -match 'snap bar peeking') $s
    $s = Step 60
    Expect 'peeks with the pointer 60 epx down' ($s -match 'snap bar peeking') $s
    $s = StepTo 0 'snap bar expanded'
    Expect 'back at the top: all the way down' ($s -match '^0 epx below the top, snap bar expanded') $s
    Debug 13 8 $window 450

    "--- the same for a drag that starts in the strip its edge peeks into (8 epx down: below the 4 epx band of the screen's own top edge)"
    if ($displayAbove) {
        '  SKIP with a display above, the band at the top centre is 12 epx deep and covers the 11 epx strip'
    } else {
        [void](NewLines)
        $s = StepTo 8 'snap bar peeking'
        Expect 'starting 8 epx down: it only peeks' ($s -match '^8 epx below the top, snap bar peeking') $s
        $s = Step 8
        Expect 'still only peeks while the pointer stays on its edge' ($s -match 'snap bar peeking') $s
        $s = Step 60
        Expect 'peeks with the pointer 60 epx down' ($s -match 'snap bar peeking') $s
        $s = StepTo 8 'snap bar expanded'
        Expect 'back on its edge: all the way down' ($s -match '^8 epx below the top, snap bar expanded') $s
        Debug 13 8 $window 450
    }

    "--- a title bar pressed near the top is no drag yet: nothing shows until the pointer moves"
    [void](NewLines)
    Debug 14 2040 $window 450
    $pressed = Matching (NewLines) 'Test: drag step '
    $s = if ($pressed.Count) { $pressed[-1] -replace '^.*Test: drag step ', '' } else { 'no log line' }
    Expect 'pressed 40 epx down, not moved: no pop down' ($s -match '^40 epx below the top, snap bar hidden') $s
    # Again after 450 ms, still not moved: under a display above the peek waits 300 ms, so only this one would show it.
    $s = Step 40 450
    Expect 'still not moved, past the 300 ms a display above makes the peek wait: no pop down' ($s -match '^40 epx below the top, snap bar hidden') $s
    $s = StepTo 60 'snap bar peeking'
    Expect 'moved to 60 epx down: it peeks' ($s -match '^60 epx below the top, snap bar peeking') $s
    Debug 13 8 $window 450

    "--- the maximize button's flyout (Win + Z) offers the same sets, opening on the one picked last in either"
    Debug 13 1 $window 700
    Debug 8 0 $window 300
    [void](NewLines)
    Debug 4 0 $window 700
    $fly = Matching (NewLines) 'Test: flyout Horizontal \[rows, top-two-thirds, top-main, three-rows\] of Classic/Horizontal/Custom'
    Expect 'the flyout on the horizontal set, with the same three tabs' ($fly.Count -eq 1) ($fly -join ' | ')
    Shot 'b7-flyout.png'
    Debug 14 0 $window ($hover + 300)
    Expect 'resting on the flyout''s first tab shows the default set there' ((Matching (NewLines) 'Flyout set by hover: Classic \[').Count -eq 1)
    Shot 'b8-flyout-default.png'
    Debug 8 0 $window 300
    Debug 13 3 $window 700
    Expect 'the next pop down opens on the set picked in the flyout' ((Matching (NewLines) 'Test: snap bar Classic \[').Count -eq 1)
    Debug 8 0 $window 300

    "--- Windows Snap switched back on (simulated in test mode, the real setting is not touched): Enhanced Snap stands down"
    Debug 13 1 $window 700
    [void](NewLines)
    Debug 13 6 $window 400
    $standDown = Matching (NewLines) 'Test: Windows Snap on \(simulated\), takeover standing down, mouse hook needed: False'
    Expect 'the takeover stands down and the mouse hook is no longer needed' ($standDown.Count -eq 1) ($standDown -join ' | ')
    $s = Step 0 450
    Expect 'dragging to the top shows no pop down of ours' ($s -match '^0 epx below the top, snap bar hidden') $s
    Debug 4 0 $window 500
    Expect 'Win + Z shows no flyout of ours' ((Matching (NewLines) 'Test: flyout not shown').Count -eq 1)
    Debug 13 7 $window 400
    Expect 'switched off again: the takeover is back' ((Matching (NewLines) 'Test: Windows Snap off \(simulated\), takeover active').Count -eq 1)
    $s = StepTo 100 'snap bar peeking'
    Expect 'and the pop down peeks again' ($s -match '^100 epx below the top, snap bar peeking') $s
    Debug 8 0 $window 300
} catch {
    $script:failures++
    "FAIL $($_.Exception.Message)"
} finally {
    if ($proc) { try { if (-not $proc.HasExited) { $proc.Kill() } } catch { } }
}
"snap bar checks failed: $script:failures"
exit $script:failures
