# BatteryCPUMonitor

放在螢幕邊緣的極簡資訊橫條，一眼看到電量、CPU 與記憶體使用率。

- **極簡**：只有一條字，不佔工作列。
- **免設定**：下載後執行就能用，沒有設定視窗。
- **不需要系統管理員權限**：所有數值都透過一般使用者可用的 Windows API 取得。

```
電量:82% / 充電中 / CPU:12% / RAM:61%
```

## 下載

到 [Releases](https://github.com/cornming/BatteryCPUMonitor/releases/latest) 下載最新版，兩個檔案擇一：

| 檔案 | 說明 |
| --- | --- |
| `BatteryCPUMonitor-vX.Y.Z-standalone.exe` | 免安裝完整版，下載後直接執行，檔案較大 |
| `BatteryCPUMonitor-vX.Y.Z.exe` | 精簡版，電腦需先安裝 [.NET 10 桌面執行階段](https://dotnet.microsoft.com/download/dotnet/10.0) |

執行檔沒有程式碼簽章，第一次執行時 Windows SmartScreen 可能會跳出警告，按「其他資訊」→「仍要執行」即可。

系統需求：Windows 10 或 Windows 11（64 位元）。

## 使用方式

| 操作 | 效果 |
| --- | --- |
| 滑鼠移到橫條上 | 字體放大，方便閱讀 |
| 左鍵按住拖曳 | 移動橫條，可拖到其他螢幕；位置會記住，下次啟動回到原處 |
| 在橫條或系統匣圖示上按右鍵 | 開啟選單 |
| 滑鼠停在系統匣圖示上 | 以提示文字顯示目前數值 |

橫條預設出現在主螢幕底部正中央、工作列上方，而且不會被拖出螢幕外。

### 選單

| 項目 | 說明 |
| --- | --- |
| 開機自動啟動 | 登入 Windows 後自動執行。設定寫在目前使用者的登錄機碼，不需要系統管理員權限 |
| 滑鼠穿透 | 開啟後橫條不再擋住滑鼠，點擊會落到後面的視窗。此時橫條本身點不到，要關閉請從系統匣圖示操作 |
| 重設位置 | 回到主螢幕底部正中央 |
| 關閉 | 結束程式 |

開機自動啟動記的是執行檔當下的路徑。之後如果搬移或更新了執行檔，請重新勾選一次。

## 顯示內容

| 項目 | 說明 |
| --- | --- |
| 電量 | 電池剩餘百分比。桌機等沒有電池的電腦不會顯示這一項 |
| 充電中／已接電源 | 正在充電時顯示「充電中」；接著電源但沒在充電（例如已充飽）時顯示「已接電源」 |
| CPU | 過去一秒的平均使用率。採用效能計數器「Processor Utility」，數字較接近工作管理員；該計數器無法使用時自動改用 `GetSystemTimes` 計算 |
| RAM | 實體記憶體使用率 |

三個項目各自依下列門檻變色：

| 項目 | 綠色 | 黃色 | 紅色 |
| --- | --- | --- | --- |
| 電量（未接電源時） | 41% 以上 | 21%–40% | 20% 以下 |
| CPU | 低於 60% | 60%–84% | 85% 以上 |
| RAM | 低於 80% | 80%–89% | 90% 以上 |

只要接著電源，電量一律顯示綠色。

## 程式會留下的資料

| 位置 | 內容 |
| --- | --- |
| `%AppData%\BatteryCPUMonitor\settings.json` | 橫條位置與滑鼠穿透的開關 |
| 登錄機碼 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `BatteryCPUMonitor` | 只有勾選「開機自動啟動」時才會寫入，取消勾選即移除 |

## 從原始碼建置

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 與 Windows。

```powershell
dotnet build BatteryCPUMonitor.sln
dotnet test BatteryCPUMonitor.sln
dotnet run --project BatteryCPUMonitor
```

產生單一執行檔：

```powershell
dotnet publish BatteryCPUMonitor/BatteryCPUMonitor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish/standalone
```

### 專案結構

| 路徑 | 內容 |
| --- | --- |
| `BatteryCPUMonitor/BarForm.cs` | 橫條視窗：繪製、拖曳、放大、選單、系統匣、每秒更新 |
| `BatteryCPUMonitor/BarText.cs` | 決定顯示的文字與各段的警示等級（純計算） |
| `BatteryCPUMonitor/Levels.cs` | 警示等級與門檻（純計算） |
| `BatteryCPUMonitor/BarPlacement.cs` | 計算橫條擺放位置（純計算） |
| `BatteryCPUMonitor/AppSettings.cs` | 讀寫自動記住的狀態 |
| `BatteryCPUMonitor/AutoStart.cs` | 開機自動啟動 |
| `BatteryCPUMonitor/Metrics/` | 讀取電池、CPU、記憶體 |
| `BatteryCPUMonitor.Tests/` | 單元測試 |
| `.github/` | 自動建置與發版流程 |

## 版本與發版

每次推送到 `master`，GitHub Actions 會自動建置、測試、跳版號並發布 Release，更新內容直接取自提交訊息。推送到 `dev/` 開頭的分支只會建置與測試，不會發布，適合在合併前先驗證。版號依提交標題的前綴決定：

| 提交標題 | 版號變化 | 範例 |
| --- | --- | --- |
| `feat!: ...`（或其他類型加 `!`） | 主版號 +1 | 2.3.1 → 3.0.0 |
| `feat: ...` | 次版號 +1 | 2.3.1 → 2.4.0 |
| 其他（`fix:`、`refactor:`、`ci:` 等） | 修訂號 +1 | 2.3.1 → 2.3.2 |

提交內文中以 `- ` 開頭的行，會成為 Release 說明裡該項目的子項目。只修改 Markdown 文件不會觸發發版。

## 規劃

- [x] 第一階段：升級 .NET 10、修正更新時畫面卡頓、自動建置與發版
- [x] 第二階段：電量／CPU／RAM 各自依門檻變色、記住位置、多螢幕與高 DPI、系統匣圖示、開機自動啟動、滑鼠穿透
- [ ] 第三階段：滑鼠移入時顯示電池詳情（健康度、耗電瓦數、預估剩餘或充滿時間）

不打算做的事：溫度、風扇、GPU 監控。這些需要系統管理員權限載入驅動程式，與本專案的定位不合；有這類需求建議使用 [LiteMonitor](https://github.com/Diorser/LiteMonitor)。

## 參考專案

- [Diorser/LiteMonitor](https://github.com/Diorser/LiteMonitor)：橫條形式與三色警示的參考
- [glzr-io/zebar](https://github.com/glzr-io/zebar)：電池資訊欄位的參考

1.x 的原始版本保留在 [`v1.0.0`](https://github.com/cornming/BatteryCPUMonitor/tree/v1.0.0) 標籤。
