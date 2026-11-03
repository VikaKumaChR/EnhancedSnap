# Changelog

## 1.0.0 — 2026-11-02

First public release: **Enhanced Snap replaces Windows Snap** on Windows 11 — the same gestures, flyout, Snap Assist
and snap groups, with rounded corners, even spacing and a frame behind each group.

- **Everything Windows Snap does**: drag a window to a side, a corner or the top; the snap bar; the snap layouts flyout
  on the maximize button and Win + Z, with Windows' layouts for the screen and snap suggestions; Snap Assist; snap
  groups in the taskbar previews, Alt + Tab and Task View (listed first, peek in place, X closes the whole group);
  dividers between grouped windows; Win + arrow keys; apps running as administrator (installer version).
- **What it adds**: windows keep their rounded corners and Fluent spacing; groups sit on a Mica, Mica Alt, Acrylic or
  solid frame; drag any window over a group's empty space to fill it; dividers stop at each app's minimum size; a
  full-size preview of the hovered zone; per-monitor layouts (11 templates and your own); Shift for the zones overlay
  and Ctrl to span zones; light on energy and memory when idle.
- **Safe takeover**: Windows Snap is switched off only while Enhanced Snap runs, and given back when it pauses, quits,
  crashes (on the next start) or is uninstalled.
- **Product icon**: a window with a dark-blue title bar and a cyan window snapped in front of it, drawn to the Windows
  11 app-icon guidelines (48-px grid, two layers, 120° gradients, masked shadows, unplated) in the cyan-blue of
  Windows' own utilities, and fitted to the pixel grid at every size from 16 to 256 px: taskbar, Start, title bar,
  tray (grey while paused), installer and Store.
- **Microsoft Store and GitHub**: an MSIX package for the Store — it starts at sign-in through its startup task and
  never writes the Windows Snap switch to your profile, so removing it can't leave Snap off — next to the per-user
  MSI installer and the portable ZIP on GitHub.
- Installing 1.0 removes any development preview (0.1–0.5); your settings are kept.

## Development previews

Built and tested before the public release. They carried internal version numbers 1.0.0–2.3.0, listed here as
0.1–0.5 so that numbering starts with the first public release. Labels such as (Prob11) name the private reference
screenshots a change was checked against.

### 0.5 (internal 2.3.0) — 2026-11-02

Snap groups that act as one in the taskbar, Alt + Tab and Task View, and a flyout with Windows' snap suggestions.

- **X on a group closes its windows** (V_Prob03, V_Good_Example03): closing "Group | …" from the taskbar preview,
  Alt + Tab or Task View asks every window of the group to close, as Windows does; apps can still ask to save, and
  whatever stays open stays grouped. Before, X only ungrouped the windows.
- **Peek shows the group in place, without the flash** (V_Prob03, V_Good_Example03): resting the pointer on a group's
  taskbar thumbnail shows its windows where they are, over the desktop, like Windows' own groups — rounded and
  shadowed, faded in and out by Windows. It used to show an empty, pale screen. The full-resolution picture is captured in
  the background as the taskbar preview opens, so it is ready when the peek starts; if it isn't yet, the group's
  snapshot fills in for a moment, because Windows shows a blank placeholder window for a peek answered late.
- **The group base and dividers hide during any peek**: peeking at another window (or the desktop) no longer leaves
  the group's frame behind as a pale panel.
- **Groups come first in Alt + Tab and Task View** (Prob13, V_Good_Example03): pressing Alt + Tab or Win + Tab in a
  group window lists "Group | …" first, as Windows lists its groups. Alt + Tab still selects the window you used
  before, so a quick Alt + Tab goes where it always did; Esc (or picking the group) brings the group back.
- **Snap suggestions in the layouts flyout** (Prob11): like Windows, the first row offers the window together with your
  most recently used apps — side by side, or one beside two stacked — with each app's icon in its zone; one click
  snaps them all as a group.
- **The flyout offers Windows' layouts for the screen**: four on screens under 1920 effective pixels wide (halves,
  2/3 + 1/3, one beside two stacked, quarters) and six on wider ones, in Windows' card proportions. The new
  **Half and two stacked** template (50 % beside two quarters) is also in the layout gallery.

### 0.4 (internal 2.2.0) — 2026-11-01

Snap groups you can fill and find, and a flyout that looks like Windows' own.

- **Fill a group's empty space** (V_Good_Example02): drag a window over the empty part of a snap group — a zone left
  free, or one whose window was closed — and a neutral preview shows where it goes; drop it and it joins the group in
  that space, sized to it like the other members.
- **Empty space follows the dividers**: moving a divider no longer leaves a strip beside an empty zone that no window
  can fill (or lets a window grow over it); filling the space, Snap Assist and snapping into it use all of it.
- **The snap layouts flyout looks like Windows' own**, in light and dark (Prob11): the layouts sit straight on the
  acrylic panel without cards, zones use Windows' gray fill and outline (ControlAltFillColorQuarternary and
  ControlStrongStrokeColorDefault), the panel uses AcrylicBackgroundFillColorBase, two layouts per row (three on
  ultrawide displays), and the hovered zone turns accent.
- **Snap groups in Alt + Tab and Task View** (Prob13): every group appears as "Group | …" with a picture of its
  arrangement over the wallpaper, next to its windows' own entries; picking it brings the whole group back, as
  clicking it in the taskbar does. The picture is rendered in the background when the group forms or changes and
  handed over instantly, because Task View shows a black card if a thumbnail comes late; a slow or hung app can no
  longer stall Enhanced Snap while it is drawn.
- **Group thumbnails show the wallpaper** between the windows, like Windows' snap-group thumbnails.
- **Settings pages line up** (Prob12): every page keeps its content left-aligned at up to 1000 px, like Windows
  Settings, instead of centring it on wide windows (Appearance and Keyboard were misaligned with the other pages). The
  Keyboard page's shortcut list also shows filling a gap and picking a group in Alt + Tab.

### 0.3 (internal 2.1.0) — 2026-11-01

Closer to Windows Snap where it matters, and lighter.

- **The group base behaves like part of the desktop**: it is click-through and never activated, so clicking a gap or
  margin reaches what is underneath — no more base covering the screen or "Ungroup windows" menu (Prob07). It also
  hides whenever a member is minimized, maximized or moved to another virtual desktop.
- **Dividers appear on hover, like Windows Snap**: nothing shows until the pointer is over a gap; then an acrylic bar
  with a grip appears (V_Good_Example01). While you drag, the other windows become wallpaper placeholders and the
  window in front resizes live; everything is placed on release.
- **No more overflowing windows** (V_Prob01, V_Prob02): a divider stops where any adjoining window reaches its app's
  own minimum size (read with WM_GETMINMAXINFO) or a quarter of the display's shorter side, so limits follow the
  display's aspect ratio. Joining a group also makes room for an app that can't shrink to its zone.
- **Frame materials**: Mica (default, tinted once with the wallpaper), Mica Alt, Acrylic or a solid color.
- **Snap groups in the taskbar**: hover an app on the taskbar and its groups appear next to its window thumbnails as
  "Group | …" with a picture of the arrangement; click to bring the whole group back. The tray-icon groups flyout is
  gone; the tray icon shows its normal tooltip again.
- **Snap apps running as administrator** (Prob08): one UAC prompt registers a scheduled task that starts Enhanced
  Snap elevated; the settings app stays unelevated. Turning it off restarts the engine without administrator rights.
- **Memory** (Prob10): popup text is drawn with Windows' software renderer instead of loading the graphics driver
  (12.6 MB instead of 54 MB with a popup open); the settings window hands its memory back to Windows and switches to
  efficiency mode a few seconds after it goes to the background.

### 0.2 (internal 2.0.0) — 2026-10-29

Enhanced Snap now **replaces Windows Snap** instead of sitting next to it.

- **Takeover with safe restore**: turns off Settings → Multitasking → "Snap windows" while running and restores the
  previous value when Enhanced Snap pauses, quits, crashes (next start) or is uninstalled. If you switch Windows Snap
  back on yourself, Enhanced Snap stands down for the session. Windows already arranged by Windows Snap are adopted
  as rounded, spaced snap groups.
- **Snap layouts flyout**: hover a window's maximize button (or press Win + Z) for an acrylic layouts flyout; hovering
  a zone previews it full-size on the screen.
- **Drag like Windows Snap**: sides → halves, corners → quarters, top edge → rounded fill, top centre → snap bar.
- **Snap Assist**: after snapping, live thumbnails of the other windows fill the remaining zones (mouse or keyboard).
- **Snap groups with an Edge-style frame**: grouped windows sit in one themed, rounded frame (concentric corners,
  follows light/dark). The gaps are dividers — drag one to resize every adjoining window, horizontally or vertically,
  including between small windows. Resizing a window's shared edge resizes its neighbours too. Clicking a group
  window brings the whole group forward.
- **Snap groups from the taskbar**: hover the Enhanced Snap icon to see every group as a live miniature; click to
  restore it.
- **Settings**: new "Windows Snap replacement" section; previews now scale with the window instead of lagging behind
  it while it is resized (Prob05).
- **Still light when idle**: the GPU devices that draw popup text are created on first use and released a minute after
  the last popup closes (measured 6.6 MB at start, 54 MB with a popup open, 21 MB after release); popups close their
  surfaces when they hide, and a lost graphics device (driver update, GPU reset) is recreated on the next popup.

### 0.1 (internal 1.0.0) — 2026-10-29

First preview.

- Drag snapping with a Fluent 2 overlay: acrylic zone tiles, accent drop target that glides between zones,
  8 px radius matching Windows 11 windows; Shift (or always-on) activation; Ctrl to span zones.
- Even, DPI-scaled spacing between windows and around the work area; windows keep rounded corners and their
  visible frame lands on the zone pixel-exactly.
- Win + Arrow zone navigation across monitors, Win + ↑ rounded fill, Win + ↓ back/unsnap.
- Restore original size when a snapped window is dragged out.
- Per-monitor layouts: 10 templates and custom column/row layouts; live re-flow on spacing, layout and display
  changes.
- Energy aware: idle without timers, EcoQoS, GPU-composited animations, solid fallbacks in Energy saver,
  transparency off, high contrast and Remote Desktop.
- WinUI 3 settings app (Mica) in English, 正體中文 and 简体中文; tray icon with pause; excluded apps;
  launch at sign-in.
- Per-user MSI installer (no admin) and portable ZIP, both NativeAOT with the Windows App SDK bundled.
