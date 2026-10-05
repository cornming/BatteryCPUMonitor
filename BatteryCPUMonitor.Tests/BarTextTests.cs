using BatteryCPUMonitor.Metrics;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class BarTextTests
{
    private static BatterySnapshot Battery(int percent, bool pluggedIn = false, bool charging = false) =>
        new(HasBattery: true, Percent: percent, IsPluggedIn: pluggedIn, IsCharging: charging, SecondsRemaining: null);

    private static readonly BatterySnapshot NoBattery =
        new(HasBattery: false, Percent: null, IsPluggedIn: true, IsCharging: false, SecondsRemaining: null);

    [Fact]
    public void 放電中_只顯示電量()
    {
        Assert.Equal("電量:82% / CPU:12% / RAM:61%", BarText.Format(Battery(82), 12, 61));
    }

    [Fact]
    public void 充電中_加上充電中字樣()
    {
        Assert.Equal("電量:40% / 充電中 / CPU:5% / RAM:50%", BarText.Format(Battery(40, pluggedIn: true, charging: true), 5, 50));
    }

    [Fact]
    public void 接著電源但沒在充電_顯示已接電源()
    {
        Assert.Equal("電量:100% / 已接電源 / CPU:5% / RAM:50%", BarText.Format(Battery(100, pluggedIn: true), 5, 50));
    }

    [Fact]
    public void 沒有電池_不顯示電量區塊()
    {
        Assert.Equal("CPU:30% / RAM:45%", BarText.Format(NoBattery, 30, 45));
    }

    [Fact]
    public void 數值還沒準備好_以兩個減號代替()
    {
        Assert.Equal("CPU:--% / RAM:--%", BarText.Format(NoBattery, null, null));
    }

    [Theory]
    [InlineData(12.4, "CPU:12%")]
    [InlineData(12.5, "CPU:13%")]
    [InlineData(99.6, "CPU:100%")]
    [InlineData(0.0, "CPU:0%")]
    public void 百分比四捨五入成整數(double cpu, string expectedPrefix)
    {
        Assert.StartsWith(expectedPrefix + " / ", BarText.Format(NoBattery, cpu, 50));
    }

    [Fact]
    public void 各段文字帶有各自的警示等級()
    {
        // 電量 15% 放電中 → 紅；CPU 70% → 黃；RAM 50% → 綠。
        var segments = BarText.Build(Battery(15), 70, 50);

        Assert.Equal(Level.Critical, segments.Single(s => s.Text == "電量:15%").Level);
        Assert.Equal(Level.Warn, segments.Single(s => s.Text == "CPU:70%").Level);
        Assert.Equal(Level.Good, segments.Single(s => s.Text == "RAM:50%").Level);
    }

    [Fact]
    public void 分隔符號不帶警示等級()
    {
        var segments = BarText.Build(Battery(15), 99, 99);

        Assert.True(segments.Where(s => s.Text == " / ").All(s => s.Level == Level.Neutral));
        Assert.Equal(2, segments.Count(s => s.Text == " / "));
    }

    [Fact]
    public void 充電狀態文字與電量同色()
    {
        var segments = BarText.Build(Battery(10, pluggedIn: true, charging: true), 5, 50);

        Assert.Equal(Level.Good, segments.Single(s => s.Text == "電量:10%").Level);
        Assert.Equal(Level.Good, segments.Single(s => s.Text == "充電中").Level);
    }

    [Fact]
    public void 尚無數值的項目不帶警示等級()
    {
        var segments = BarText.Build(NoBattery, null, null);

        Assert.True(segments.All(s => s.Level == Level.Neutral));
    }

    [Fact]
    public void 各段文字串起來等於整行文字()
    {
        var battery = Battery(82, pluggedIn: true);

        Assert.Equal(BarText.Format(battery, 12, 61), BarText.Plain(BarText.Build(battery, 12, 61)));
    }
}
