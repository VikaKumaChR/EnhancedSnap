# Roadmap — planned updates

Every item below has an `UPDATE:` comment at the place in the code where it plugs in
(search the repository for `UPDATE:`).

Shipped in 1.0 (built up over the previews 0.1–0.5): Windows Snap takeover, edge snapping and the snap bar, the snap
layouts flyout with Windows' layout set and snap suggestions, Snap Assist, snap groups with frames (Mica, Mica Alt,
Acrylic, solid), hover dividers with placeholders and minimum-size limits, filling a group's empty space, snap groups
in the taskbar previews, Alt + Tab and Task View (first, peek in place, X closes the group), administrator mode, the
product icon, and the Microsoft Store package next to the MSI and ZIP.

## Next

| Update | Where it plugs in | Notes |
| --- | --- | --- |
| **Remember snap groups across restarts** (persist group fractions + window identities) | `src/EnhancedSnap/Groups/GroupManager.cs` | Re-attach by process + title after sign-in |
| **Taskbar previews for apps with a process-wide AppUserModelID** (VS Code, Electron apps) | `src/EnhancedSnap/Groups/AppIds.cs` | Map executable → ID through the Start menu's AppsFolder entries; also lists groups made only of such apps in Alt + Tab and Task View |
| **Full acrylic recipe** (luminosity blend + 2 % noise) for the divider bar and the Acrylic frame | `src/EnhancedSnap/Ui/FluentTheme.cs` | Needs a composition effect graph and a long-lived noise surface |
| **Keyboard navigation in the snap layouts flyout** (number keys like Windows' Win + Z) | `src/EnhancedSnap/Ui/LayoutFlyout.cs` | Routed through the input hook's popup keys |
| **Group first in Task View opened from the taskbar button or a touchpad gesture** | `src/EnhancedSnap/Input/InputHook.cs` | Today only Alt + Tab and Win + Tab put the group first; the button needs its position from UI Automation |
| **Snap Assist for spanned/large layouts with paging** (more than 9 candidates) | `src/EnhancedSnap/Ui/SnapAssist.cs` | Scroll or page dots |
| **Code signing** + **winget** manifest | `installer/Package.wxs` | Removes SmartScreen prompts for the GitHub downloads (the Store package is signed by Microsoft) |
| **Update check** for the installer and ZIP versions | `src/EnhancedSnap.Settings/Pages/AboutPage.xaml.cs` | GitHub Releases API; skip when `AppInfo.IsPackaged` (the Store updates it) |
| **Snapping elevated windows in the Store version** | `src/EnhancedSnap/Platform/Elevation.cs` | Needs an elevated start with package identity (`allowElevation` capability, Store approval) instead of a scheduled task |

## Later

| Update | Where it plugs in |
| --- | --- |
| Live group thumbnails in Alt + Tab / Task View (Windows.Graphics.Capture instead of snapshots) | `src/EnhancedSnap/Groups/TaskbarGroups.cs` |
| Per-virtual-desktop layouts (`IVirtualDesktopManager`) | `src/EnhancedSnap.Core/Layouts/LayoutCatalog.cs` |
| Free-form "canvas" layouts (overlapping zones) | `src/EnhancedSnap.Core/Settings/AppSettings.cs` (`CustomLayout`) |
| Direct-manipulation layout editor (drag dividers on screen) | `src/EnhancedSnap.Settings/Controls/LayoutEditorDialog.cs` |
| Non-primary/middle mouse button activation, mouse-wheel layout switching while dragging | `SnapEngine.UpdateDrag` |
| Zone numbers / dragged app icon inside the drop target (Direct2D/DirectWrite drawing surfaces) | `src/EnhancedSnap/Overlay/ZoneOverlay.cs` |
| Fluent tray flyout instead of the Win32 context menu | `src/EnhancedSnap/Tray/TrayIcon.cs` |
| "Pick from running apps" for exclusions | `src/EnhancedSnap.Settings/Pages/AppsPage.xaml.cs` |
| More UI languages | `src/EnhancedSnap.Core/Localization/Strings.cs` |
| Windows App SDK 2.x component packages (smaller settings payload) | `src/EnhancedSnap.Settings/EnhancedSnap.Settings.csproj` |
| ARM64 release (needs MSVC ARM64 tools on the build machine) | `build.ps1 -Runtime win-arm64` |
