<p align="center"><img src="assets/AppIcon-256.png" width="96" alt="Enhanced Snap icon"></p>

<h1 align="center">Enhanced Snap</h1>

<p align="center">The Windows Snap replacement for Windows 11, with rounded corners and even spacing.</p>

<p align="center"><a href="https://apps.microsoft.com/detail/9p14s84633ll?mode=direct"><img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200" alt="Download from the Microsoft Store"></a></p>

<p align="center"><a href="#install">Install</a> · <a href="#documentation">Documentation</a> · <a href="CHANGELOG.md">Changelog</a> · <a href="README.zh-TW.md">正體中文</a> · <a href="README.zh-CN.md">简体中文</a></p>

---

## Why

Windows 11 rounds window corners, and apps such as Microsoft Edge now draw their own rounded, inset content
frame. Windows Snap still tiles windows edge to edge and switches them to **square corners** while snapped,
so a multi-window desktop ends up with doubled borders, square frames around rounded content, and no breathing
room.

Enhanced Snap snaps windows into zones **with Fluent spacing between them, and keeps every window's 8 px
rounded corners**, using an overlay that follows Windows 11's own materials and motion.

<p align="center"><img src="docs/images/demo-compare.gif" width="800" alt="The top of the seam between two snapped windows, close up. With Windows Snap the corners are square and the windows touch (0 px). With Enhanced Snap the corners stay rounded and there are 8 px between the windows and the screen edge."></p>

## Replaces Windows Snap

While Enhanced Snap runs it **turns Windows Snap off and does the job itself**, and gives it back the moment
Enhanced Snap is paused, quits or is uninstalled (after a crash, on its next start). Windows that Windows Snap already
arranged become Enhanced Snap groups.

| Windows Snap | Enhanced Snap |
| --- | --- |
| Snap layouts on the maximize button and <kbd>Win</kbd> + <kbd>Z</kbd> | The same flyout, with Windows' layouts and **snap suggestions** in Windows' own look, plus a full-size preview of the zone you point at; **the same sets as the snap bar** |
| Drag to a side, a corner or the top; the snap bar | The same gestures, and windows **keep their rounded corners and spacing**; near the top the snap bar peeks, and slides all the way down when the pointer reaches the top centre |
| The snap bar always offers the same few layouts | **Your own layouts in the snap layout menu**: Default, Top and bottom and a Custom set of up to 12; rest the pointer on a set's name to switch, or turn the wheel while you drag |
| Snap Assist | Live thumbnails in the remaining zones; mouse or keyboard |
| Snap groups in taskbar previews, <kbd>Alt</kbd> + <kbd>Tab</kbd> and Task View | The same **Group \| …** entries — peek from the taskbar, click to bring the group back, **X closes all its windows** — listed first when you switch from one of its windows |
| A group's empty zone stays empty until Snap Assist runs again | **Drag any window into the empty space** and it joins the group |
| A divider on the seam between two windows | An acrylic divider on **every gap**, and no window is squeezed below its minimum size |
| Snaps apps running as administrator | The same, after one prompt (installer and ZIP versions) |
| Square corners, edge to edge | An Edge-style **frame behind each group** in Mica, Mica Alt, Acrylic or a solid color |

## More than Windows Snap

- **Spacing you set** – between windows and around the screen edges, scaled for each monitor's DPI; every window's
  visible frame lands on its zone to the pixel.
- **Layouts for each monitor** – 14 templates, from halves and thirds to a center focus for ultrawide screens and rows
  for portrait ones, plus your own columns and rows.
- **Zones on demand** – hold <kbd>Shift</kbd> while dragging to see every zone; add <kbd>Ctrl</kbd> to span several.
- **Keyboard** – <kbd>Win</kbd> + arrow keys move a window between zones and across monitors; <kbd>Win</kbd> +
  <kbd>↑</kbd> fills the screen *with* rounded corners (press again to maximize).
- **Live** – change the spacing or a layout and snapped windows re-flow at once; they stay in their zones when the
  resolution, taskbar or monitors change.
- **Restore on unsnap** – drag a snapped window out and it gets its original size back.
- **Light** – no timers or polling while idle, about 7 MB of memory.
- **Private** – no telemetry and no network connections.
- **Excluded apps**, **launch at sign-in**, **English / 繁體中文 / 简体中文**.

## Install

Requires Windows 11 (build 22000 or later), x64.

- **[Microsoft Store](https://apps.microsoft.com/detail/9p14s84633ll?mode=direct)** (recommended): installs and
  updates through the Store. Microsoft signs the package, so Windows doesn't warn about it.
- **[GitHub Releases](https://github.com/VikaKumaChR/EnhancedSnap/releases/latest)**: `EnhancedSnap-<version>-x64.msi`
  (per-user install, no admin prompt) or the portable ZIP.

<details>
<summary>Why Windows may warn about a GitHub download, and how to check one</summary>

The GitHub downloads are not code-signed yet, so Microsoft Edge may say the file *isn't commonly downloaded*, Chrome
may call it uncommon, Windows may show *Windows protected your PC* with an unknown publisher, and Smart App Control,
if it is on, may block the app. That means Windows doesn't know the file yet, not that anything was found in it.

Before you open it, run `Get-FileHash .\EnhancedSnap-<version>-x64.msi` in PowerShell in your Downloads folder. The
hash must match the SHA-256 shown next to the file on the release page and its line in `SHA256SUMS.txt`; if it
doesn't, delete the file. Smart App Control has no per-app exception: if it blocks the app, use the Store version.
More in [code signing](docs/CODE_SIGNING.md).

</details>

## Documentation

- **Using it**: [usage](docs/USAGE.md) (the basics, shortcuts, settings and logs) ·
  [known limitations](docs/LIMITATIONS.md) · [privacy](PRIVACY.md)
- **How it works**: [design](docs/DESIGN.md) · [energy and rendering](docs/ENERGY.md) ·
  [building from source](docs/BUILDING.md) · [code signing](docs/CODE_SIGNING.md)
- **What's next**: [roadmap](docs/ROADMAP.md) · [changelog](CHANGELOG.md)

## License and contributors

[![License: MIT](https://img.shields.io/github/license/VikaKumaChR/EnhancedSnap)](LICENSE)
[![Contributors](https://img.shields.io/github/contributors/VikaKumaChR/EnhancedSnap)](https://github.com/VikaKumaChR/EnhancedSnap/graphs/contributors)

Enhanced Snap is open source under the [MIT License](LICENSE); the third-party components in its release builds keep
their own licenses ([notices](THIRD-PARTY-NOTICES.md)). Issues and pull requests are welcome; everyone who has
contributed is on the [contributors page](https://github.com/VikaKumaChR/EnhancedSnap/graphs/contributors).
Enhanced Snap is an independent project and is not affiliated with Microsoft.
