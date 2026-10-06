# 第三方軟體聲明

BatteryCPUMonitor 的發布檔案內含下列第三方軟體。

## LibreHardwareMonitorLib

- 用途：讀取 CPU、GPU、主機板的溫度、功耗、風扇等感測器（選用的「硬體感測器」功能）。
- 版本：0.9.6（未經修改，直接使用 NuGet 上發布的套件）
- 授權：Mozilla Public License 2.0（MPL-2.0）
  - 授權全文：<https://www.mozilla.org/MPL/2.0/>
- 原始碼：<https://github.com/LibreHardwareMonitor/LibreHardwareMonitor>
- 套件頁面：<https://www.nuget.org/packages/LibreHardwareMonitorLib/>

MPL-2.0 是檔案層級的授權：LibreHardwareMonitorLib 本身的原始碼維持 MPL-2.0 並可從上方連結取得；
BatteryCPUMonitor 自己的程式碼不受其影響。

LibreHardwareMonitorLib 還依賴其他套件（例如 HidSharp、RAMSPDToolkit-NDD、DiskInfoToolkit、
System.IO.Ports、System.Management 等），各自的授權請見它們在 NuGet 上的套件頁面。

## PawnIO（不包含在本程式中）

讀取 CPU 與主機板感測器需要的核心驅動程式 [PawnIO](https://pawnio.eu/) **不包含在本程式裡，也不會由本程式安裝**。
使用者需要時，請自行從官方網站取得並安裝。
