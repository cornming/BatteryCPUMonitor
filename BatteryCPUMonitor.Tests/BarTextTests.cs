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

    [Theory]
    [InlineData(0.0, 0, 255)]
    [InlineData(100.0, 255, 0)]
    [InlineData(50.0, 128, 127)]
    [InlineData(250.0, 255, 0)]
    public void 顏色隨CPU由綠轉紅(double cpu, int expectedRed, int expectedGreen)
    {
        Assert.Equal((expectedRed, expectedGreen, 0), BarText.ColorForCpu(cpu));
    }

    [Fact]
    public void 沒有CPU數值時為綠色()
    {
        Assert.Equal((0, 255, 0), BarText.ColorForCpu(null));
    }
}
