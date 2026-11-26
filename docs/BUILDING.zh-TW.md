# 建置 Enhanced Snap

[README](../README.zh-TW.md) · [English](BUILDING.md)

## 需求

.NET 10 SDK、Visual Studio 2022／2026（含「使用 C++ 的桌面開發」，NativeAOT 連結器需要）、Windows 11 SDK（MSIX 需要）。

## 建置

```powershell
./build.ps1                    # 單元測試 → NativeAOT 發佈 → artifacts/dist/*.zip、*.msi、SHA256SUMS.txt（GitHub）
                               #   以及 artifacts/store/*.msix（上傳 Microsoft Store）
./build.ps1 -Version 1.0.1     # 預設：Directory.Build.props 中的 <Version>
```

發佈到 GitHub 與 Microsoft Store 的步驟：[PUBLISHING.md](PUBLISHING.md)（英文）。

## 專案

| 專案 | 用途 |
| --- | --- |
| `src/EnhancedSnap.Core` | 設定模型、版面配置目錄、區域幾何、顯示器、在地化（有單元測試） |
| `src/EnhancedSnap` | `EnhancedSnap.exe`：背景引擎，負責 hook、疊加層、視窗放置與系統匣（NativeAOT，不使用 UI 架構） |
| `src/EnhancedSnap.Settings` | `EnhancedSnap.Settings.exe`：WinUI 3 設定程式（NativeAOT、獨立式部署） |
| `tests/EnhancedSnap.Core.Tests` | 幾何、導覽與設定的 xUnit 測試 |
| `installer/` | WiX v5 個別使用者 MSI |
| `packaging/` | Microsoft Store 的 MSIX：資訊清單範本、標誌、商店識別資訊、`build-msix.ps1` |
| `tools/` | 圖示產生器與桌面實測工具（不隨產品發佈） |

程式碼中以 `UPDATE:` 註解標示可後續更新的擴充點，並整理於 [ROADMAP.md](ROADMAP.md)（英文）。
