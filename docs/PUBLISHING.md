# Publishing Enhanced Snap

Two channels, one build (`./build.ps1`):

| Channel | Artifact | Signed by | Updates |
| --- | --- | --- | --- |
| GitHub Releases | `artifacts/dist/EnhancedSnap-<v>-x64.msi`, `…-portable.zip`, `SHA256SUMS.txt` | not signed yet, so SmartScreen and Smart App Control may stop them; the release workflow signs through SignPath once there is a certificate ([code signing](#code-signing-signpath)) | download the new MSI; it upgrades in place |
| Microsoft Store ([listing](https://apps.microsoft.com/detail/9p14s84633ll)) | `artifacts/store/EnhancedSnap-<v>-x64.msix` | Microsoft, at certification | automatic, through the Store |

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

`.github/workflows/release.yml` then builds on a Windows runner, checks that the tag matches `<Version>`, has the MSI
and the ZIP signed once signing is set up (below; SignPath e-mails the approver, who confirms the request within the
workflow's 60-minute wait), creates the release *Enhanced Snap 1.0.0* with the MSI, the ZIP and `SHA256SUMS.txt`, and keeps the MSIX as a
workflow artifact for the Store. `ci.yml` runs the unit tests on every push and pull request.

Without the workflow: on the repository page, *Releases → Draft a new release*, tag `v1.0.0`, title
*Enhanced Snap 1.0.0*, notes from the changelog, and attach the MSI, the ZIP and `SHA256SUMS.txt` from
`artifacts/dist`.

## Code signing (SignPath)

The release workflow can sign the GitHub downloads through SignPath ([code signing](CODE_SIGNING.md)). It signs nothing
until the values below exist, and until then publishes unsigned releases whose notes say so. The certificate comes
from SignPath Foundation's free program for open-source projects, which first looks for public adoption (stars, forks,
contributors, outside articles or discussions; apply at <https://signpath.org/apply> once Enhanced Snap has more of
them), or from a paid SignPath subscription.

One time, once there is a certificate:

1. Every team member turns on multi-factor authentication for GitHub and SignPath (a SignPath Foundation condition).
   With the Foundation, `docs/CODE_SIGNING.md` also becomes its code signing policy: the line *Free code signing
   provided by SignPath.io, certificate by SignPath Foundation*, the team roles (committers and reviewers, approvers)
   and the privacy statement, linked from the README as *Code signing policy*.
2. In SignPath: add the predefined trusted build system **GitHub.com** and link it to the project; set the project's
   repository URL to <https://github.com/VikaKumaChR/EnhancedSnap>; paste
   [`packaging/signpath-artifact-configuration.xml`](../packaging/signpath-artifact-configuration.xml) as the default
   artifact configuration; in the signing policy, turn on *Use approval process* (the maintainer approves),
   *Verify origin* with the allowed branch `main`, and make a CI user its submitter.
3. Create an API token for the CI user. In the GitHub repository settings, add it as the secret `SIGNPATH_API_TOKEN`,
   and add the variables `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG` and `SIGNPATH_SIGNING_POLICY_SLUG` (shown
   on the signing policy's page in SignPath). Optional: `SIGNING_PUBLISHER`, the signer name to expect (default
   *SignPath Foundation*; with a paid subscription, the certificate's name), and `STORE_URL`, another Microsoft Store
   link for the release notes than the listing they show first.
   Signing keys and tokens stay with SignPath and in the repository settings, never in the repository.

Still to confirm on the first signed release:

- The workflow runs for a tag, while SignPath's origin check names allowed branches. If SignPath refuses a tag build,
  run the release from `main` instead (for example a manually started workflow that creates the tag at the end).
- The artifact configuration nests the MSI and the portable ZIP in the uploaded ZIP; if SignPath refuses that, sign
  them in two requests.
- SignPath may refuse a re-run of the same workflow run. If signing fails (a denied request, or no approval within
  the hour), delete the tag and push it again, which starts a new run, instead of re-running the failed one.

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

For the test tools, an exe started by its path runs outside the package. Start the engine inside it, in test mode, and
then run `tools/grouptest.ps1` or `tools/switchertest.ps1` as usual:

```powershell
$p = Get-AppxPackage EnhancedSnap
$cmd = "`$env:ENHANCEDSNAP_TEST_WINDOW_TITLE='EnhancedSnap Test Window'; & '$($p.InstallLocation)\EnhancedSnap.exe' --verbose"
Invoke-CommandInDesktopPackage -AppId EnhancedSnap -PackageFamilyName $p.PackageFamilyName -Command powershell.exe -Args "-NoProfile -WindowStyle Hidden -Command $cmd"
```

Started from Start instead, the engine reads `%LOCALAPPDATA%\Packages\<family>\LocalCache\Local\EnhancedSnap\settings.json`
first, so a seed file there (with your open apps in `excludedApps`) keeps real settings and windows out of the test.
