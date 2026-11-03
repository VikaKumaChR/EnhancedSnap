# Publishing Enhanced Snap

Two channels, one build (`./build.ps1`):

| Channel | Artifact | Signed by | Updates |
| --- | --- | --- | --- |
| GitHub Releases | `artifacts/dist/EnhancedSnap-<v>-x64.msi`, `…-portable.zip`, `SHA256SUMS.txt` | not signed yet (SmartScreen asks once) | download the new MSI; it upgrades in place |
| Microsoft Store | `artifacts/store/EnhancedSnap-<v>-x64.msix` | Microsoft, at certification | automatic, through the Store |

## Versions

- Public versions start at **1.0.0**; the earlier development builds (internally 1.0.0–2.3.0) are listed in the
  changelog as previews 0.1–0.5.
- One number for everything: `<Version>` in `Directory.Build.props`. The MSIX gets `x.y.z.0` (the Store requires the
  last part to be 0).
- MSI upgrade code `581B8709-7EC0-4F79-808C-B8CCFA467DA7`. Never change it again: it is how an MSI finds the version
  it replaces. The previews used `7E4B9C2A-…`; `installer/Package.wxs` removes those on install.

## Release checklist

1. Set `<Version>` and add the `## <version> — <date>` section to `CHANGELOG.md` (the GitHub release notes are taken
   from it).
2. Icon changed? Run `python tools/make_icons.py` (it checks the 3:1 contrast rule and fails below 50 %).
3. `./build.ps1` — unit tests, NativeAOT publish, ZIP, MSI, MSIX.
4. Smoke-test the MSI on a Windows 11 PC: install over the previous version, drag to the edges and corners, the
   layouts flyout (maximize button and Win + Z), Snap Assist, a snap group in the taskbar preview, Alt + Tab and Task
   View, pause and quit from the tray (Windows Snap comes back), uninstall.

## GitHub

Repository: <https://github.com/VikaKumaChR/EnhancedSnap> (`main`). `.gitignore` keeps out `ProbScreenshots/`
(private screenshots and recordings), `artifacts/`, build output, logs and the folder's `desktop.ini`; check
`git status` before committing.

Every release:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

`.github/workflows/release.yml` then builds on a Windows runner, checks that the tag matches `<Version>`, creates the
release *Enhanced Snap 1.0.0* with the MSI, the ZIP and `SHA256SUMS.txt`, and keeps the MSIX as a workflow artifact
for the Store. `ci.yml` runs the unit tests on every push and pull request.

Without the workflow: on the repository page, *Releases → Draft a new release*, tag `v1.0.0`, title
*Enhanced Snap 1.0.0*, notes from the changelog, and attach the MSI, the ZIP and `SHA256SUMS.txt` from
`artifacts/dist`.

## Microsoft Store

One time:

1. Register a developer account in [Partner Center](https://partner.microsoft.com/dashboard) (individual or company).
2. **Apps and games → New product → MSIX or PWA app**, and reserve the name **Enhanced Snap**.
3. **Product management → Product identity**: copy `Package/Identity/Name`, `Package/Identity/Publisher` and
   `Package/Properties/PublisherDisplayName` into `packaging/store-identity.json`, then rebuild. An upload whose identity
   differs from the reservation is rejected.

Each submission:

| Section | What to enter |
| --- | --- |
| Pricing and availability | Free; all markets; public. |
| Properties | Category *Productivity* (subcategory none) or *Utilities & tools*. Privacy policy URL: `https://github.com/VikaKumaChR/EnhancedSnap/blob/main/PRIVACY.md`. Website: `https://github.com/VikaKumaChR/EnhancedSnap`; support: `https://github.com/VikaKumaChR/EnhancedSnap/issues`. System requirements: Windows 11, keyboard and mouse. |
| Age ratings | IARC questionnaire: a utility with no user-generated content, chat, purchases or location → rated for everyone. |
| Packages | Upload `artifacts/store/EnhancedSnap-<v>-x64.msix`. Device family: Windows Desktop (minimum build 22000 comes from the manifest). |
| Store listings | English (United States), Chinese (Traditional), Chinese (Simplified): the texts in [`docs/store/`](store). Screenshots: at least one, 1920 × 1080 recommended (shot list in the listing files). |
| Submission options → Notes for certification | [`docs/store/certification-notes.md`](store/certification-notes.md) (explains `runFullTrust`, the Windows Snap takeover, the input hooks and how to try the app). |

Certification usually finishes within three business days.

### How the Store build differs

The package is the same NativeAOT engine and settings app with package identity. A package has no uninstall hook and
its registry and AppData writes are private to it, so (see `AppInfo.IsPackaged`):

- **Launch at sign-in** is the package's startup task (`windows.startupTask`, shown in *Settings → Apps → Startup*);
  the app's switch drives it through `StartupTask`. If the user turns it off in Windows Settings, only they can turn
  it back on; the app then says where.
- **Replace Windows Snap** changes the *Snap windows* switch for the current sign-in only (no `SPIF_UPDATEINIFILE`):
  removing the app while it runs can't leave Snap off past the next sign-in.
- **Snap apps running as administrator** is hidden: it needs a scheduled task that starts the engine elevated, outside
  the package.
- Settings and logs live in the package's `LocalCache`; *About → Open settings folder* opens that folder.
- The self-contained Windows App SDK classes are registered in the package manifest (generated from the settings
  app's side-by-side manifest by `packaging/build-msix.ps1`); `resources.pri` merges the logos with the settings app's
  own index.

### Trying the MSIX before uploading

An unsigned package installs only as a registered layout, which needs **Developer Mode** (*Settings → System → For
developers*). Then:

```powershell
Add-AppxPackage -Register artifacts\store\layout-win-x64\AppxManifest.xml   # quit the MSI version first
Get-AppxPackage *EnhancedSnap* | Remove-AppxPackage                           # remove it again
```

Check once per release: first start from Start (Settings opens with the welcome tip), launch at sign-in (listed in
*Settings → Apps → Startup*, survives sign-out), dragging to the edges with Windows Snap replaced, quitting restores
Windows Snap, snap groups in taskbar previews / Alt + Tab / Task View, *About → Open settings folder*, and uninstalling.
