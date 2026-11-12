# 使用 Enhanced Snap

[README](../README.zh-TW.md) · [English](USAGE.md)

「貼齊」就是把開著的視窗並排放好，讓您同時看到、使用好幾個視窗，例如一邊看文件、一邊開瀏覽器。
Enhanced Snap 沿用 Windows 原本的操作方式，再加上圓角與間距。

## 安裝

| 管道 | 檔案 | 說明 |
| --- | --- | --- |
| **Microsoft Store** | Enhanced Snap | 由 Store 安裝與更新；登入時啟動（列在「設定 → 應用程式 → 啟動」）。不提供「貼齊以系統管理員身分執行的應用程式」。 |
| [**GitHub Releases**](https://github.com/VikaKumaChR/EnhancedSnap/releases/latest) | `EnhancedSnap-<版本>-x64.msi` | 以目前使用者安裝（不需系統管理員權限）到 `%LOCALAPPDATA%\Programs\Enhanced Snap`，建立開始功能表捷徑、登入時啟動，可從「設定 → 應用程式」乾淨解除安裝；會取代先前的預覽版。 |
| | `EnhancedSnap-<版本>-x64-portable.zip` | 免安裝版：解壓縮後執行 `EnhancedSnap.exe`；如需開機啟動，請在設定中開啟「登入時啟動」。 |

系統需求：**Windows 11**（組建 22000 以上）、x64。不需要另外安裝任何執行環境
（.NET 程式碼已編譯為原生程式碼，Windows App SDK 也已內含）。

> GitHub 下載的檔案目前尚未進行程式碼簽章，第一次執行時 SmartScreen 可能會要求確認（「其他資訊 → 仍要執行」）。
> 可用 `SHA256SUMS.txt` 驗證下載的檔案。Store 版由 Microsoft 簽章。

## 基本操作

**把視窗並排。** 把視窗拖到螢幕頂端，放到貼齊列的版面配置上。
接著貼齊輔助會列出其他開著的視窗，替每個剩下的位置各點一個。

**一起調整大小。** 把指標移到兩個視窗之間的空隙，出現分隔條後拖曳它，沿著這條空隙的視窗會一起調整大小。

**補上空位。** 群組裡關掉了一個視窗？把任何視窗拖到空出來的位置上，會先預覽它會放在哪裡，放開就加入群組。

**叫回整個群組。** 一起貼齊的視窗會成為一個群組。指標停在應用程式的工作列按鈕上，或按 <kbd>Alt</kbd>＋<kbd>Tab</kbd>，
都會看到 **群組 \| …**；選它就能一次叫回群組的所有視窗。

## 快速鍵與手勢

| 操作 | 動作 |
| --- | --- |
| 拖曳到側邊／角落／頂端 | 一半／四分之一／圓角填滿；頂端中央會開啟貼齊列 |
| 指標停在最大化按鈕上、<kbd>Win</kbd>＋<kbd>Z</kbd> | 貼齊版面配置浮出視窗 |
| 指標停在群組的間隙上，再拖曳分隔條 | 一起調整所有相鄰視窗的大小（每個視窗最小只到其應用程式的最小尺寸，以及顯示器短邊的四分之一） |
| 把視窗拖到群組的空位上 | 加入群組並填滿該空位 |
| <kbd>Alt</kbd>＋<kbd>Tab</kbd>、<kbd>Win</kbd>＋<kbd>Tab</kbd> | 選取 **群組 \| …** 即可還原整個貼齊群組（在群組中時排在第一個） |
| 在 **群組 \| …** 上按 X（工作列預覽、Alt＋Tab、工作檢視） | 關閉群組中的所有視窗 |
| <kbd>Shift</kbd>＋拖曳 | 顯示區域並把視窗放進去（也可設定為「一律顯示」，此時按住 Shift 則可自由移動） |
| 拖曳時按 <kbd>Ctrl</kbd> | 從第一個區域跨越到目前的區域 |
| <kbd>Win</kbd>＋<kbd>←</kbd>／<kbd>→</kbd> | 移到左／右側的區域（到邊緣時繼續移到下一個顯示器） |
| <kbd>Win</kbd>＋<kbd>↑</kbd> | 上方區域，或以圓角填滿畫面；再按一次＝最大化 |
| <kbd>Win</kbd>＋<kbd>↓</kbd> | 下方區域、從填滿返回，或取消貼齊並恢復原本大小 |

按一下系統匣圖示開啟**設定**；按右鍵可選擇「版面配置」「暫停貼齊」「結束」。
再次執行 `EnhancedSnap.exe` 也會開啟設定。

## 設定檔與記錄

- 設定：`%LOCALAPPDATA%\EnhancedSnap\settings.json`（可直接閱讀，變更即時生效）
- 記錄：`%LOCALAPPDATA%\EnhancedSnap\logs\`（只記錄啟動、結束與錯誤）
- Microsoft Store 版：Windows 會把兩者放在應用程式自己的資料夾
  `%LOCALAPPDATA%\Packages\<套件>\LocalCache\Local\EnhancedSnap`，並在移除應用程式時一併刪除。
  「設定 → 關於 → 開啟設定資料夾」一律會開啟正確的位置。

所有資料都留在您的電腦上：Enhanced Snap 沒有遙測，也不會連線到網路（[隱私權](../PRIVACY.md)）。
