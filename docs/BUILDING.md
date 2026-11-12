# Building Enhanced Snap

[README](../README.md) · [正體中文](BUILDING.zh-TW.md)

## Requirements

.NET 10 SDK, Visual Studio 2022/2026 with *Desktop development with C++* (NativeAOT linker), and the Windows 11 SDK
(for the MSIX).

## Build

```powershell
./build.ps1                    # tests → NativeAOT publish → artifacts/dist/*.zip, *.msi, SHA256SUMS.txt (GitHub)
                               #   and artifacts/store/*.msix (Microsoft Store upload)
./build.ps1 -Version 1.0.1     # default: <Version> in Directory.Build.props
python tools/make_icons.py     # re-render every icon asset from the one design (Pillow, NumPy)
```

Releasing to GitHub and the Microsoft Store: [PUBLISHING.md](PUBLISHING.md).

## Projects

| Project | Purpose |
| --- | --- |
| `src/EnhancedSnap.Core` | Settings model, layout catalog, zone geometry, displays, localization (unit tested) |
| `src/EnhancedSnap` | `EnhancedSnap.exe` — background engine: hooks, overlay, placement, tray (NativeAOT, no UI framework) |
| `src/EnhancedSnap.Settings` | `EnhancedSnap.Settings.exe` — WinUI 3 settings app (NativeAOT, self-contained) |
| `tests/EnhancedSnap.Core.Tests` | xUnit tests for geometry, navigation, settings |
| `installer/` | WiX v5 per-user MSI |
| `packaging/` | MSIX for the Microsoft Store: manifest template, logos, store identity, `build-msix.ps1` |
| `tools/` | Icon generator and live-desktop test probes (not shipped) |

Extension points for future updates are marked with `UPDATE:` comments in the code and collected in
[ROADMAP.md](ROADMAP.md).
