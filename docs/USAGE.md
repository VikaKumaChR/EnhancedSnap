# Using Enhanced Snap

[README](../README.md) · [正體中文](USAGE.zh-TW.md)

Snapping puts the windows you have open side by side, so you can see and use several at once — a document next to a
browser, say. Enhanced Snap keeps the way Windows does it and adds rounded corners and spacing.

## Install

| Where | Package | Notes |
| --- | --- | --- |
| **Microsoft Store** | Enhanced Snap | Installs and updates through the Store; starts at sign-in (listed in *Settings → Apps → Startup*). Doesn't offer *Snap apps running as administrator*. |
| [**GitHub Releases**](https://github.com/VikaKumaChR/EnhancedSnap/releases/latest) | `EnhancedSnap-<version>-x64.msi` | Per-user install (no admin prompt) into `%LOCALAPPDATA%\Programs\Enhanced Snap`, Start menu shortcut, launches at sign-in, clean uninstall from *Settings → Apps*. Replaces any earlier preview build. |
| | `EnhancedSnap-<version>-x64-portable.zip` | Unzip anywhere and run `EnhancedSnap.exe`. Enable *Launch at sign-in* in Settings if you want it. |

Requirements: **Windows 11** (build 22000 or later), x64. Nothing else — the .NET code is compiled to native
code and the Windows App SDK is bundled.

> The GitHub downloads are not code-signed yet, so SmartScreen may ask for confirmation on first launch
> (*More info → Run anyway*). Verify them with `SHA256SUMS.txt`. The Store version is signed by Microsoft.

## The basics

**Snap windows side by side.** Drag a window to the top of the screen and drop it on a layout in the snap bar. Snap
Assist then shows your other open windows: click one for each space left.

**Resize them together.** Point at the gap between two windows until a bar appears, then drag the bar. Every window
along that gap resizes with it.

**Fill an empty space.** Closed a window in a group? Drag any other window over the empty space: a preview shows where
it will go, and dropping it there adds it to the group.

**Bring a group back.** Windows you snapped together stay a group. It is listed as **Group | …** when you point at the
app's taskbar button and in <kbd>Alt</kbd> + <kbd>Tab</kbd>; pick it to bring all of its windows back at once.

## Shortcuts

| Shortcut | Action |
| --- | --- |
| Drag to a side / corner / top | Half / quarter / rounded fill; top centre opens the snap bar |
| Hover the maximize button, <kbd>Win</kbd> + <kbd>Z</kbd> | Snap layouts flyout |
| Hover a gap inside a group, then drag the bar | Resize all adjoining windows together (each stops at its app's minimum size and at a quarter of the display's shorter side) |
| Drag a window over a group's empty space | Join the group in that space |
| <kbd>Alt</kbd> + <kbd>Tab</kbd>, <kbd>Win</kbd> + <kbd>Tab</kbd> | Pick **Group \| …** to bring a whole snap group back (listed first while you're in the group) |
| X on **Group \| …** (taskbar preview, Alt + Tab, Task View) | Close every window of the group |
| <kbd>Shift</kbd> + drag | Show zones and drop the window into one (or *always* show zones; then Shift moves freely) |
| <kbd>Ctrl</kbd> while dragging | Span from the first zone to the current one |
| <kbd>Win</kbd> + <kbd>←</kbd> / <kbd>→</kbd> | Zone on the left / right (continues onto the next monitor) |
| <kbd>Win</kbd> + <kbd>↑</kbd> | Zone above, or fill the screen with rounded corners; again = maximize |
| <kbd>Win</kbd> + <kbd>↓</kbd> | Zone below, back from fill, or unsnap to the original size |

Click the tray icon to open **Settings**; right-click it for *Layouts*, *Pause snapping* and *Quit*.
Running `EnhancedSnap.exe` again also opens Settings.

## Settings and logs

- Settings: `%LOCALAPPDATA%\EnhancedSnap\settings.json` (human-readable; changes apply live)
- Logs: `%LOCALAPPDATA%\EnhancedSnap\logs\` (lifecycle and errors only)
- Microsoft Store version: Windows keeps both in the app's own folder,
  `%LOCALAPPDATA%\Packages\<package>\LocalCache\Local\EnhancedSnap`, and removes them with the app.
  *Settings → About → Open settings folder* opens the right one either way.

Nothing leaves your PC: Enhanced Snap has no telemetry and makes no network connections ([privacy](../PRIVACY.md)).
