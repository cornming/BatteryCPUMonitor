using BatteryCPUMonitor.Metrics;
using Xunit;

namespace BatteryCPUMonitor.Tests;

/// <summary>
/// 在真正的 Windows 上實際呼叫系統 API，確認與 Windows 之間的資料結構對接沒有寫錯。
/// 這些測試在 GitHub Actions 的 Windows 主機上執行；非 Windows 環境直接略過。
/// </summary>
public class LiveSystemTests
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
