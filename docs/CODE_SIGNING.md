# Code signing

[README](../README.md) · [Usage](USAGE.md) · [Privacy](../PRIVACY.md)

| Download | Signed by |
| --- | --- |
| [Microsoft Store](https://apps.microsoft.com/detail/9p14s84633ll) | Microsoft, when the Store publishes the package |
| `EnhancedSnap-<version>-x64.msi` and `EnhancedSnap-<version>-x64-portable.zip` on [GitHub Releases](https://github.com/VikaKumaChR/EnhancedSnap/releases) | not code-signed yet |

## Why Windows may warn about the GitHub downloads

The installer and the portable ZIP carry no signature yet, and a new release has few downloads. So Microsoft Edge may
say the file *isn't commonly downloaded*, Chrome may call it uncommon, Windows may show *Windows protected your PC* with
an unknown publisher, and Smart App Control, if it is on, may block the app. Smart App Control has no per-app
exception. These warnings mean Windows doesn't know the file yet, not that anything was found in it. Windows doesn't
warn about the Store version, which Microsoft signs.

## Check a download

In PowerShell, in your Downloads folder, `Get-FileHash .\EnhancedSnap-<version>-x64.msi` must match the SHA-256 shown
next to the file on the release page and its line in `SHA256SUMS.txt`. If it doesn't, delete the file.

## Signing the GitHub downloads

The release workflow ([`release.yml`](../.github/workflows/release.yml)) is ready to sign them through
[SignPath](https://about.signpath.io), and signs nothing until the project has a signing certificate. Then each release
works like this:

1. GitHub Actions builds the release from this repository on a GitHub-hosted runner, with the unit tests first.
2. The workflow uploads the installer and the portable ZIP and submits them to SignPath, which checks that they come
   from this repository's workflow and that the executables carry this release's product name and version. It signs
   `EnhancedSnap.exe` and `EnhancedSnap.Settings.exe` inside both, and then the installer
   ([artifact configuration](../packaging/signpath-artifact-configuration.xml)). Every other file in them is Microsoft's
   Windows App SDK runtime, already signed by Microsoft.
3. A maintainer approves the signing request in SignPath. Nothing is signed without that approval.
4. The workflow checks every signature, writes `SHA256SUMS.txt` from the signed files and publishes the release.

A signed release can still get a browser or SmartScreen warning while it is new: Windows builds a file's reputation
from downloads over time.

## Privacy

Enhanced Snap makes no network connections ([privacy policy](../PRIVACY.md)).
