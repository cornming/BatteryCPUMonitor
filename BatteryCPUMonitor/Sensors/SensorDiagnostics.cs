using System.Text.Json.Serialization;

namespace BatteryCPUMonitor.Sensors;

/// <summary>CPU 的一個感測器，不論有沒有讀到數值都列出來（沒有數值就是 null）。</summary>
/// <param name="Type">Temperature、Power、Clock、Load…，沿用 LibreHardwareMonitor 的名稱。</param>
internal sealed record SensorDump(
    [property: JsonPropertyName("h")] string Hardware,
    [property: JsonPropertyName("t")] string Type,
    [property: JsonPropertyName("n")] string Name,
    [property: JsonPropertyName("v")] double? Value)
{
    /// <summary>有沒有讀到「有意義」的數值：溫度與功耗不可能剛好是 0，0 代表驅動程式沒有把數值讀回來。</summary>
    [JsonIgnore]
    public bool HasValue => Value is double value && double.IsFinite(value) && value > 0;
}

/// <summary>函式庫偵測到的一顆 CPU，以及函式庫對它的處理方式。</summary>
/// <param name="LibraryClass">函式庫替這顆 CPU 選的實作：IntelCpu、Amd17Cpu…；GenericCpu 代表函式庫沒有針對這個型號的實作。</param>
/// <param name="MicroArchitecture">函式庫認得的微架構（目前只有 Intel 讀得到），Unknown 代表不認得。</param>
/// <param name="DriverModuleLoaded">這顆 CPU 用的 PawnIO 模組有沒有載入成功；讀不出來時為 null。</param>
internal sealed record CpuDiagnostics(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("vendor")] string? Vendor,
    [property: JsonPropertyName("family")] long Family,
    [property: JsonPropertyName("model")] long Model,
    [property: JsonPropertyName("stepping")] long Stepping,
    [property: JsonPropertyName("cls")] string LibraryClass,
    [property: JsonPropertyName("arch")] string? MicroArchitecture,
    [property: JsonPropertyName("loaded")] bool? DriverModuleLoaded,
    [property: JsonPropertyName("report")] string? Report);

/// <summary>
/// 感測器服務啟動後蒐集一次的診斷資料，隨第一則訊息送回主程式。
/// 只包含 CPU 相關的內容，不含主機板序號之類的識別資訊。
/// </summary>
/// <param name="LibraryVersion">LibreHardwareMonitorLib 的版本。</param>
/// <param name="PawnIoInstalled">函式庫認為有沒有安裝 PawnIO；沒辦法查（函式庫根本載入失敗）時為 null。</param>
/// <param name="Detail">初始化失敗時的完整例外內容。</param>
internal sealed record SensorDiagnostics(
    [property: JsonPropertyName("lib")] string LibraryVersion,
    [property: JsonPropertyName("admin")] bool Elevated,
    [property: JsonPropertyName("pio")] bool? PawnIoInstalled,
    [property: JsonPropertyName("pioVer")] string? PawnIoVersion,
    [property: JsonPropertyName("cpus")] List<CpuDiagnostics> Cpus,
    [property: JsonPropertyName("sensors")] List<SensorDump> CpuSensors,
    [property: JsonPropertyName("detail")] string? Detail)
{
    /// <summary>函式庫根本沒初始化成功時的診斷資料：只能說明例外，沒有硬體可看。</summary>
    public static SensorDiagnostics ForInitFailure(bool elevated, string detail) =>
        new("?", elevated, null, null, [], [], detail);
}

/// <summary>PawnIO 在 Windows 服務管理員裡的狀態。</summary>
internal enum DriverServiceState
{
    /// <summary>查不到（沒有權限或呼叫失敗）。</summary>
    Unknown,

    /// <summary>沒有這個服務：沒安裝，或安裝壞了。</summary>
    NotInstalled,

    Stopped,

    Running,

    /// <summary>正在啟動或停止。</summary>
    Changing,
}

internal enum DiagnosisKind
{
    /// <summary>還沒有資料可以判斷（沒啟用、啟動中、等第一批數值）。</summary>
    Waiting,

    /// <summary>CPU 溫度讀得到。</summary>
    Working,

    /// <summary>感測器服務本身沒起來（沒同意權限、逾時、提前結束…），還沒走到驅動程式或 CPU 這一步。</summary>
    ServiceFailed,

    /// <summary>服務起來了，但函式庫初始化或讀取就丟出例外。</summary>
    LibraryError,

    NoCpuDetected,

    /// <summary>函式庫沒有這顆 CPU 的實作或不認得微架構。裝再多次驅動程式都沒用。</summary>
    CpuNotSupported,

    /// <summary>感測器服務沒有系統管理員權限：驅動程式開不起來，這時候查驅動程式本身沒有意義。</summary>
    NotElevated,

    /// <summary>PawnIO 是在感測器服務啟動之後才裝的。函式庫只在啟動時載入驅動程式，之後不會重試，重新連線就好。</summary>
    RestartNeeded,

    DriverNotInstalled,

    /// <summary>PawnIO 裝了，但函式庫開不起來。</summary>
    DriverNotLoaded,

    /// <summary>驅動程式載入了、函式庫也認得 CPU，但溫度暫存器讀回來是空的。</summary>
    ReadFailed,

    /// <summary>函式庫認得這顆 CPU，卻沒有替它建立任何溫度感測器。</summary>
    NoTemperatureSensors,
}

/// <param name="Headline">一句話的結論，也用在選單的狀態列。</param>
/// <param name="Advice">接下來可以怎麼做。</param>
internal sealed record Diagnosis(DiagnosisKind Kind, string Headline, string Advice);

/// <param name="Status">感測器服務的狀態說明（失敗原因等），沒有就是空字串。</param>
/// <param name="Host">感測器服務送回來的診斷資料；還沒收到或服務沒起來時為 null。</param>
/// <param name="ServiceState">主程式（一般權限）從服務管理員查到的 PawnIO 服務狀態。</param>
/// <param name="PawnIoFilesPresent">主程式「現在」找得到 PawnIOLib.dll。與服務啟動時的偵測結果對照，就知道 PawnIO 是不是後來才裝的。</param>
internal sealed record DiagnosisInput(
    SensorState State,
    string Status,
    SensorValues? Latest,
    SensorDiagnostics? Host,
    DriverServiceState ServiceState,
    bool PawnIoFilesPresent = false);

/// <summary>
/// 判斷「CPU 溫度為什麼沒有出來」：從服務一路往下查，第一個對不上的環節就是原因。純計算，不碰硬體。
/// </summary>
/// <remarks>
/// 順序是刻意的：先確認「函式庫認不認得這顆 CPU」，再看驅動程式。CPU 不被支援時，驅動程式裝得再好也沒用，
/// 所以不能先叫使用者去裝驅動程式。
/// </remarks>
internal static class SensorDiagnosis
{
    public static Diagnosis Evaluate(DiagnosisInput input)
    {
        switch (input.State)
        {
            case SensorState.Off:
                return new(DiagnosisKind.Waiting, "硬體感測器沒有在執行", "到選單「硬體感測器」勾選「啟用硬體感測器」；已經啟用的話按「重新連線」。");

            case SensorState.Starting:
                return new(DiagnosisKind.Waiting, "正在啟動，等待授權與初始化…", "請在 Windows 的系統管理員確認視窗按「是」。第一次初始化可能要幾十秒。");

            case SensorState.Declined:
            case SensorState.Failed:
                return new(
                    DiagnosisKind.ServiceFailed,
                    input.Status,
                    "這是感測器服務本身沒有起來，還沒有走到驅動程式或 CPU 支援這一步。按「重新連線」再試一次；一再失敗的話，把診斷資訊貼出來。");
        }

        // 以下都是已連線。
        if (input.Status.Length > 0)
        {
            return new(
                DiagnosisKind.LibraryError,
                input.Status,
                "感測器服務連上了，但 LibreHardwareMonitor 初始化或讀取失敗。常見原因是驅動程式被安全軟體擋下，或 PawnIO 沒有安裝好。" +
                "完整的例外內容在診斷資訊最下方。");
        }

        if (input.Latest?.CpuTemperature is not null)
        {
            return new(DiagnosisKind.Working, "CPU 溫度讀取正常", "不需要處理。");
        }

        if (input.Latest is null)
        {
            return new(DiagnosisKind.Waiting, "已連線，等待數值…", "感測器服務每秒回報一次；超過幾秒都沒有數值的話，先停用再啟用硬體感測器。");
        }

        SensorDiagnostics? host = input.Host;
        if (host is null)
        {
            return new(DiagnosisKind.Waiting, "已連線，等待診斷資料…", "診斷資料在感測器服務初始化完成後的第一則訊息裡，稍等一下再看。");
        }

        CpuDiagnostics? cpu = host.Cpus.FirstOrDefault();
        if (cpu is null)
        {
            return new(
                DiagnosisKind.NoCpuDetected,
                "函式庫沒有偵測到任何 CPU",
                "這通常發生在虛擬機器，或函式庫根本沒有成功初始化。完整內容請看診斷資訊。");
        }

        if (!IsKnownToLibrary(cpu))
        {
            return new(
                DiagnosisKind.CpuNotSupported,
                "函式庫不支援這顆 CPU（問題不在驅動程式）",
                $"LibreHardwareMonitor {host.LibraryVersion} 沒有這顆 CPU 的實作（類別 {cpu.LibraryClass}，微架構 {cpu.MicroArchitecture ?? "未知"}，" +
                $"Family {cpu.Family} / Model {cpu.Model} / Stepping {cpu.Stepping}）。重新安裝驅動程式沒有幫助。" +
                "等新版程式升級函式庫，或把診斷資訊貼到 LibreHardwareMonitor 的 issue，請他們加入這個型號。");
        }

        if (!host.Elevated)
        {
            return new(
                DiagnosisKind.NotElevated,
                "感測器服務沒有系統管理員權限",
                "PawnIO 驅動程式只有系統管理員權限開得起來，所以這時候不管驅動程式裝得好不好都讀不到。" +
                "按「重新連線」，並在 Windows 的確認視窗按「是」；如果沒有跳出確認視窗，檢查「使用者帳戶控制」是否被關閉。");
        }

        if (host.PawnIoInstalled == false && input.PawnIoFilesPresent)
        {
            return new(
                DiagnosisKind.RestartNeeded,
                "PawnIO 是感測器服務啟動之後才裝的，要重新連線",
                "感測器服務啟動時還沒有 PawnIO，而函式庫只在啟動時載入驅動程式，之後裝好也不會重試。" +
                "選單「硬體感測器」→「重新連線」，同意系統管理員權限後就會讀到。");
        }

        if (host.PawnIoInstalled == false)
        {
            return new(
                DiagnosisKind.DriverNotInstalled,
                "沒有安裝 PawnIO 驅動程式",
                $"函式庫認得這顆 CPU，只是缺驅動程式。選單「硬體感測器」→「安裝 PawnIO…」，或在系統管理員的命令提示字元執行：{PawnIoDetector.WingetCommand}。" +
                "裝完按「重新連線」。");
        }

        bool serviceMissing = input.ServiceState is DriverServiceState.NotInstalled or DriverServiceState.Stopped;
        if (cpu.DriverModuleLoaded == false || (cpu.DriverModuleLoaded is null && serviceMissing))
        {
            return new(
                DiagnosisKind.DriverNotLoaded,
                "PawnIO 已安裝，但驅動程式沒有載入",
                $"函式庫認得這顆 CPU，但它開不起來 PawnIO（PawnIO 服務狀態：{ServiceText(input.ServiceState)}）。" +
                "先重新開機；剛安裝完常常需要。還是不行的話，檢查安全軟體（含 Windows 的核心隔離）有沒有擋下驅動程式，" +
                "或重新安裝 PawnIO。服務是「執行中」卻載入不了，也可能是 PawnIO 版本與函式庫內建的模組不相容。");
        }

        if (!host.CpuSensors.Any(s => s.Hardware == "Cpu" && s.Type == "Temperature"))
        {
            return new(
                DiagnosisKind.NoTemperatureSensors,
                "函式庫認得這顆 CPU，卻沒有建立溫度感測器",
                "驅動程式正常，但函式庫沒有替這顆 CPU 建立任何溫度感測器，多半是這個型號的溫度讀取還沒實作。" +
                "把診斷資訊貼到 LibreHardwareMonitor 的 issue。");
        }

        return new(
            DiagnosisKind.ReadFailed,
            "驅動程式已載入、CPU 也支援，但讀不到溫度",
            "函式庫建立了溫度感測器，驅動程式也載入了，但讀回來的暫存器是空的。常見原因：在虛擬機器或被 hypervisor 限制了暫存器存取、" +
            "BIOS 鎖住，或這個型號的 PawnIO 模組還不支援。把診斷資訊（含最下方的函式庫報告）貼到 LibreHardwareMonitor 或 PawnIO 的 issue。");
    }

    /// <summary>函式庫有沒有針對這顆 CPU 的實作：落到通用類別，或微架構不認得，都算沒有。</summary>
    internal static bool IsKnownToLibrary(CpuDiagnostics cpu) =>
        !string.Equals(cpu.LibraryClass, "GenericCpu", StringComparison.Ordinal)
        && !string.Equals(cpu.MicroArchitecture, "Unknown", StringComparison.OrdinalIgnoreCase);

    internal static string ServiceText(DriverServiceState state) => state switch
    {
        DriverServiceState.NotInstalled => "沒有這個服務",
        DriverServiceState.Stopped => "已停止",
        DriverServiceState.Running => "執行中",
        DriverServiceState.Changing => "啟動或停止中",
        _ => "查不到",
    };
}
