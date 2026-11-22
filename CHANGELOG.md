# Changelog

## Unreleased

- **Your own layouts in the snap layout menu** (suggestion00): the snap bar that opens when you drag a window to the
  top of the screen and the flyout on the maximize button (and Win + Z) now offer the same sets of layouts, named
  below them: **Default** (what Windows offers for the display, led by the display's own zone layout when that is
  another one), **Top and bottom** (halves, two-thirds on top, a main window above
  two, three rows) and **Custom**, up to 12 built-in or custom layouts of your choice in your order. Rest the pointer
  on a set's name for Windows' hover time to switch (in the flyout you can also click it; while you drag, the mouse
  wheel switches too); the set you pick in either opens first in both next time. Set it up in *Settings → Layouts →
  Snap layout menu*, where layouts in lists now show as clear pictograms with rounded corners. Layouts are stored as
  proportions, never pixels, so a set fits every display. Three new built-in layouts come with it: *Two-thirds top*,
  *Main and two below* and *Three rows*. Below a display stacked above another, the snap bar now waits until the
  pointer rests at the top, instead of opening as it passes on its way up.
- **The snap bar peeks, like Windows'**: as you drag a window towards the top of the screen, the snap bar's edge shows
  at the top; it slides all the way down when the pointer reaches the top centre or that edge, back up when the pointer
  leaves it, and away when you drag further down. A drag that starts at the top — a maximized window taken by its
  title bar — has to leave the top first, so the snap bar doesn't open under the pointer before the window has moved.
  Its set names are touch-sized and as wide as the name in every language.
- **A clearer Layouts page**: it starts with the **snap layout menu** (all displays: dragging to the top, the maximize
  button, Win + Z), then **display zones** (the display you pick: Shift + drag and Win + arrow keys; its templates are
  now *zone templates*) and **my layouts**, each with a line on what it is for.
- **Windows Snap switched back on in Windows Settings**: while Enhanced Snap runs, its snap bar, edges, flyout and Snap
  Assist now stand down as well, so the two never show at once.
- **No more wait when Windows Snap comes back**: pausing Enhanced Snap, turning off *Replace Windows Snap*,
  uninstalling it, signing out, shutting down and switching Windows Snap back on in Windows Settings held Enhanced Snap
  for seconds (3 to 13 when pausing, about 5 at uninstall and shutdown), and kept Windows waiting at shutdown. Giving
  Windows Snap back announces the change to every window, Enhanced Snap's own included, which took it for you switching
  Windows Snap on and gave it back again, up to 31 times over. It now gives it back once, and when Windows ends the
  session or the installer closes Enhanced Snap, it exits straight away.
- **"Enhanced Snap" on the taskbar** (Prob14): right-clicking the settings window's taskbar button showed
  *EnhancedSnap.Settings*; it now shows *Enhanced Snap*. The window carries the Start menu shortcut's ID, so its button
  groups with the shortcut, and a pinned button starts Enhanced Snap and opens Settings.
- **A README in Simplified Chinese** (README.zh-CN.md), in the words of Windows' own Simplified Chinese interface.
- **Dividers are easier to grab** (V_Prob04): the ends of a gap between grouped windows now belong to the divider —
  at the top and bottom of the screen and next to empty space — so the pointer no longer lands on one window's corner
  and resizes that window alone. At the top of the screen you can push the pointer against the edge, above the seam,
  whatever the margin. A divider takes only the gap and those ends, less any part a window reaches into: windows' own
  edges and content are never covered, even where an app is larger than its space, and the cursor always shows what a
  press there will do. The bar that
  appears over the seam is only drawn, so where it lies over a window's edge, that edge stays the window's. One
  deliberate exception: a gap narrower than 6 effective pixels still gets a strip that wide, so the divider stays easy
  to grab, and that strip reaches up to 3 effective pixels over the windows' edges beside it.
- **Dividers move only as far as you drag them**: pressing beside the seam, on one of its ends say, no longer makes
  the divider jump to the pointer, and a click that doesn't move past Windows' drag threshold leaves it where it is.
- **Your wallpaper in the previews** (Suggestion01_01, Suggestion01_02): the desktop pictures on the General and
  Appearance pages show your own wallpaper, fitted the way Windows fits it (fill, fit, stretch, center, tile or span;
  a tiled picture at its own size), in a rounded display frame shaped like your monitor, as in Settings →
  Personalization. The windows and taskbar in them are Mica, so they take on its colors like real ones; like Windows'
  own Mica they turn solid with transparency effects off, in Energy saver, in high contrast and in Remote Desktop. The
  taskbar follows Windows' light or dark mode, as the real one does. The previews follow the wallpaper as soon as
  Windows announces a new one, and again whenever you come back to the window.
- **Even space on both sides of every settings page** (Suggestion02): the content now keeps the same distance from the
  left and right edges at any window size, like the WinUI 3 Gallery, instead of leaving a wide gap on the right of
  large windows. In a narrow window, where the navigation button floats over the page, the page starts below it.
- **Screen readers and voice access can press *Open settings folder* and *Open logs*** in Settings → About.

## 1.0.1 — 2026-11-05

Fixes from testing the Microsoft Store package on a real install.

- **Launch at sign-in follows Windows**: when you come back to Enhanced Snap after changing it in Windows Settings or
  Task Manager, the switch and its message update right away, without leaving the page.
- **Store package: the icon in Windows' lists**: lists such as *Settings → Apps → Startup* draw the icon on a plate in
  your accent colour. The icon now takes two thirds of the plate, like other packaged apps, instead of filling it edge
  to edge with the plate showing only in its open corner. The taskbar and Start are unchanged.

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
