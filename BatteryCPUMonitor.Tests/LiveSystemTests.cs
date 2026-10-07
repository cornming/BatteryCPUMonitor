using Xunit.Abstractions;
using System.Diagnostics;
using BatteryCPUMonitor.Metrics;
using Xunit;

namespace BatteryCPUMonitor.Tests;

/// <summary>
/// 在真正的 Windows 上實際呼叫系統 API，確認與 Windows 之間的資料結構對接沒有寫錯。
/// 這些測試在 GitHub Actions 的 Windows 主機上執行；非 Windows 環境直接略過。
/// </summary>
public class LiveSystemTests(ITestOutputHelper output)
{
    private static bool NotWindows => !OperatingSystem.IsWindows();

    [Fact]
    public void 效能計數器_可以開啟並加入CPU與磁碟計數器()
    {
        if (NotWindows) { return; }

        using PdhQuery? query = PdhQuery.TryOpen();
        Assert.NotNull(query);

        Assert.NotNull(query.TryAdd(@"\Processor Information(_Total)\% Processor Utility"));
        Assert.NotNull(query.TryAdd(@"\PhysicalDisk(_Total)\Disk Read Bytes/sec"));
        Assert.NotNull(query.TryAdd(@"\PhysicalDisk(_Total)\Disk Write Bytes/sec"));
    }

    [Fact]
    public void 效能計數器_不存在的計數器回傳空值而不是當掉()
    {
        if (NotWindows) { return; }

        using PdhQuery? query = PdhQuery.TryOpen();
        Assert.NotNull(query);

        Assert.Null(query.TryAdd(@"\No Such Category(_Total)\No Such Counter"));
        Assert.Null(query.Read(null));
    }

    [Fact]
    public void 收集器_第二次取樣起各項數值都在合理範圍()
    {
        if (NotWindows) { return; }

        using var collector = new MetricsCollector();
        collector.Sample(VisibleItems.All);
        Thread.Sleep(1100);
        MetricsSnapshot m = collector.Sample(VisibleItems.All);

        Assert.True(m.CpuPercent is >= 0 and <= 100, $"CPU = {m.CpuPercent}");
        Assert.True(m.RamPercent is > 0 and <= 100, $"RAM = {m.RamPercent}");
        Assert.True(m.DiskReadBytesPerSecond is >= 0, $"讀取 = {m.DiskReadBytesPerSecond}");
        Assert.True(m.DiskWriteBytesPerSecond is >= 0, $"寫入 = {m.DiskWriteBytesPerSecond}");
        Assert.True(m.Network is { UploadBytesPerSecond: >= 0, DownloadBytesPerSecond: >= 0 }, $"網路 = {m.Network}");

        // CPU 頻率：虛擬機器不一定提供；有數值的話必須在合理範圍內。
        Assert.True(m.CpuClockMHz is null or (> 0 and < 20000), $"CPU 頻率 = {m.CpuClockMHz}");

        // 建置主機不一定有 GPU；有數值的話必須在合理範圍內。
        Assert.True(m.GpuPercent is null or (>= 0 and <= 100), $"GPU = {m.GpuPercent}");
        Assert.True(m.GpuMemoryBytes is null or >= 0, $"顯存 = {m.GpuMemoryBytes}");
    }

    [Fact]
    public void 效能計數器_萬用字元計數器可以讀出每個執行個體的名稱與數值()
    {
        if (NotWindows) { return; }

        using PdhQuery? query = PdhQuery.TryOpen();
        Assert.NotNull(query);
        IntPtr? perCore = query.TryAdd(@"\Processor Information(*)\% Processor Utility");
        Assert.NotNull(perCore);

        query.Collect();
        Thread.Sleep(300);
        query.Collect();
        var items = query.ReadArray(perCore);

        // 每個邏輯處理器各一筆，再加上各群組與全部的總計，至少會有兩筆；名稱裡一定有「_Total」。
        Assert.NotNull(items);
        Assert.True(items.Count >= 2, $"筆數 = {items.Count}");
        Assert.True(items.Any(i => i.Instance.Contains("_Total")), $"名稱 = {string.Join(", ", items.Select(i => i.Instance))}");
        // 「Processor Utility」在處理器超頻運作（Turbo）時本來就會超過 100，所以只檢查數值合理。
        Assert.True(items.All(i => i.Value is >= 0 and < 1000), $"數值 = {string.Join(", ", items.Select(i => i.Value))}");
    }

    [Fact]
    public void 電池裝置列舉_正常結束()
    {
        if (NotWindows) { return; }

        List<string> paths = BatteryDetailReader.EnumerateDevicePaths(out int lastError);

        // 259 = 沒有更多項目，代表列舉用的資料結構大小正確（寫錯會得到別的錯誤碼）。
        Assert.True(lastError == 259, $"錯誤碼 = {lastError}，找到 {paths.Count} 個電池裝置");
    }

    [Fact]
    public void 電池詳情_沒有電池時回傳空值而不是當掉()
    {
        if (NotWindows) { return; }

        using var reader = new BatteryDetailReader();
        BatteryDetail? detail = reader.Read(pluggedIn: true, charging: false);

        Assert.True(detail is null || detail.Value.HealthPercent is null or (> 0 and <= 100), $"電池詳情 = {detail}");
    }

    [Theory]
    [InlineData(CpuUsageMode.Time)]
    [InlineData(CpuUsageMode.Utility)]
    public void 收集器_兩種CPU算法在第二次取樣起都在零到一百之間(CpuUsageMode mode)
    {
        if (NotWindows) { return; }

        using var collector = new MetricsCollector { CpuMode = mode };
        collector.Sample(VisibleItems.All);
        Thread.Sleep(600);
        MetricsSnapshot m = collector.Sample(VisibleItems.All);

        output.WriteLine($"{mode}：CPU = {m.CpuPercent}");
        Assert.True(m.CpuPercent is >= 0 and <= 100, $"{mode}：CPU = {m.CpuPercent}");
    }

    [Fact]
    public void 收集器_算法可以在執行中切換()
    {
        if (NotWindows) { return; }

        using var collector = new MetricsCollector();
        Assert.Equal(CpuUsageMode.Time, collector.CpuMode);

        collector.CpuMode = CpuUsageMode.Utility;

        Assert.Equal(CpuUsageMode.Utility, collector.CpuMode);
    }

    /// <summary>
    /// 在「同一個時間區間」裡同時量三種數字：我們自己用 GetSystemTimes 算的忙碌時間、Windows 的 % Processor Time 計數器、
    /// Windows 的 % Processor Utility 計數器。前兩者應該幾乎一樣（驗證忙碌時間算法沒有算錯）；第三者可以不同。
    /// 量測期間讓每個邏輯處理器大約一半的時間在忙。
    /// </summary>
    [Fact]
    public async Task CPU使用率_忙碌時間算法與Windows自己的處理器時間計數器一致()
    {
        if (NotWindows) { return; }

        using PdhQuery? query = PdhQuery.TryOpen();
        Assert.NotNull(query);
        IntPtr? time = query.TryAdd(@"\Processor Information(_Total)\% Processor Time");
        IntPtr? utility = query.TryAdd(@"\Processor Information(_Total)\% Processor Utility");
        Assert.NotNull(time);

        var systemTimes = new SystemTimesCpu();
        query.Collect();
        systemTimes.Sample();

        using var stop = new CancellationTokenSource();
        Task[] load = Enumerable.Range(0, Environment.ProcessorCount).Select(_ => Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                long start = Stopwatch.GetTimestamp();
                while (Stopwatch.GetElapsedTime(start) < TimeSpan.FromMilliseconds(50)) { }
                Thread.Sleep(50);
            }
        })).ToArray();

        await Task.Delay(3000);
        query.Collect();
        double? pdhTime = query.Read(time, allowAbove100: true);
        double? pdhUtility = query.Read(utility, allowAbove100: true);
        double? ours = systemTimes.Sample();

        stop.Cancel();
        await Task.WhenAll(load);

        output.WriteLine($"同一個 3 秒區間（{Environment.ProcessorCount} 個邏輯處理器）：忙碌時間（GetSystemTimes）= {ours:0.0}%，% Processor Time = {pdhTime:0.0}%，% Processor Utility = {pdhUtility:0.0}%");
        Assert.NotNull(ours);
        Assert.NotNull(pdhTime);
        Assert.True(Math.Abs(ours.Value - pdhTime.Value) < 15, $"忙碌時間 {ours:0.0}% 與 Windows 的 % Processor Time {pdhTime:0.0}% 差太多");
        Assert.True(ours.Value > 20, $"有負載時應該明顯大於 0，實際 {ours:0.0}%");
    }

    /// <summary>診斷用：量收集器每一段各花多少處理器時間，不是正確性測試。</summary>
    [Fact]
    public void 診斷_收集器各段花費的時間()
    {
        if (NotWindows) { return; }

        const int rounds = 20;
        var network = new NetworkSampler();
        using PdhQuery? query = PdhQuery.TryOpen();
        IntPtr? read = query?.TryAdd(@"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
        using var gpuQuery = PdhQuery.TryOpen();
        IntPtr? engines = gpuQuery?.TryAdd(@"\GPU Engine(*)\Utilization Percentage");
        IntPtr? memory = gpuQuery?.TryAdd(@"\GPU Adapter Memory(*)\Dedicated Usage");
        var times = new SystemTimesCpu();

        double Measure(string name, Action action)
        {
            action(); // 暖機
            using Process self = Process.GetCurrentProcess();
            TimeSpan before = self.TotalProcessorTime;
            for (int i = 0; i < rounds; i++)
            {
                action();
            }

            self.Refresh();
            double ms = (self.TotalProcessorTime - before).TotalMilliseconds / rounds;
            output.WriteLine($"{name}：{ms:0.0} 毫秒");
            return ms;
        }

        Measure("網路（列舉網卡與流量）", () => network.Sample());
        Measure("一般效能計數器收集", () => { query?.Collect(); query?.Read(read); });
        Measure("GPU 計數器收集與讀取（萬用字元）", () => { gpuQuery?.Collect(); gpuQuery?.ReadArray(engines); gpuQuery?.ReadArray(memory); });
        Measure("電池狀態", () => BatteryReader.Read());
        Measure("記憶體", () => MemoryReader.ReadUsedPercent());
        Measure("GetSystemTimes", () => times.Sample());
        output.WriteLine($"GPU 計數器的執行個體數：{gpuQuery?.ReadArray(engines)?.Count}");
    }

    /// <summary>
    /// 監控程式自己也會用掉 CPU，用太多的話數字就會被自己拉高。
    /// 這裡量「每次取樣」平均花多少處理器時間（全部項目都打開，包含最花時間的 GPU 與網路）。
    /// </summary>
    [Fact]
    public void 收集器自己的CPU負擔_每次取樣不會用掉太多處理器時間()
    {
        if (NotWindows) { return; }

        using var collector = new MetricsCollector();
        collector.Sample(VisibleItems.All);
        Thread.Sleep(300);
        collector.Sample(VisibleItems.All);

        using Process self = Process.GetCurrentProcess();
        TimeSpan before = self.TotalProcessorTime;
        const int samples = 20;
        for (int i = 0; i < samples; i++)
        {
            collector.Sample(VisibleItems.All);
            Thread.Sleep(50);
        }

        self.Refresh();
        double perSampleMs = (self.TotalProcessorTime - before).TotalMilliseconds / samples;
        output.WriteLine($"每次取樣平均花 {perSampleMs:0.0} 毫秒的處理器時間（每秒取樣一次，約佔一顆核心的 {perSampleMs / 10:0.0}%）");

        // 每秒取樣一次，超過 100 毫秒就等於一直佔掉一顆核心的一成以上，不合理。
        Assert.True(perSampleMs < 100, $"每次取樣平均 {perSampleMs:0.0} 毫秒");
    }

    [Fact]
    public void 收集器_釋放後再取樣不會當掉()
    {
        if (NotWindows) { return; }

        var collector = new MetricsCollector();
        collector.Dispose();

        Assert.Equal(MetricsSnapshot.Empty, collector.Sample(VisibleItems.All));
    }

    [Fact]
    public void CPU備援算法_第二次取樣在零到一百之間()
    {
        if (NotWindows) { return; }

        var cpu = new SystemTimesCpu();
        Assert.Null(cpu.Sample());
        Thread.Sleep(300);
        double? usage = cpu.Sample();

        Assert.True(usage is >= 0 and <= 100, $"CPU = {usage}");
    }

    [Fact]
    public void 電池狀態_讀取結果前後一致()
    {
        if (NotWindows) { return; }

        BatterySnapshot battery = BatteryReader.Read();

        // 建置主機沒有電池；不論有沒有，「有電池」就一定讀得到 0–100 的電量。
        Assert.True(!battery.HasBattery || battery.Percent is >= 0 and <= 100, $"電池 = {battery}");
    }

    [Fact]
    public void 網路取樣_在任何平台上第二次取樣都有數值()
    {
        var network = new NetworkSampler();
        network.Sample();
        Thread.Sleep(200);
        NetworkRate? rate = network.Sample();

        Assert.True(rate is { UploadBytesPerSecond: >= 0, DownloadBytesPerSecond: >= 0 }, $"網路 = {rate}");
    }
}
