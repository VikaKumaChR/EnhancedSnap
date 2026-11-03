# Notes for certification

Paste into *Submission options → Notes for certification*.

---

Enhanced Snap is a window manager for Windows 11: it replaces Windows Snap with the same gestures and adds rounded
corners, even spacing and frames for snap groups. It is a desktop app (C# compiled with NativeAOT, plus a WinUI 3
settings app) and declares runFullTrust because it:

- arranges other apps' windows with Win32 window APIs (SetWindowPos, DWM attributes) and shows overlay windows;
- follows window moves with WinEvent hooks, and its shortcuts (Win + arrow keys, Win + Z, Alt + Tab, Win + Tab;
  Shift and Ctrl while dragging) with a low-level keyboard hook. Keys are not logged, stored or transmitted;
- registers snap-group entries for the taskbar previews, Alt + Tab and Task View (DWM iconic thumbnails).

Windows Snap: with "Replace Windows Snap" on (default; Settings > General), the app turns off Windows' "Snap
windows" setting while it runs (SPI_SETWINARRANGING, for the current sign-in only, not saved to the profile) and turns
it back on when paused or closed. Signing out also restores it.

No account, no sign-in, no network access, no in-app purchases, no ads.

How to try it:

1. Start Enhanced Snap from Start. The settings window opens with a welcome message; the app keeps running in the
   notification area (tray icon).
2. Drag any window to the left edge of the screen: a rounded zone preview appears. Release: the window snaps with
   rounded corners, and Snap Assist offers the other open windows for the rest of the screen.
3. Hover a window's maximize button (or press Win + Z): the snap layouts flyout opens. Pick a zone.
4. With two windows snapped together, hover their app on the taskbar: a "Group | ..." preview appears; Alt + Tab lists
   the group too.
5. Right-click the tray icon > Quit: Windows Snap works as before.
