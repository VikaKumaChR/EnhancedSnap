# Known limitations

[README](../README.md) · [正體中文](LIMITATIONS.zh-TW.md)

Where Enhanced Snap stops short of Windows Snap or works differently, and why.

- **Apps running as administrator.** Their windows (admin terminals, Task Manager) can only be moved by an elevated
  process. Turn on *Snap apps running as administrator*: Windows asks once, then a scheduled task starts Enhanced Snap
  elevated (at sign-in too). This needs your account to be an administrator, and the installer or ZIP version: the
  Microsoft Store version can't start itself elevated, so it leaves those windows alone.
- **Windows Settings shows "Snap windows" off.** With *Replace Windows Snap* on, the switch shows **Off** while
  Enhanced Snap runs — that is the takeover. Quitting or pausing Enhanced Snap switches it back on. If a run was killed
  and the switch stayed off, start Enhanced Snap once (or run `EnhancedSnap.exe --restore-snap`). The Microsoft Store
  version never saves the change to your profile, so signing out restores Windows Snap as well.
- **Apps with one ID for the whole process.** Taskbar previews find an app's button by its AppUserModelID. Apps that
  set that ID for their whole process (for example VS Code and some other Electron apps) can't be matched from
  outside, so their previews don't show the group, and a group made only of such apps isn't listed in Alt + Tab or
  Task View; the group still works everywhere else.
- **Group thumbnails are pictures.** In Alt + Tab and Task View a group's thumbnail shows it as it was when it last
  changed or was last used, not live video like Windows' own (a peek is captured when it starts).
- **Group order in taskbar previews.** In an app's taskbar preview the group comes after the app's windows: Windows
  lists them in the order they opened, and the only way for another program to reorder that list (removing and
  re-adding the windows) erases their badges and progress bars. Alt + Tab and Task View do list the group first when
  opened with Alt + Tab or Win + Tab from one of its windows; opened with the mouse or a touchpad gesture, Task View
  places it by when it was last used.
- **"Close all windows" reaches the groups.** On an app's taskbar button it also reaches the group previews filed under
  it, so every window of the groups that app is in is asked to close as well — other apps' windows in those groups
  included.
- **Apps without the standard move loop.** Drags that don't use the standard Windows move loop (for example dragging a
  browser *tab* out, some games and remote-desktop clients) don't trigger zones.
- **Dividers by touch.** A divider takes the pointer over the whole gap between two windows (8 epx with the default
  spacing, never less than 6) and about 40 epx across at its ends. That suits a mouse, but along the seam it is narrower
  than the 40 × 40 epx Microsoft's guidelines for targeting suggest for touch. A finger works it as the mouse does
  (Windows turns touch a window doesn't handle into mouse input); it hasn't been tried on a touch screen yet.
