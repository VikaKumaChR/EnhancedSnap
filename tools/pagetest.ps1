<#
.SYNOPSIS
  Settings app layout check (Suggestion02) and desktop-preview captures (Suggestion01_01, Suggestion01_02).
  Opens its own settings window on a scratch data folder, sets it to several widths and measures, per page, the space
  between the Content Layer's edges and its content: left and right must be equal (40 epx, 24 epx when the content
  layer is narrower than 640 epx), and the pane button that floats over the content in a narrow window (NavigationView's
  minimal mode) must not cover the page title. A width with nothing to measure fails; one without a full-width element
  to measure the right side on is a SKIP, which also makes the exit code non-zero. Pages are opened through UI
  Automation (the navigation item's SelectionItemPattern) and checked by their title; the window is sized with
  SetWindowPos without activation. So there is no synthetic mouse or keyboard input, and after it opens the window is
  never brought to the front. The caller's ENHANCEDSNAP_DATA_DIR is left as it was. The captures show the user's own
  wallpaper; they stay in -OutDir. The Layouts page opens on the snap layout menu's sets (suggestion00), which must be
  there; it is captured at the top and, scrolled to the end (UI Automation's ScrollPattern), measured and captured
  again. The window's taskbar identity (Prob14) is read back: its AppUserModelID and relaunch properties (its property
  store) and the name of the taskbar button it is on (UI Automation). A wallpaper change is sent to this window only
  (WM_SETTINGCHANGE with SPI_SETDESKWALLPAPER, through SendMessageTimeout as Windows' broadcast delivers it; nothing is
  broadcast and no setting changes), and its log must show the desktop previews reading it again. Not shipped.
#>
param(
    [Parameter(Mandatory)] [string]$OutDir,
    [string]$Exe,
    [string]$DataDir,
    [int[]]$WidthsEpx = @(560, 820, 1200, 1700)
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 leaves $PSScriptRoot empty in the parameter defaults of a script with [Parameter()].
if (-not $Exe) { $Exe = "$PSScriptRoot\..\artifacts\publish\win-x64\EnhancedSnap.Settings.exe" }
if (-not $DataDir) { $DataDir = Join-Path $OutDir 'data' }
New-Item -ItemType Directory -Force $OutDir, $DataDir | Out-Null
$Exe = (Resolve-Path $Exe).Path

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class EsPage {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT Monitor; public RECT Work; public uint Flags; }
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint m, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }
    [StructLayout(LayoutKind.Sequential)] public struct PROPERTYKEY { public Guid FormatId; public uint PropertyId; }
    [StructLayout(LayoutKind.Sequential)] public struct PROPVARIANT { public ushort Vt; public ushort R1, R2, R3; public IntPtr Value; public IntPtr Value2; }
    [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, out IPropertyStore store);
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT value);
    // A System.AppUserModel.* string of a window (pid 5 = ID, 2 = RelaunchCommand, 3 = RelaunchIconResource, 4 = RelaunchDisplayNameResource).
    public static string AppUserModel(IntPtr hwnd, uint pid) {
        var iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
        IPropertyStore store;
        if (SHGetPropertyStoreForWindow(hwnd, ref iid, out store) != 0 || store == null) return null;
        try {
            var key = new PROPERTYKEY { FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), PropertyId = pid };
            PROPVARIANT value;
            if (store.GetValue(ref key, out value) != 0) return null;
            string text = value.Vt == 31 ? Marshal.PtrToStringUni(value.Value) : null;
            PropVariantClear(ref value);
            return text;
        } finally { Marshal.ReleaseComObject(store); }
    }
    public static RECT Work(IntPtr h) {
        var mi = new MONITORINFO(); mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        GetMonitorInfo(MonitorFromWindow(h, 2), ref mi); return mi.Work;
    }
    public static void Capture(IntPtr h, string path) {
        RECT r; GetWindowRect(h, out r);
        using (var b = new Bitmap(r.R - r.L, r.B - r.T, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(b)) {
            IntPtr dc = g.GetHdc(); PrintWindow(h, dc, 2); g.ReleaseHdc(dc); b.Save(path, ImageFormat.Png);
        }
    }
}
'@
[void][EsPage]::SetProcessDpiAwarenessContext([IntPtr](-4))   # physical pixels, like UI Automation's rectangles

$AE = [Windows.Automation.AutomationElement]
$TreeScope = [Windows.Automation.TreeScope]

function Find-Window([int]$processId, [int]$timeoutSeconds = 45) {
    $cond = New-Object Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $found = $AE::RootElement.FindFirst($TreeScope::Children, $cond)
        if ($found) { return $found }
        Start-Sleep -Milliseconds 200
    }
    throw 'settings window did not appear'
}

function Find-ById($root, [string]$automationId) {
    $root.FindFirst($TreeScope::Descendants, (New-Object Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $automationId)))
}

# The page's ScrollViewer, as wide as the Content Layer: the largest one on screen. The navigation pane has a smaller
# one, and a collapsed one has an empty rectangle.
function Find-PageScroller($window) {
    $scrollers = $window.FindAll($TreeScope::Descendants, (New-Object Windows.Automation.PropertyCondition($AE::ClassNameProperty, 'ScrollViewer')))
    @($scrollers) | Where-Object { $r = $_.Current.BoundingRectangle; -not $r.IsEmpty -and $r.Width -gt 0 -and $r.Height -gt 0 } |
        Sort-Object { $_.Current.BoundingRectangle.Width * $_.Current.BoundingRectangle.Height } -Descending | Select-Object -First 1
}

# Every page starts with a title that shows its navigation item's text.
function Find-PageTitle($window, [string]$title) {
    $scroller = Find-PageScroller $window
    if (-not $scroller) { return $null }
    $scroller.FindFirst($TreeScope::Descendants, (New-Object Windows.Automation.PropertyCondition($AE::NameProperty, $title)))
}

# Opens a page as a screen reader would, with no input and no activation: the navigation item, found by its
# AutomationId (WinUI takes it from x:Name: NavGeneral, NavLayouts...), is selected through its SelectionItemPattern,
# or invoked when it has none. Returns the page's title once the item is selected and the page shows that title, else
# $null.
function Select-Page($window, [string]$page) {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    $item = $null
    while (-not $item -and [DateTime]::UtcNow -lt $deadline) {
        $item = Find-ById $window "Nav$page"
        if (-not $item) { Start-Sleep -Milliseconds 200 }
    }
    if (-not $item) { return $null }
    $title = $item.Current.Name
    if ([string]::IsNullOrEmpty($title)) { return $null }
    $pattern = $null
    $selectable = $item.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)
    if ($selectable) { $pattern.Select() }
    elseif ($item.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $pattern.Invoke() }
    else { return $null }
    while ([DateTime]::UtcNow -lt $deadline) {
        if ((-not $selectable -or $pattern.Current.IsSelected) -and (Find-PageTitle $window $title)) { return $title }
        Start-Sleep -Milliseconds 200
    }
    $null
}

# In NavigationView's minimal mode the pane button floats over the Content Layer's top-left corner, so the page must
# start below it. Returns what is wrong (the title is gone, or the button covers part of it), else $null.
function Test-Title($window, [string]$title) {
    $text = Find-PageTitle $window $title
    if (-not $text -or $text.Current.BoundingRectangle.IsEmpty) { return 'the page title is not on screen' }
    $button = Find-ById $window 'TogglePaneButton'
    if (-not $button -or $button.Current.BoundingRectangle.IsEmpty) { return $null }   # nothing floats over the content
    $b = $button.Current.BoundingRectangle
    $t = $text.Current.BoundingRectangle
    $overlapW = [math]::Min($b.Right, $t.Right) - [math]::Max($b.Left, $t.Left)
    $overlapH = [math]::Min($b.Bottom, $t.Bottom) - [math]::Max($b.Top, $t.Top)
    if ($overlapW -gt 0 -and $overlapH -gt 0) { return "the pane button ($b) covers the page title ($t)" }
    $null
}

# Left and right space between the page's ScrollViewer (the Content Layer's width) and its content.
# $viewport: the content layer as read before scrolling. Once a page is scrolled, UI Automation can report its
# ScrollViewer as the whole scrolled content (far above the window), and WinUI's hosting windows then pass as content.
function Measure-Gutters($window, [double]$scale, $viewport = $null) {
    if ($null -ne $viewport) {
        $sc = $viewport
    } else {
        $scroller = Find-PageScroller $window
        if (-not $scroller) { return $null }
        $sc = $scroller.Current.BoundingRectangle
    }
    $all = @($window.FindAll($TreeScope::Descendants, [Windows.Automation.Condition]::TrueCondition))
    # The page's own content: not the scroll bar, not the navigation button that floats over the content layer's
    # top-left corner when the window is narrow (NavigationView's minimal mode), and not WinUI's hosting windows
    # (InputSiteWindowClass, Microsoft.UI.Content.DesktopChildSiteBridge), which cover the whole window.
    $content = @($all | Where-Object {
        $r = $_.Current.BoundingRectangle
        $c = $_.Current.ClassName
        -not $r.IsEmpty -and $r.Width -gt 0 -and $c -notin 'ScrollViewer', 'ScrollBar', 'Thumb', 'RepeatButton', 'InputSiteWindowClass' -and
        $c -notlike 'Microsoft.UI.Content.*' -and
        $_.Current.AutomationId -notin 'TogglePaneButton', 'NavigationViewBackButton' -and
        $r.Left -ge $sc.Left -and $r.Right -le $sc.Right + 0.5 -and $r.Top -ge $sc.Top -and $r.Top -lt $sc.Bottom
    })
    if ($content.Count -eq 0) { return $null }
    $widest = $content | Sort-Object { $_.Current.BoundingRectangle.Width } -Descending | Select-Object -First 1
    $w = $widest.Current.BoundingRectangle
    $expected = $(if ($sc.Width / $scale -lt 640) { 24 } else { 40 }) * $scale
    $fullWidth = $w.Width -ge 0.8 * ($sc.Width - 2 * $expected)
    # Both sides from the same full-width element; without one, the left edge of the content.
    $left = if ($fullWidth) { $w.Left - $sc.Left } else { ($content | ForEach-Object { $_.Current.BoundingRectangle.Left } | Measure-Object -Minimum).Minimum - $sc.Left }
    [pscustomobject]@{
        LayerEpx = [math]::Round($sc.Width / $scale)
        Expected = [math]::Round($expected, 1)
        Left = [math]::Round($left, 1)
        Right = $(if ($fullWidth) { [math]::Round($sc.Right - $w.Right, 1) } else { $null })
        Widest = "$($widest.Current.ClassName) '$($widest.Current.Name)'"
    }
}

# The window at a width in effective pixels, kept on the work area. NOZORDER | NOACTIVATE: it stays where it is in the
# z-order and never takes the focus.
function Set-WindowWidth([int]$epx) {
    $width = [math]::Min([int]($epx * $scale), $work.R - $work.L - 40)
    [void][EsPage]::SetWindowPos($hwnd, [IntPtr]::Zero, $work.L + 20, $work.T + 20, $width, $height, 0x14)
}

if (Get-Process EnhancedSnap.Settings -ErrorAction SilentlyContinue) { throw 'a settings app is already running; not touching it' }
$dynamicScrollbars = (Get-ItemProperty 'HKCU:\Control Panel\Accessibility' -ErrorAction SilentlyContinue).DynamicScrollbars
if ($dynamicScrollbars -eq 0) { 'note: "Always show scrollbars" is on: the right space then includes the scroll bar' }

# The scratch data folder reaches the settings app through the environment it starts with; this session gets its own
# value back right away (pages are switched through UI Automation, so nothing is launched again).
$previousDataDir = $env:ENHANCEDSNAP_DATA_DIR
$env:ENHANCEDSNAP_DATA_DIR = (Resolve-Path $DataDir).Path
try {
    $proc = Start-Process -FilePath $Exe -ArgumentList '--page', 'general' -PassThru
} finally {
    if ($null -eq $previousDataDir) { Remove-Item Env:ENHANCEDSNAP_DATA_DIR -ErrorAction SilentlyContinue } else { $env:ENHANCEDSNAP_DATA_DIR = $previousDataDir }
}

$failures = 0
$skipped = 0
try {
    $window = Find-Window $proc.Id
    $hwnd = [IntPtr]$window.Current.NativeWindowHandle
    $scale = [EsPage]::GetDpiForWindow($hwnd) / 96.0
    $work = [EsPage]::Work($hwnd)
    $height = [math]::Min([int](860 * $scale), $work.B - $work.T - 40)
    "window pid=$($proc.Id) scale=$scale work=$($work.R - $work.L)x$($work.B - $work.T)"
    Start-Sleep -Seconds 2   # entrance transition, first wallpaper read

    "--- taskbar identity (Prob14): the window's AppUserModelID and relaunch properties, and its taskbar button"
    $engineExe = Join-Path (Split-Path $Exe) 'EnhancedSnap.exe'
    $identity = [ordered]@{
        ID = [EsPage]::AppUserModel($hwnd, 5)
        RelaunchCommand = [EsPage]::AppUserModel($hwnd, 2)
        RelaunchDisplayName = [EsPage]::AppUserModel($hwnd, 4)
        RelaunchIcon = [EsPage]::AppUserModel($hwnd, 3)
    }
    $identityOk = $identity.ID -eq 'EnhancedSnap.EnhancedSnap' -and $identity.RelaunchCommand -eq "`"$engineExe`" --settings" -and
        $identity.RelaunchDisplayName -eq 'Enhanced Snap' -and $identity.RelaunchIcon -eq "$engineExe,0"
    if (-not $identityOk) { $failures++ }
    '  {0} window: {1}' -f $(if ($identityOk) { 'PASS' } else { 'FAIL' }), (($identity.GetEnumerator() | ForEach-Object { "$($_.Key)='$($_.Value)'" }) -join ', ')
    $button = $null
    for ($t = 0; $t -lt 20 -and -not $button; $t++) {
        $taskbar = [EsPage]::FindWindow('Shell_TrayWnd', [NullString]::Value)
        if ($taskbar -ne [IntPtr]::Zero) {
            $button = $AE::FromHandle($taskbar).FindFirst($TreeScope::Descendants, (New-Object Windows.Automation.PropertyCondition($AE::AutomationIdProperty, 'Appid: EnhancedSnap.EnhancedSnap')))
        }
        if (-not $button) { Start-Sleep -Milliseconds 250 }
    }
    $buttonOk = $button -and $button.Current.Name -like 'Enhanced Snap*'
    if (-not $buttonOk) { $failures++ }
    '  {0} taskbar button: {1}' -f $(if ($buttonOk) { 'PASS' } else { 'FAIL' }), $(if ($button) { "'$($button.Current.Name)'" } else { 'no button with Appid: EnhancedSnap.EnhancedSnap' })

    # Sent, as Windows' broadcast delivers it (SendMessageTimeout). It can't be posted: user32 refuses WM_SETTINGCHANGE in
    # PostMessage with ERROR_MESSAGE_SYNC_ONLY (1159), as a message that carries a pointer.
    "--- a new wallpaper while the window is open: WM_SETTINGCHANGE (SPI_SETDESKWALLPAPER) sent to this window only"
    $settingsLog = Join-Path $DataDir 'logs\settings.log'
    $logBefore = @(Get-Content $settingsLog -Encoding UTF8 -ErrorAction SilentlyContinue).Count
    $sendResult = [IntPtr]::Zero
    $sent = [EsPage]::SendMessageTimeout($hwnd, 0x001A, [IntPtr]0x14, [IntPtr]::Zero, 0x0002, 3000, [ref]$sendResult)
    if ($sent -eq [IntPtr]::Zero) { '  note: SendMessageTimeout failed or timed out' }
    Start-Sleep -Milliseconds 1500
    $logAfter = @(Get-Content $settingsLog -Encoding UTF8 -ErrorAction SilentlyContinue)
    $logNew = if ($logAfter.Count -gt $logBefore) { $logAfter[$logBefore..($logAfter.Count - 1)] } else { @() }
    $reread = @($logNew | Where-Object { $_ -match 'Desktop wallpaper changed: desktop previews read it again' })
    if ($reread.Count -ne 1) { $failures++ }
    '  {0} the desktop previews read the wallpaper again{1}' -f $(if ($reread.Count -eq 1) { 'PASS' } else { 'FAIL' }), $(if ($reread.Count -ne 1) { ": $($logNew -join ' | ')" } else { '' })

    foreach ($page in 'General', 'Appearance', 'Layouts', 'Keyboard', 'Apps', 'About') {
        $name = $page.ToLowerInvariant()
        if ($proc.HasExited) { throw "the settings app exited during the test (exit code $($proc.ExitCode))" }
        Set-WindowWidth 1200   # minimal mode hides the navigation items: switch pages in a wide window
        $title = Select-Page $window $page
        if (-not $title) { $failures++; "  FAIL $name did not open (navigation item Nav$page not found, not selected, or its page title never showed)"; continue }
        Start-Sleep -Milliseconds 1000   # entrance transition
        foreach ($epx in $WidthsEpx) {
            if ($proc.HasExited) { throw "the settings app exited during the test (exit code $($proc.ExitCode))" }
            Set-WindowWidth $epx
            Start-Sleep -Milliseconds 700
            $m = Measure-Gutters $window $scale
            if (-not $m) {
                $failures++
                "  FAIL $name @ $epx epx: nothing to measure"
            } else {
                $leftOk = [math]::Abs($m.Left - $m.Expected) -le 1.5
                $status = if ($null -eq $m.Right) { if ($leftOk) { 'SKIP' } else { 'FAIL' } } elseif ($leftOk -and [math]::Abs($m.Right - $m.Left) -le 1.5) { 'PASS' } else { 'FAIL' }
                if ($status -eq 'FAIL') { $failures++ } elseif ($status -eq 'SKIP') { $skipped++ }
                '  {0} {1} @ {2} epx: layer {3} epx, left {4}, right {5}, expected {6}  (widest: {7})' -f $status, $name, $epx, $m.LayerEpx, $m.Left, $(if ($null -eq $m.Right) { '- (no full-width element to measure it on)' } else { $m.Right }), $m.Expected, $m.Widest
            }
            $titleProblem = Test-Title $window $title
            if ($titleProblem) { $failures++; "  FAIL $name @ $epx epx: $titleProblem" }
            if ($name -in 'general', 'appearance') { [EsPage]::Capture($hwnd, (Join-Path $OutDir "$name-$epx.png")) }
            if ($name -eq 'layouts') {
                # The snap layout menu's sets open the page (suggestion00); my layouts end it, with even space too.
                if (-not (Find-ById $window 'AddToSetButton')) { $failures++; "  FAIL layouts @ $epx epx: the snap layout menu section (its Add layout button) is missing" }
                [EsPage]::Capture($hwnd, (Join-Path $OutDir "layouts-top-$epx.png"))
                $scroll = $null
                $scroller = Find-PageScroller $window
                $viewport = if ($scroller) { $scroller.Current.BoundingRectangle } else { $null }   # read before scrolling
                if ($scroller -and $scroller.TryGetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern, [ref]$scroll) -and $scroll.Current.VerticallyScrollable) {
                    $scroll.SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, 100)
                    Start-Sleep -Milliseconds 500
                }
                $end = Measure-Gutters $window $scale $viewport
                if ($end -and $null -ne $end.Right) {
                    $endOk = [math]::Abs($end.Left - $end.Expected) -le 1.5 -and [math]::Abs($end.Right - $end.Left) -le 1.5
                    if (-not $endOk) { $failures++ }
                    '  {0} layouts (end of page) @ {1} epx: left {2}, right {3}, expected {4}  (widest: {5})' -f $(if ($endOk) { 'PASS' } else { 'FAIL' }), $epx, $end.Left, $end.Right, $end.Expected, $end.Widest
                }
                [EsPage]::Capture($hwnd, (Join-Path $OutDir "layouts-$epx.png"))
                if ($scroll) { $scroll.SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, 0); Start-Sleep -Milliseconds 300 }
            }
        }
    }
} catch {
    $failures++
    "FAIL $($_.Exception.Message)"
} finally {
    # Also after Ctrl+C: plain method calls, which don't depend on running a cmdlet while the pipeline stops.
    if (-not $proc.HasExited) {
        [void]$proc.CloseMainWindow()
        if (-not $proc.WaitForExit(5000)) { $proc.Kill(); [void]$proc.WaitForExit(5000) }
    }
    "settings process exited: $($proc.HasExited)"
}
"failures: $failures, skipped: $skipped (both count in the exit code)"
exit ($failures + $skipped)
