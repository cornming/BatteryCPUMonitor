using BatteryCPUMonitor.Metrics;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class BarContentTests
{
    private const double KB = 1024;
    private const double MB = 1024 * 1024;

    private static BatterySnapshot Battery(int percent, bool pluggedIn = false, bool charging = false, int? secondsRemaining = null) =>
        new(HasBattery: true, Percent: percent, IsPluggedIn: pluggedIn, IsCharging: charging, SecondsRemaining: secondsRemaining);

    private static readonly BatterySnapshot NoBattery =
        new(HasBattery: false, Percent: null, IsPluggedIn: true, IsCharging: false, SecondsRemaining: null);

    private static MetricsSnapshot Snapshot(BatterySnapshot battery) => new(
        Battery: battery,
        CpuPercent: 12.4,
        RamPercent: 61,
        DiskReadBytesPerSecond: 45.2 * KB,
        DiskWriteBytesPerSecond: 3.9 * MB,
        Network: new NetworkRate(UploadBytesPerSecond: 1.1 * KB, DownloadBytesPerSecond: 8.6 * KB));

    [Fact]
    public void 筆電_依序為電池_CPU與RAM_磁碟_網路四欄()
    {
        var columns = BarContent.Build(Snapshot(Battery(82)), VisibleItems.All);

        Assert.Equal(4, columns.Count);
        Assert.Equal(new BarCell("電量", "82%", Level.Good), columns[0].Top);
        Assert.Equal(new BarCell("CPU", "12%", Level.Good), columns[1].Top);
        Assert.Equal(new BarCell("RAM", "61%", Level.Good), columns[1].Bottom);
        Assert.Equal(new BarCell("讀取", "45.2KB", Level.Good), columns[2].Top);
        Assert.Equal(new BarCell("寫入", "3.9MB", Level.Good), columns[2].Bottom);
        Assert.Equal(new BarCell("上傳", "1.1KB", Level.Good), columns[3].Top);
        Assert.Equal(new BarCell("下載", "8.6KB", Level.Good), columns[3].Bottom);
    }

    [Fact]
    public void 桌機沒有電池_不會出現電池欄()
    {
        var columns = BarContent.Build(Snapshot(NoBattery), VisibleItems.All);

        Assert.Equal(3, columns.Count);
        Assert.Equal("CPU", columns[0].Top.Label);
    }

    [Fact]
    public void 電池下格_充電中()
    {
        var columns = BarContent.Build(Snapshot(Battery(40, pluggedIn: true, charging: true)), VisibleItems.All);

        Assert.Equal(new BarCell("電源", "充電中", Level.Good), columns[0].Bottom);
    }

    [Fact]
    public void 電池下格_接著電源但沒在充電()
    {
        var columns = BarContent.Build(Snapshot(Battery(100, pluggedIn: true)), VisibleItems.All);

        Assert.Equal(new BarCell("電源", "已接上", Level.Good), columns[0].Bottom);
    }

    [Fact]
    public void 電池下格_放電中顯示估計剩餘時間_顏色跟著電量()
    {
        var columns = BarContent.Build(Snapshot(Battery(15, secondsRemaining: 5400)), VisibleItems.All);

        Assert.Equal(new BarCell("電量", "15%", Level.Critical), columns[0].Top);
        Assert.Equal(new BarCell("剩餘", "1:30", Level.Critical), columns[0].Bottom);
    }

    [Fact]
    public void 電池下格_放電中但系統還沒估出剩餘時間()
    {
        var columns = BarContent.Build(Snapshot(Battery(82)), VisibleItems.All);

        Assert.Equal(new BarCell("電源", "電池", Level.Good), columns[0].Bottom);
    }

    [Fact]
    public void 關掉的項目不會出現()
    {
        var visible = new VisibleItems(Battery: false, BatteryDetail: false, CpuRam: true, Gpu: false, Disk: false, Network: true);
        var columns = BarContent.Build(Snapshot(Battery(82)), visible);

        Assert.Equal(2, columns.Count);
        Assert.Equal("CPU", columns[0].Top.Label);
        Assert.Equal("上傳", columns[1].Top.Label);
    }

    [Fact]
    public void 全部關掉時_至少保留CPU與RAM()
    {
        var columns = BarContent.Build(Snapshot(Battery(82)), new VisibleItems(false, false, false, false, false, false));

        Assert.Equal("CPU", Assert.Single(columns).Top.Label);
    }

    [Fact]
    public void 只勾電池但電腦沒有電池_改顯示CPU與RAM而不是空白()
    {
        var columns = BarContent.Build(Snapshot(NoBattery), new VisibleItems(true, true, false, false, false, false));

        Assert.Equal("CPU", Assert.Single(columns).Top.Label);
    }

    [Fact]
    public void 尚無數值_以兩個減號佔位且不帶警示等級()
    {
        var columns = BarContent.Build(MetricsSnapshot.Empty, VisibleItems.All);

        Assert.Equal(3, columns.Count);
        Assert.True(columns.All(c => c.Top.Value == "--" && c.Bottom.Value == "--"));
        Assert.True(columns.All(c => c.Top.Level == Level.Neutral && c.Bottom.Level == Level.Neutral));
    }

    [Fact]
    public void 數值超過門檻時帶有對應的警示等級()
    {
        var busy = new MetricsSnapshot(
            Battery: NoBattery,
            CpuPercent: 70,
            RamPercent: 95,
            DiskReadBytesPerSecond: 20 * MB,
            DiskWriteBytesPerSecond: 150 * MB,
            Network: new NetworkRate(UploadBytesPerSecond: 2 * MB, DownloadBytesPerSecond: 30 * MB));

        var columns = BarContent.Build(busy, VisibleItems.All);

        Assert.Equal(Level.Warn, columns[0].Top.Level);
        Assert.Equal(Level.Critical, columns[0].Bottom.Level);
        Assert.Equal(Level.Warn, columns[1].Top.Level);
        Assert.Equal(Level.Critical, columns[1].Bottom.Level);
        Assert.Equal(Level.Warn, columns[2].Top.Level);
        Assert.Equal(Level.Critical, columns[2].Bottom.Level);
    }

    [Fact]
    public void 有GPU數值時_GPU欄排在CPU之後()
    {
        var snapshot = Snapshot(NoBattery) with { GpuPercent = 35.2, GpuMemoryBytes = 3.2 * 1024 * MB };

        var columns = BarContent.Build(snapshot, VisibleItems.All);

        Assert.Equal(4, columns.Count);
        Assert.Equal("CPU", columns[0].Top.Label);
        Assert.Equal(new BarCell("GPU", "35%", Level.Good), columns[1].Top);
        Assert.Equal(new BarCell("顯存", "3.2GB", Level.Good), columns[1].Bottom);
    }

    [Fact]
    public void GPU使用率依門檻變色()
    {
        var snapshot = Snapshot(NoBattery) with { GpuPercent = 90, GpuMemoryBytes = 512 * MB };

        var columns = BarContent.Build(snapshot, VisibleItems.All);

        Assert.Equal(new BarCell("GPU", "90%", Level.Critical), columns[1].Top);
        Assert.Equal(new BarCell("顯存", "512MB", Level.Good), columns[1].Bottom);
    }

    [Fact]
    public void 這台電腦沒有GPU計數器_不會出現GPU欄()
    {
        var columns = BarContent.Build(Snapshot(NoBattery), VisibleItems.All);

        Assert.True(columns.All(c => c.Top.Label != "GPU"));
    }

    [Fact]
    public void 只讀得到顯示記憶體_GPU使用率以兩個減號佔位()
    {
        var snapshot = Snapshot(NoBattery) with { GpuMemoryBytes = 512 * MB };

        var columns = BarContent.Build(snapshot, VisibleItems.All);

        Assert.Equal(new BarCell("GPU", "--", Level.Neutral), columns[1].Top);
    }

    [Fact]
    public void 電池詳情欄_放電中顯示負的功耗與健康度()
    {
        var snapshot = Snapshot(Battery(82)) with { BatteryDetail = new BatteryDetail(PowerWatts: -12.34, HealthPercent: 91.4, SecondsToFull: null) };

        var columns = BarContent.Build(snapshot, VisibleItems.All);

        Assert.Equal(5, columns.Count);
        Assert.Equal("電量", columns[0].Top.Label);
        Assert.Equal(new BarCell("功耗", "-12.3W", Level.Good), columns[1].Top);
        Assert.Equal(new BarCell("健康", "91%", Level.Good), columns[1].Bottom);
        Assert.Equal("CPU", columns[2].Top.Label);
    }

    [Fact]
    public void 電池詳情欄_健康度偏低時變色_韌體沒回報的項目以減號佔位()
    {
        var snapshot = Snapshot(Battery(82)) with { BatteryDetail = new BatteryDetail(PowerWatts: null, HealthPercent: 55, SecondsToFull: null) };

        var columns = BarContent.Build(snapshot, VisibleItems.All);

        Assert.Equal(new BarCell("功耗", "--", Level.Neutral), columns[1].Top);
        Assert.Equal(new BarCell("健康", "55%", Level.Critical), columns[1].Bottom);
    }

    [Fact]
    public void 讀不到電池詳情_不會出現電池詳情欄()
    {
        var columns = BarContent.Build(Snapshot(Battery(82)), VisibleItems.All);

        Assert.True(columns.All(c => c.Top.Label != "功耗"));
    }

    [Fact]
    public void 充電中且估得出充滿時間_電池下格顯示充滿時間()
    {
        var snapshot = Snapshot(Battery(40, pluggedIn: true, charging: true))
            with { BatteryDetail = new BatteryDetail(PowerWatts: 45, HealthPercent: 95, SecondsToFull: 2700) };

        var columns = BarContent.Build(snapshot, VisibleItems.All);

        Assert.Equal(new BarCell("充滿", "0:45", Level.Good), columns[0].Bottom);
        Assert.Equal(new BarCell("功耗", "+45.0W", Level.Good), columns[1].Top);
    }

    [Theory]
    [InlineData(45.0, "+45.0W")]
    [InlineData(-12.34, "-12.3W")]
    [InlineData(0.0, "0.0W")]
    [InlineData(-0.04, "0.0W")]
    [InlineData(99.94, "+99.9W")]
    [InlineData(-120.4, "-120W")]
    public void 瓦數附正負號_未滿一百顯示一位小數(double watts, string expected)
    {
        Assert.Equal(expected, BarContent.Watts(watts));
    }

    [Fact]
    public void 沒有瓦數時以兩個減號代替()
    {
        Assert.Equal("--", BarContent.Watts(null));
    }

    [Fact]
    public void 系統匣提示文字_筆電()
    {
        Assert.Equal("電量 82% · CPU 12% · RAM 61% · ↑1.1KB ↓8.6KB", BarContent.TrayText(Snapshot(Battery(82))));
    }

    [Fact]
    public void 系統匣提示文字_桌機且尚無網路數值()
    {
        var snapshot = MetricsSnapshot.Empty with { CpuPercent = 5, RamPercent = 50 };

        Assert.Equal("CPU 5% · RAM 50%", BarContent.TrayText(snapshot));
    }

    [Fact]
    public void 系統匣提示文字不超過六十三個字元()
    {
        var worst = new MetricsSnapshot(
            Battery(100, pluggedIn: true),
            CpuPercent: 100,
            RamPercent: 100,
            DiskReadBytesPerSecond: 0,
            DiskWriteBytesPerSecond: 0,
            Network: new NetworkRate(1023.4 * KB, 1023.4 * KB));

        Assert.True(BarContent.TrayText(worst).Length <= 63);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(59, "0:00")]
    [InlineData(60, "0:01")]
    [InlineData(5400, "1:30")]
    [InlineData(36000, "10:00")]
    public void 剩餘時間以時分顯示(int seconds, string expected)
    {
        Assert.Equal(expected, BarContent.Duration(seconds));
    }
}
