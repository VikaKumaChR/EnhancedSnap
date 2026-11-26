# Notes for certification

Paste the text between the lines into *Submission options → Notes for certification*. The certification testers read
it; customers don't see it. Rewrite the first paragraph for each update.

---

Update 1.1.0, submitted 2026-11-27. Same package identity and capabilities as 1.0.1 (runFullTrust only; nothing new
declared).

What changed:
- New app icon (all logos in the package).
- A Settings page at the bottom of the navigation: Theme (system, light or dark, for this window) and Language, then
  Send feedback, Open logs, Open settings folder, Quit and an About section (version, license, privacy, repository).
- Send feedback opens the project's public GitHub issue form in the browser. The URL carries only the app version, the
  edition and the Windows build; the app sends nothing itself, and the user submits on GitHub with their own account.
- General page, "Snap apps running as administrator": this version can't run elevated (no allowElevation or uiAccess),
  so instead of a switch it says so and links to the project's GitHub releases. The app downloads and installs nothing.

How to try it:
1. Start Enhanced Snap from Start. It keeps running in the notification area.
2. Drag a window to the left edge of the screen: a rounded zone preview appears. Release to snap; Snap Assist offers
   the other windows.
3. Hover a window's maximize button or press Win + Z: the snap layouts flyout opens.
4. Select Settings at the bottom of the navigation and set Theme to Dark: the window turns dark at once.
5. Right-click the tray icon > Quit: Windows Snap works as before.

As in 1.0.1:
- While it runs, the app turns off Windows' "Snap windows" for the current sign-in only (SPI_SETWINARRANGING, not
  saved) and turns it back on when paused or closed.
- Its shortcuts use a low-level keyboard hook; keys are not logged, stored or transmitted.
- No account, no network access, no ads, no in-app purchases.
