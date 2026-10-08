using BatteryCPUMonitor.Sensors;
using Xunit;
using static BatteryCPUMonitor.Tests.SensorTransportTests;

namespace BatteryCPUMonitor.Tests;

public class SensorDiagnosisTests
{
    private static CpuDiagnostics Cpu(string cls = "IntelCpu", string? arch = "MeteorLake", bool? moduleLoaded = true) =>
        new("Intel Core Ultra 7 155U", "Intel", 6, 170, 4, cls, arch, moduleLoaded, "Intel CPU\r\n\r\nName: Intel Core Ultra 7 155U\r\nMicroArchitecture: MeteorLake\r\n");

    private static SensorDump Temp(double? value, string name = "CPU Package") => new("Cpu", "Temperature", name, value);

    private static SensorDiagnostics Host(CpuDiagnostics? cpu = null, bool? pawnIoInstalled = true, SensorDump[]? sensors = null) =>
        new("0.9.6.0", true, pawnIoInstalled, "2.2.0.0", cpu is null ? [] : [cpu], [.. sensors ?? []], null);

    private static SensorValues NoTemperature => SensorValues.Empty;

    private static SensorValues WithTemperature => SensorSelector.Select([new SensorReading("Cpu", "Temperature", "CPU Package", 55)]);

    private static DiagnosisInput Connected(
        SensorDiagnostics? host,
        SensorValues? latest = null,
        DriverServiceState service = DriverServiceState.Running,
        string status = "",
        bool filesPresent = true) =>
        new(SensorState.Connected, status, latest ?? NoTemperature, host, service, filesPresent);

    // ---- 服務本身 ----

    [Fact]
    public void 還沒啟動或啟動中_沒有資料可以判斷()
    {
        foreach (SensorState state in new[] { SensorState.Off, SensorState.Starting })
        {
            Diagnosis diagnosis = SensorDiagnosis.Evaluate(new DiagnosisInput(state, string.Empty, null, null, DriverServiceState.Unknown));

            Assert.Equal(DiagnosisKind.Waiting, diagnosis.Kind);
        }
    }

    [Fact]
    public void 服務沒起來_結論就是服務失敗_並帶著原因()
    {
        (SensorState State, string Status)[] cases =
        [
            (SensorState.Declined, "你沒有同意系統管理員權限，硬體感測器沒有啟用。"),
            (SensorState.Failed, "連不上感測器服務（逾時）。"),
        ];

        foreach ((SensorState state, string status) in cases)
        {
            Diagnosis diagnosis = SensorDiagnosis.Evaluate(new DiagnosisInput(state, status, null, null, DriverServiceState.Running));

            Assert.Equal(DiagnosisKind.ServiceFailed, diagnosis.Kind);
            Assert.Equal(status, diagnosis.Headline);
        }
    }

    [Fact]
    public void 已連線但函式庫初始化失敗_結論是函式庫錯誤()
    {
        Diagnosis diagnosis = SensorDiagnosis.Evaluate(Connected(null, status: "感測器服務初始化失敗：驅動程式被擋下"));

        Assert.Equal(DiagnosisKind.LibraryError, diagnosis.Kind);
        Assert.Contains("驅動程式被擋下", diagnosis.Headline);
    }

    [Fact]
    public void 已連線但太久沒有新數值_繼續等()
    {
        Diagnosis diagnosis = SensorDiagnosis.Evaluate(new DiagnosisInput(SensorState.Connected, string.Empty, null, Host(Cpu()), DriverServiceState.Running));

        Assert.Equal(DiagnosisKind.Waiting, diagnosis.Kind);
    }

    [Fact]
    public void 已連線但還沒收到診斷資料_繼續等()
    {
        Assert.Equal(DiagnosisKind.Waiting, SensorDiagnosis.Evaluate(Connected(null)).Kind);
    }

    // ---- 讀得到 ----

    [Fact]
    public void 讀得到CPU溫度_一切正常_不管診斷資料怎麼說()
    {
        // 即使診斷資料看起來有問題（例如函式庫沒有認得微架構），讀得到就是讀得到。
        SensorDiagnostics host = Host(Cpu(arch: "Unknown"), pawnIoInstalled: false);

        Assert.Equal(DiagnosisKind.Working, SensorDiagnosis.Evaluate(Connected(host, WithTemperature)).Kind);
    }

    // ---- 讀不到：從哪一環開始對不上 ----

    [Fact]
    public void 函式庫沒有偵測到任何CPU()
    {
        Assert.Equal(DiagnosisKind.NoCpuDetected, SensorDiagnosis.Evaluate(Connected(Host(cpu: null))).Kind);
    }

    [Fact]
    public void 落到通用類別_函式庫沒有這顆CPU的實作()
    {
        SensorDiagnostics host = Host(Cpu(cls: "GenericCpu", arch: null), sensors: [Temp(null)]);

        Diagnosis diagnosis = SensorDiagnosis.Evaluate(Connected(host));

        Assert.Equal(DiagnosisKind.CpuNotSupported, diagnosis.Kind);
        Assert.Contains("Family 6 / Model 170 / Stepping 4", diagnosis.Advice);
        Assert.Contains("0.9.6.0", diagnosis.Advice);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("unknown")]
    public void 微架構不認得_也算函式庫不支援(string arch)
    {
        Assert.Equal(DiagnosisKind.CpuNotSupported, SensorDiagnosis.Evaluate(Connected(Host(Cpu(arch: arch), sensors: [Temp(null)]))).Kind);
    }

    [Fact]
    public void 函式庫不支援時_先講CPU_不叫使用者去裝驅動程式()
    {
        // 沒裝 PawnIO 而且 CPU 不被支援：裝了驅動也沒用，結論必須是 CPU 不被支援。
        SensorDiagnostics host = Host(Cpu(cls: "GenericCpu", arch: null, moduleLoaded: false), pawnIoInstalled: false);

        Diagnosis diagnosis = SensorDiagnosis.Evaluate(Connected(host, service: DriverServiceState.NotInstalled));

        Assert.Equal(DiagnosisKind.CpuNotSupported, diagnosis.Kind);
    }

    [Fact]
    public void 函式庫認得CPU但沒裝PawnIO_結論是沒安裝驅動程式()
    {
        SensorDiagnostics host = Host(Cpu(moduleLoaded: false), pawnIoInstalled: false, sensors: [Temp(null)]);

        Diagnosis diagnosis = SensorDiagnosis.Evaluate(Connected(host, service: DriverServiceState.NotInstalled, filesPresent: false));

        Assert.Equal(DiagnosisKind.DriverNotInstalled, diagnosis.Kind);
        Assert.Contains(PawnIoDetector.WingetCommand, diagnosis.Advice);
    }

    [Fact]
    public void 服務啟動時沒有PawnIO_現在找得到了_結論是要重新連線()
    {
        // 先啟用硬體感測器、後安裝 PawnIO：函式庫只在啟動時載入驅動程式，舊的服務永遠讀不到。
        SensorDiagnostics host = Host(Cpu(moduleLoaded: false), pawnIoInstalled: false, sensors: [Temp(null)]);

        Diagnosis diagnosis = SensorDiagnosis.Evaluate(Connected(host, service: DriverServiceState.Running, filesPresent: true));

        Assert.Equal(DiagnosisKind.RestartNeeded, diagnosis.Kind);
        Assert.Contains("重新連線", diagnosis.Advice);
    }

    [Fact]
    public void 函式庫不支援這顆CPU時_即使PawnIO是後來才裝的_仍先講CPU不被支援()
    {
        SensorDiagnostics host = Host(Cpu(cls: "GenericCpu", arch: null), pawnIoInstalled: false);

        Assert.Equal(DiagnosisKind.CpuNotSupported, SensorDiagnosis.Evaluate(Connected(host, filesPresent: true)).Kind);
    }

    [Fact]
    public void PawnIO已安裝_但這顆CPU的模組沒載入_結論是驅動程式沒載入()
    {
        SensorDiagnostics host = Host(Cpu(moduleLoaded: false), sensors: [Temp(null)]);

        Diagnosis diagnosis = SensorDiagnosis.Evaluate(Connected(host, service: DriverServiceState.Stopped));

        Assert.Equal(DiagnosisKind.DriverNotLoaded, diagnosis.Kind);
        Assert.Contains("已停止", diagnosis.Advice);
    }

    [Fact]
    public void 服務執行中卻載入不了模組_仍是驅動程式沒載入_並提醒版本不相容的可能()
    {
        SensorDiagnostics host = Host(Cpu(moduleLoaded: false), sensors: [Temp(null)]);

        Diagnosis diagnosis = SensorDiagnosis.Evaluate(Connected(host, service: DriverServiceState.Running));

        Assert.Equal(DiagnosisKind.DriverNotLoaded, diagnosis.Kind);
        Assert.Contains("不相容", diagnosis.Advice);
    }

    [Fact]
    public void 讀不出模組有沒有載入_改看服務狀態()
    {
        SensorDiagnostics host = Host(Cpu(moduleLoaded: null), sensors: [Temp(null)]);

        Assert.Equal(DiagnosisKind.DriverNotLoaded, SensorDiagnosis.Evaluate(Connected(host, service: DriverServiceState.Stopped)).Kind);
        Assert.Equal(DiagnosisKind.DriverNotLoaded, SensorDiagnosis.Evaluate(Connected(host, service: DriverServiceState.NotInstalled)).Kind);

        // 服務是執行中的，沒有證據說驅動程式沒載入，往下判斷。
        Assert.Equal(DiagnosisKind.ReadFailed, SensorDiagnosis.Evaluate(Connected(host, service: DriverServiceState.Running)).Kind);
    }

    [Fact]
    public void 驅動程式載入了_溫度感測器也建立了_但全是空的_結論是讀不到()
    {
        SensorDiagnostics host = Host(Cpu(moduleLoaded: true), sensors: [Temp(null), Temp(null, "Core Max")]);

        Assert.Equal(DiagnosisKind.ReadFailed, SensorDiagnosis.Evaluate(Connected(host)).Kind);
    }

    [Fact]
    public void 溫度是零也算沒讀到()
    {
        SensorDiagnostics host = Host(Cpu(), sensors: [Temp(0)]);

        Assert.Equal(DiagnosisKind.ReadFailed, SensorDiagnosis.Evaluate(Connected(host)).Kind);
    }

    [Fact]
    public void 函式庫認得CPU_驅動程式也正常_卻沒有任何溫度感測器()
    {
        SensorDiagnostics host = Host(Cpu(), sensors: [new SensorDump("Cpu", "Load", "CPU Total", 12)]);

        Assert.Equal(DiagnosisKind.NoTemperatureSensors, SensorDiagnosis.Evaluate(Connected(host)).Kind);
    }

    [Fact]
    public void Meteor_Lake_函式庫認得微架構_建了溫度感測器_但模組沒載入_結論是驅動程式沒載入()
    {
        // Core Ultra 7 155U 的函式庫實測結果：認得微架構、建了溫度感測器、數值全空。
        SensorDiagnostics host = Host(
            Cpu(cls: "IntelCpu", arch: "MeteorLake", moduleLoaded: false),
            sensors: [Temp(null), Temp(null, "Core Max"), Temp(null, "P-Core #1")]);

        Diagnosis diagnosis = SensorDiagnosis.Evaluate(Connected(host, service: DriverServiceState.Running));

        Assert.Equal(DiagnosisKind.DriverNotLoaded, diagnosis.Kind);
    }

    [Fact]
    public void 感測器服務不是系統管理員權限_先講權限_不講驅動程式()
    {
        SensorDiagnostics host = Host(Cpu(moduleLoaded: false), sensors: [Temp(null)]) with { Elevated = false };

        Diagnosis diagnosis = SensorDiagnosis.Evaluate(Connected(host));

        Assert.Equal(DiagnosisKind.NotElevated, diagnosis.Kind);
    }

    [Fact]
    public void 函式庫不支援這顆CPU時_即使沒有系統管理員權限_仍先講CPU不被支援()
    {
        SensorDiagnostics host = Host(Cpu(cls: "GenericCpu", arch: null)) with { Elevated = false };

        Assert.Equal(DiagnosisKind.CpuNotSupported, SensorDiagnosis.Evaluate(Connected(host)).Kind);
    }

    [Fact]
    public void IsKnownToLibrary_只有通用類別與Unknown微架構才算不認得()
    {
        Assert.True(SensorDiagnosis.IsKnownToLibrary(Cpu("IntelCpu", "MeteorLake")));
        Assert.True(SensorDiagnosis.IsKnownToLibrary(Cpu("Amd17Cpu", null)));
        Assert.False(SensorDiagnosis.IsKnownToLibrary(Cpu("GenericCpu", null)));
        Assert.False(SensorDiagnosis.IsKnownToLibrary(Cpu("IntelCpu", "Unknown")));
    }
}

public class SensorDiagnosticsReportTests
{
    private static readonly ReportEnvironment Environment = new(
        new DateTimeOffset(2026, 10, 8, 14, 3, 11, TimeSpan.FromHours(8)),
        "2.6.0",
        "Microsoft Windows 10.0.26200，X64",
        "Intel(R) Core(TM) Ultra 7 155U｜Intel64 Family 6 Model 170 Stepping 4｜GenuineIntel");

    private static SensorDiagnostics Host() => new(
        "0.9.6.0",
        true,
        true,
        "2.2.0.0",
        [new CpuDiagnostics("Intel Core Ultra 7 155U", "Intel", 6, 170, 4, "IntelCpu", "MeteorLake", false, "Intel CPU\r\n\r\nMicroArchitecture: MeteorLake\r\nTime Stamp Counter Multiplier: 0\r\n")],
        [
            new SensorDump("Cpu", "Temperature", "CPU Package", null),
            new SensorDump("Cpu", "Temperature", "Core Max", 0),
            new SensorDump("Cpu", "Power", "CPU Package", 0),
            new SensorDump("Cpu", "Load", "CPU Total", 32.14),
        ],
        null);

    private static string Report(DiagnosisInput input) => SensorDiagnosticsReport.Build(input, Environment).Text;

    private static DiagnosisInput Connected(SensorDiagnostics? host) =>
        new(SensorState.Connected, string.Empty, SensorValues.Empty, host, DriverServiceState.Running, PawnIoFilesPresent: true);

    [Fact]
    public void 報告包含結論_環境_驅動程式_函式庫對CPU的處理()
    {
        string text = Report(Connected(Host()));

        Assert.Contains("■ 判斷", text);
        Assert.Contains("PawnIO 已安裝，但驅動程式沒有載入", text);
        Assert.Contains("2026-10-08 14:03:11 +08:00", text);
        Assert.Contains("程式版本：2.6.0", text);
        Assert.Contains("Microsoft Windows 10.0.26200，X64", text);
        Assert.Contains("Intel64 Family 6 Model 170 Stepping 4", text);
        Assert.Contains("Windows 服務 PawnIO：執行中", text);
        Assert.Contains("感測器服務啟動時，函式庫認為已安裝：是（版本 2.2.0.0）", text);
        Assert.Contains("找得到 PawnIOLib.dll", text);
        Assert.Contains("函式庫的實作類別：IntelCpu", text);
        Assert.Contains("微架構：MeteorLake", text);
        Assert.Contains("Family 6（0x6）／Model 170（0xAA）／Stepping 4", text);
        Assert.Contains("PawnIO 模組是否載入：否", text);
    }

    [Fact]
    public void 報告數出溫度與功耗感測器_有多少真的有數值()
    {
        string text = Report(Connected(Host()));

        Assert.Contains("溫度感測器：2 個，有數值的 0 個", text);
        Assert.Contains("功耗感測器：1 個，有數值的 0 個", text);
    }

    [Fact]
    public void 報告列出每個感測器_沒有數值的寫明沒有數值()
    {
        string text = Report(Connected(Host()));

        Assert.Contains("Temperature｜CPU Package｜沒有數值", text);
        Assert.Contains("Load｜CPU Total｜32.14", text);
    }

    [Fact]
    public void 報告附上函式庫自己產生的CPU報告()
    {
        string text = Report(Connected(Host()));

        Assert.Contains("■ 函式庫報告：Intel Core Ultra 7 155U", text);
        Assert.Contains("Time Stamp Counter Multiplier: 0", text);
    }

    [Fact]
    public void 還沒收到診斷資料時_照樣產生報告並說明()
    {
        string text = Report(new DiagnosisInput(SensorState.Failed, "連不上感測器服務（逾時）。", null, null, DriverServiceState.NotInstalled));

        Assert.Contains("連不上感測器服務（逾時）。", text);
        Assert.Contains("診斷資料：尚未收到", text);
        Assert.Contains("Windows 服務 PawnIO：沒有這個服務", text);
        Assert.DoesNotContain("函式庫對 CPU 的處理", text);
    }

    [Fact]
    public void 初始化失敗時_報告附上完整的例外內容()
    {
        SensorDiagnostics failed = SensorDiagnostics.ForInitFailure(elevated: true, "System.IO.IOException: 驅動程式被擋下\r\n   at Foo.Bar()");
        var input = new DiagnosisInput(SensorState.Connected, "感測器服務初始化失敗：驅動程式被擋下", SensorValues.Empty, failed, DriverServiceState.Running);

        string text = Report(input);

        Assert.Contains("■ 例外內容", text);
        Assert.Contains("at Foo.Bar()", text);
        Assert.Contains("函式庫認為已安裝：未知", text);
    }

    [Fact]
    public void 報告的結論與Evaluate一致()
    {
        DiagnosisInput input = Connected(Host());

        SensorDiagnosticsView view = SensorDiagnosticsReport.Build(input, Environment);

        Assert.Equal(SensorDiagnosis.Evaluate(input), view.Diagnosis);
        Assert.StartsWith("BatteryCPUMonitor 硬體感測器診斷", view.Text);
    }
}

public class SensorDiagnosticsTransportTests
{
    private static SensorDiagnostics Sample() => new(
        "0.9.6.0",
        true,
        true,
        "2.2.0.0",
        [new CpuDiagnostics("Intel Core Ultra 7 155U", "Intel", 6, 170, 4, "IntelCpu", "MeteorLake", false, "報告\r\n第二行")],
        [new SensorDump("Cpu", "Temperature", "CPU Package", null), new SensorDump("Cpu", "Load", "CPU Total", 32.5)],
        null);

    [Fact]
    public void 診斷資料經過序列化與解析_內容一致()
    {
        SensorMessage original = Message() with { Diagnostics = Sample() };

        SensorMessage? parsed = SensorJson.TryDeserialize(SensorJson.Serialize(original));

        SensorDiagnostics diagnostics = Assert.IsType<SensorDiagnostics>(parsed?.Diagnostics);
        Assert.Equal("0.9.6.0", diagnostics.LibraryVersion);
        Assert.True(diagnostics.Elevated);
        Assert.True(diagnostics.PawnIoInstalled);
        Assert.Equal("2.2.0.0", diagnostics.PawnIoVersion);

        CpuDiagnostics cpu = Assert.Single(diagnostics.Cpus);
        Assert.Equal(Sample().Cpus[0], cpu);
        Assert.Equal("報告\r\n第二行", cpu.Report);

        Assert.Equal(2, diagnostics.CpuSensors.Count);
        Assert.Null(diagnostics.CpuSensors[0].Value);
        Assert.Equal(32.5, diagnostics.CpuSensors[1].Value);
    }

    [Fact]
    public void 含診斷資料的訊息仍然是單行()
    {
        string json = SensorJson.Serialize(Message() with { Diagnostics = Sample() });

        Assert.DoesNotContain("\n", json);
        Assert.DoesNotContain("\r", json);
    }

    [Fact]
    public void 沒有診斷資料的訊息_不會多出diag欄位()
    {
        Assert.DoesNotContain("diag", SensorJson.Serialize(Message()));
    }

    [Fact]
    public void 舊格式的訊息_沒有診斷欄位_照樣可以解析()
    {
        SensorMessage? parsed = SensorJson.TryDeserialize("{\"v\":1,\"ok\":true,\"error\":null,\"hw\":[],\"readings\":[]}");

        Assert.NotNull(parsed);
        Assert.Null(parsed.Diagnostics);
    }

    [Fact]
    public void 診斷資料殘缺_只丟掉診斷資料_數值照收()
    {
        const string line = "{\"v\":1,\"ok\":true,\"error\":null,\"hw\":[],\"readings\":[{\"h\":\"Cpu\",\"k\":\"Temperature\",\"n\":\"CPU Package\",\"v\":50}]," +
            "\"diag\":{\"lib\":\"1\",\"admin\":true,\"pio\":true,\"pioVer\":null,\"cpus\":null,\"sensors\":[],\"detail\":null}}";

        SensorMessage? parsed = SensorJson.TryDeserialize(line);

        Assert.NotNull(parsed);
        Assert.Null(parsed.Diagnostics);
        Assert.Single(parsed.Readings);
    }

    [Fact]
    public async Task 診斷資料只在第一則訊息帶上()
    {
        // 模擬真實的來源：第一次讀取帶診斷資料，之後不帶。
        using var stream = new MemoryStream();
        using var cancel = new CancellationTokenSource();
        using var source = new FakeSource(n =>
        {
            if (n == 4)
            {
                cancel.Cancel();
            }

            return n == 1 ? Message() with { Diagnostics = Sample() } : Message();
        });

        await SensorHostLoop.RunAsync(stream, source, TimeSpan.FromMilliseconds(10), cancel.Token);

        string[] lines = System.Text.Encoding.UTF8.GetString(stream.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.NotNull(SensorJson.TryDeserialize(lines[0])!.Diagnostics);
        Assert.Null(SensorJson.TryDeserialize(lines[1])!.Diagnostics);
        Assert.Null(SensorJson.TryDeserialize(lines[2])!.Diagnostics);
    }

    [Fact]
    public void 初始化失敗的診斷資料_可以序列化()
    {
        SensorMessage failure = SensorMessage.Failure("驅動程式被擋下") with
        {
            Diagnostics = SensorDiagnostics.ForInitFailure(elevated: true, "System.IO.IOException: 驅動程式被擋下"),
        };

        SensorMessage? parsed = SensorJson.TryDeserialize(SensorJson.Serialize(failure));

        Assert.Null(parsed?.Diagnostics?.PawnIoInstalled);
        Assert.Contains("驅動程式被擋下", parsed?.Diagnostics?.Detail);
    }
}

public class SensorEnvironmentTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void 處理器與作業系統的描述查得到()
    {
        string processor = SensorEnvironment.ReadProcessor();
        string os = SensorEnvironment.DescribeOperatingSystem();
        output.WriteLine(processor);
        output.WriteLine(os);

        Assert.False(string.IsNullOrWhiteSpace(processor));
        Assert.Contains("Windows", os);
    }

    [Fact]
    public void 查PawnIO服務狀態_不論有沒有安裝都不會丟出例外()
    {
        DriverServiceState state = SensorEnvironment.QueryPawnIoService();
        output.WriteLine(state.ToString());

        Assert.True(Enum.IsDefined(state));
    }
}
