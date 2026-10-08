using System.Globalization;
using System.Text;

namespace BatteryCPUMonitor.Sensors;

/// <summary>主程式這一端（一般權限）自己查得到的環境資訊，加上時間與版本。</summary>
/// <param name="Processor">Windows 登錄檔記載的處理器名稱與識別字串，與函式庫的偵測結果對照用。</param>
internal sealed record ReportEnvironment(
    DateTimeOffset Time,
    string AppVersion,
    string OperatingSystem,
    string Processor);

/// <summary>一份診斷資訊：結論，加上可以直接複製的完整文字。</summary>
internal sealed record SensorDiagnosticsView(Diagnosis Diagnosis, string Text);

/// <summary>把診斷結果與原始資料排成一份純文字，方便貼到 issue 或對話裡。純計算。</summary>
internal static class SensorDiagnosticsReport
{
    public static SensorDiagnosticsView Build(DiagnosisInput input, ReportEnvironment environment)
    {
        Diagnosis diagnosis = SensorDiagnosis.Evaluate(input);
        return new SensorDiagnosticsView(diagnosis, Format(input, environment, diagnosis));
    }

    private static string Format(DiagnosisInput input, ReportEnvironment environment, Diagnosis diagnosis)
    {
        var text = new StringBuilder();
        SensorDiagnostics? host = input.Host;

        text.AppendLine("BatteryCPUMonitor 硬體感測器診斷");
        text.AppendLine("時間：" + environment.Time.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        text.AppendLine();

        Section(text, "判斷");
        text.AppendLine(diagnosis.Headline);
        text.AppendLine(diagnosis.Advice);

        Section(text, "環境");
        text.AppendLine("程式版本：" + environment.AppVersion);
        text.AppendLine("作業系統：" + environment.OperatingSystem);
        text.AppendLine("處理器（Windows 回報）：" + environment.Processor);

        Section(text, "感測器服務");
        text.AppendLine($"狀態：{StateText(input.State)}");
        text.AppendLine("訊息：" + (input.Status.Length > 0 ? input.Status : "（無）"));
        if (host is null)
        {
            text.AppendLine("診斷資料：尚未收到（服務沒有起來，或還在初始化）");
        }
        else
        {
            text.AppendLine("以系統管理員權限執行：" + YesNo(host.Elevated));
            text.AppendLine("LibreHardwareMonitorLib：" + host.LibraryVersion);
        }

        Section(text, "PawnIO 驅動程式");
        text.AppendLine("檔案（登錄檔或 Program Files）：" + (input.PawnIoFilesPresent ? "找得到 PawnIOLib.dll" : "找不到 PawnIOLib.dll"));
        text.AppendLine("Windows 服務 PawnIO：" + SensorDiagnosis.ServiceText(input.ServiceState));
        if (host is not null)
        {
            string installed = host.PawnIoInstalled is bool value ? YesNo(value) : "未知";
            text.AppendLine($"感測器服務啟動時，函式庫認為已安裝：{installed}" + (host.PawnIoVersion is { Length: > 0 } version ? $"（版本 {version}）" : string.Empty));
        }

        if (host is not null)
        {
            AppendCpus(text, host);
            AppendSelected(text, input.Latest);
            AppendSensors(text, host);

            if (host.Detail is { Length: > 0 })
            {
                Section(text, "例外內容");
                text.AppendLine(host.Detail);
            }

            foreach (CpuDiagnostics cpu in host.Cpus.Where(c => c.Report is { Length: > 0 }))
            {
                Section(text, "函式庫報告：" + cpu.Name);
                text.AppendLine(cpu.Report!.TrimEnd());
            }
        }

        return text.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendCpus(StringBuilder text, SensorDiagnostics host)
    {
        Section(text, "函式庫對 CPU 的處理");
        if (host.Cpus.Count == 0)
        {
            text.AppendLine("函式庫沒有偵測到任何 CPU。");
            return;
        }

        foreach (CpuDiagnostics cpu in host.Cpus)
        {
            text.AppendLine($"CPU：{cpu.Name}");
            text.AppendLine($"廠牌：{cpu.Vendor ?? "未知"}　Family {cpu.Family}（0x{cpu.Family:X}）／Model {cpu.Model}（0x{cpu.Model:X}）／Stepping {cpu.Stepping}");
            text.AppendLine($"函式庫的實作類別：{cpu.LibraryClass}" + (cpu.LibraryClass == "GenericCpu" ? "（通用類別：沒有這個型號的專用實作）" : string.Empty));
            text.AppendLine($"微架構：{cpu.MicroArchitecture ?? "（這個廠牌讀不到）"}");
            text.AppendLine("PawnIO 模組是否載入：" + (cpu.DriverModuleLoaded is bool loaded ? YesNo(loaded) : "未知"));
        }

        List<SensorDump> temperatures = host.CpuSensors.Where(s => s.Type == "Temperature").ToList();
        List<SensorDump> powers = host.CpuSensors.Where(s => s.Type == "Power").ToList();
        text.AppendLine($"溫度感測器：{temperatures.Count} 個，有數值的 {temperatures.Count(s => s.HasValue)} 個");
        text.AppendLine($"功耗感測器：{powers.Count} 個，有數值的 {powers.Count(s => s.HasValue)} 個");
    }

    private static void AppendSelected(StringBuilder text, SensorValues? values)
    {
        Section(text, "程式挑出來顯示的數值");
        if (values is null)
        {
            text.AppendLine("（沒有）");
            return;
        }

        text.AppendLine($"CPU 溫度：{SensorFormat.Temperature(values.CpuTemperature)}　GPU 溫度：{SensorFormat.Temperature(values.GpuTemperature)}");
        text.AppendLine($"CPU 功耗：{SensorFormat.Power(values.CpuPowerWatts)}　GPU 功耗：{SensorFormat.Power(values.GpuPowerWatts)}　GPU 頻率：{SensorFormat.Clock(values.GpuClockMHz)}");
        text.AppendLine($"風扇：{(values.Fans.Count == 0 ? "（沒有）" : string.Join("、", values.Fans.Select(f => $"{f.Label} {SensorFormat.Fan(f.Rpm)}")))}");
    }

    private static void AppendSensors(StringBuilder text, SensorDiagnostics host)
    {
        Section(text, "CPU 感測器明細（種類｜名稱｜數值）");
        if (host.CpuSensors.Count == 0)
        {
            text.AppendLine("（函式庫沒有替 CPU 建立任何感測器）");
            return;
        }

        foreach (SensorDump sensor in host.CpuSensors)
        {
            string value = sensor.Value is double number && double.IsFinite(number)
                ? number.ToString("0.##", CultureInfo.InvariantCulture)
                : "沒有數值";
            text.AppendLine($"{sensor.Type}｜{sensor.Name}｜{value}");
        }
    }

    private static void Section(StringBuilder text, string title)
    {
        text.AppendLine();
        text.AppendLine("■ " + title);
    }

    private static string YesNo(bool value) => value ? "是" : "否";

    private static string StateText(SensorState state) => state switch
    {
        SensorState.Off => "未連線（Off）",
        SensorState.Starting => "啟動中（Starting）",
        SensorState.Connected => "已連線（Connected）",
        SensorState.Declined => "沒有同意系統管理員權限（Declined）",
        _ => "失敗（Failed）",
    };
}
