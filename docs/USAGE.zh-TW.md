# 使用 Enhanced Snap

[README](../README.zh-TW.md) · [English](USAGE.md)

「貼齊」就是把開著的視窗並排放好，讓您同時看到、使用好幾個視窗，例如一邊看文件、一邊開瀏覽器。
Enhanced Snap 沿用 Windows 原本的操作方式，再加上圓角與間距。

## 安裝

| 管道 | 檔案 | 說明 |
| --- | --- | --- |
| [**Microsoft Store**](https://apps.microsoft.com/detail/9p14s84633ll) | Enhanced Snap | 由 Store 安裝與更新；登入時啟動（列在「設定 → 應用程式 → 啟動」）。不提供「貼齊以系統管理員身分執行的應用程式」。 |
| [**GitHub Releases**](https://github.com/VikaKumaChR/EnhancedSnap/releases/latest) | `EnhancedSnap-<版本>-x64.msi` | 以目前使用者安裝（不需系統管理員權限）到 `%LOCALAPPDATA%\Programs\Enhanced Snap`，建立開始功能表捷徑、登入時啟動，可從「設定 → 應用程式」乾淨解除安裝；會取代先前的預覽版。 |
| | `EnhancedSnap-<版本>-x64-portable.zip` | 免安裝版：解壓縮後執行 `EnhancedSnap.exe`；如需開機啟動，請在設定中開啟「登入時啟動」。 |

系統需求：**Windows 11**（組建 22000 以上）、x64。不需要另外安裝任何執行環境
（.NET 程式碼已編譯為原生程式碼，Windows App SDK 也已內含）。

> GitHub 下載的檔案目前尚未進行程式碼簽章。Microsoft Edge 可能提示檔案不常被下載，Chrome 可能標示為不常見，
> Windows 可能顯示 SmartScreen 的藍色視窗（英文版標題 *Windows protected your PC*）、發行者不明，開著 Smart App Control 時
> 也可能被擋下：這表示 Windows 還不認識這個檔案。開啟前請先核對：`Get-FileHash .\EnhancedSnap-<版本>-x64.msi` 的結果
> 要和 Release 頁面上檔案旁的 SHA-256、以及 `SHA256SUMS.txt` 相同；不同就刪除檔案。Smart App Control 無法針對單一 app 放行。
> [Store 版](https://apps.microsoft.com/detail/9p14s84633ll)由 Microsoft 簽章，Windows 不會對它發出警告。另見[程式碼簽章](CODE_SIGNING.md)。

## 基本操作

**把視窗並排。** 把視窗拖向螢幕頂端：貼齊列會先在頂端露出一截，指標到達頂端中央時再完整展開。把視窗放到其中的版面配置上，
接著貼齊輔助會列出其他開著的視窗，替每個剩下的位置各點一個。

**自選貼齊版面配置選單的版面配置。** 貼齊列與最大化按鈕（以及 <kbd>Win</kbd>＋<kbd>Z</kbd>）的浮出視窗提供同樣幾組版面配置，
名稱列在下方：**預設**（Windows 為該顯示器提供的版面；若顯示器的區域用了其他版面配置，會先列出它）、**上下分割**，以及 **自訂**，也就是您自己最多 12 個的組合。
把指標停在組合名稱上即可切換（在浮出視窗中也可以直接點選；拖曳時也可轉動滑鼠滾輪）；下次兩邊都會先開啟您選的組合。
到「設定 → 版面配置 → 貼齊版面配置選單」設定自己的組合：可加入內建或自訂版面配置、調整順序，或直接新增一個。

**一起調整大小。** 把指標移到兩個視窗之間的空隙，等分隔條出現、指標變成調整大小的箭頭後拖曳，沿著這條空隙的視窗會一起調整大小。
在螢幕頂端或底端，也可以抓住空隙的末端，一直到螢幕邊緣都可以。

**補上空位。** 群組裡關掉了一個視窗？把任何視窗拖到空出來的位置上，會先預覽它會放在哪裡，放開就加入群組。

**叫回整個群組。** 一起貼齊的視窗會成為一個群組。指標停在應用程式的工作列按鈕上，或按 <kbd>Alt</kbd>＋<kbd>Tab</kbd>，
都會看到 **群組 \| …**；選它就能一次叫回群組的所有視窗。

## 快速鍵與手勢

| 操作 | 動作 |
| --- | --- |
| 拖曳到側邊／角落／頂端 | 一半／四分之一／圓角填滿；拖近頂端時貼齊列先露出一截，頂端中央會完整展開 |
| 在貼齊列或浮出視窗中：指標停在組合名稱上（浮出視窗也可點選；拖曳時也可轉動滑鼠滾輪） | 在預設、上下分割與您的自訂組合之間切換 |
| 指標停在最大化按鈕上、<kbd>Win</kbd>＋<kbd>Z</kbd> | 貼齊版面配置浮出視窗，與貼齊列使用同一組版面配置 |
| 指標停在群組的間隙上，出現調整大小的箭頭後拖曳 | 一起調整所有相鄰視窗的大小（每個視窗最小只到其應用程式的最小尺寸，以及顯示器短邊的四分之一） |
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
