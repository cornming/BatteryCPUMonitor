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
        collector.Sample();
        Thread.Sleep(1100);
        MetricsSnapshot m = collector.Sample();

        Assert.True(m.CpuPercent is >= 0 and <= 100, $"CPU = {m.CpuPercent}");
        Assert.True(m.RamPercent is > 0 and <= 100, $"RAM = {m.RamPercent}");
        Assert.True(m.DiskReadBytesPerSecond is >= 0, $"讀取 = {m.DiskReadBytesPerSecond}");
        Assert.True(m.DiskWriteBytesPerSecond is >= 0, $"寫入 = {m.DiskWriteBytesPerSecond}");
        Assert.True(m.Network is { UploadBytesPerSecond: >= 0, DownloadBytesPerSecond: >= 0 }, $"網路 = {m.Network}");
    }

    [Fact]
    public void 收集器_釋放後再取樣不會當掉()
    {
        if (NotWindows) { return; }

        var collector = new MetricsCollector();
        collector.Dispose();

        Assert.Equal(MetricsSnapshot.Empty, collector.Sample());
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
