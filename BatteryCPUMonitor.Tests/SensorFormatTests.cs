using BatteryCPUMonitor.Metrics;
using BatteryCPUMonitor.Sensors;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class SensorFormatTests
{
    [Theory]
    [InlineData(62.4, "62°C")]
    [InlineData(62.5, "63°C")]
    [InlineData(100.0, "100°C")]
    [InlineData(5.0, "5°C")]
    public void 溫度四捨五入成整數(double celsius, string expected)
    {
        Assert.Equal(expected, SensorFormat.Temperature(celsius));
    }

    [Theory]
    [InlineData(45.24, "45.2W")]
    [InlineData(0.0, "0.0W")]
    [InlineData(99.94, "99.9W")]
    [InlineData(99.96, "100W")]
    [InlineData(250.4, "250W")]
    public void 功耗未滿一百瓦顯示一位小數(double watts, string expected)
    {
        Assert.Equal(expected, SensorFormat.Power(watts));
    }

    [Theory]
    [InlineData(3412.0, "3.4GHz")]
    [InlineData(1000.0, "1.0GHz")]
    [InlineData(999.6, "1.0GHz")]
    [InlineData(800.0, "800MHz")]
    [InlineData(4999.0, "5.0GHz")]
    public void 頻率一千MHz以上用GHz(double megahertz, string expected)
    {
        Assert.Equal(expected, SensorFormat.Clock(megahertz));
    }

    [Theory]
    [InlineData(1204.6, "1205rpm")]
    [InlineData(0.0, "0rpm")]
    public void 轉速(double rpm, string expected)
    {
        Assert.Equal(expected, SensorFormat.Fan(rpm));
    }

    [Fact]
    public void 沒有數值或數值不合理_以兩個減號代替()
    {
        Assert.Equal("--", SensorFormat.Temperature(null));
        Assert.Equal("--", SensorFormat.Temperature(double.NaN));
        Assert.Equal("--", SensorFormat.Power(null));
        Assert.Equal("--", SensorFormat.Power(-1));
        Assert.Equal("--", SensorFormat.Clock(null));
        Assert.Equal("--", SensorFormat.Clock(0));
        Assert.Equal("--", SensorFormat.Fan(null));
        Assert.Equal("--", SensorFormat.Fan(-3));
    }

    [Fact]
    public void 所有格式的字串都不超過範本寬度_欄寬不會跳動()
    {
        Assert.True(SensorFormat.Temperature(149.9).Length <= "100°C".Length + 1);
        Assert.True(SensorFormat.Power(99.94).Length <= "99.9W".Length);
        Assert.True(SensorFormat.Clock(9999).Length <= "4.2GHz".Length + 1);
        Assert.True(SensorFormat.Fan(9999).Length <= "9999rpm".Length);
    }

    // ---- CPU 頻率 ----

    [Theory]
    [InlineData(2400.0, 100.0, 2400.0)]
    [InlineData(2400.0, 150.0, 3600.0)]    // 渦輪加速時效能百分比超過 100
    [InlineData(2400.0, 33.0, 792.0)]
    public void 目前頻率等於標稱頻率乘以效能百分比(double nominal, double performance, double expected)
    {
        Assert.Equal(expected, CpuClock.CurrentMegahertz(nominal, performance)!.Value, 6);
    }

    [Theory]
    [InlineData(null, 100.0)]
    [InlineData(2400.0, null)]
    [InlineData(0.0, 100.0)]
    [InlineData(2400.0, 0.0)]
    [InlineData(2400.0, -10.0)]
    [InlineData(2400.0, double.NaN)]
    [InlineData(2400.0, 99999.0)]
    public void 缺資料或不合理_回傳空值(double? nominal, double? performance)
    {
        Assert.Null(CpuClock.CurrentMegahertz(nominal, performance));
    }
}
