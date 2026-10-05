using BatteryCPUMonitor.Metrics;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class ThresholdsTests
{
    private static BatterySnapshot Battery(int percent, bool pluggedIn = false, bool charging = false) =>
        new(HasBattery: true, Percent: percent, IsPluggedIn: pluggedIn, IsCharging: charging, SecondsRemaining: null);

    [Theory]
    [InlineData(0.0, Level.Good)]
    [InlineData(59.9, Level.Good)]
    [InlineData(60.0, Level.Warn)]
    [InlineData(84.9, Level.Warn)]
    [InlineData(85.0, Level.Critical)]
    [InlineData(100.0, Level.Critical)]
    public void CPU門檻_六十轉黃_八十五轉紅(double percent, Level expected)
    {
        Assert.Equal(expected, Thresholds.ForCpu(percent));
    }

    [Theory]
    [InlineData(79.9, Level.Good)]
    [InlineData(80.0, Level.Warn)]
    [InlineData(89.9, Level.Warn)]
    [InlineData(90.0, Level.Critical)]
    public void RAM門檻_八十轉黃_九十轉紅(double percent, Level expected)
    {
        Assert.Equal(expected, Thresholds.ForRam(percent));
    }

    [Theory]
    [InlineData(0.0, Level.Good)]
    [InlineData(10485759.0, Level.Good)]      // 差 1 位元組到 10 MB/s
    [InlineData(10485760.0, Level.Warn)]      // 10 MB/s
    [InlineData(104857600.0, Level.Critical)] // 100 MB/s
    public void 磁碟門檻_每秒十MB轉黃_一百MB轉紅(double bytesPerSecond, Level expected)
    {
        Assert.Equal(expected, Thresholds.ForDiskRate(bytesPerSecond));
    }

    [Theory]
    [InlineData(0.0, Level.Good)]
    [InlineData(1048575.0, Level.Good)]       // 差 1 位元組到 1 MB/s
    [InlineData(1048576.0, Level.Warn)]       // 1 MB/s
    [InlineData(10485760.0, Level.Critical)]  // 10 MB/s
    public void 網路門檻_每秒一MB轉黃_十MB轉紅(double bytesPerSecond, Level expected)
    {
        Assert.Equal(expected, Thresholds.ForNetworkRate(bytesPerSecond));
    }

    [Theory]
    [InlineData(59.9, Level.Good)]
    [InlineData(60.0, Level.Warn)]
    [InlineData(85.0, Level.Critical)]
    public void GPU門檻_六十轉黃_八十五轉紅(double percent, Level expected)
    {
        Assert.Equal(expected, Thresholds.ForGpu(percent));
    }

    [Theory]
    [InlineData(100.0, Level.Good)]
    [InlineData(80.0, Level.Good)]
    [InlineData(79.9, Level.Warn)]
    [InlineData(60.0, Level.Warn)]
    [InlineData(59.9, Level.Critical)]
    public void 電池健康度門檻_低於八十轉黃_低於六十轉紅(double percent, Level expected)
    {
        Assert.Equal(expected, Thresholds.ForBatteryHealth(percent));
    }

    [Fact]
    public void 沒有數值時不帶警示等級()
    {
        Assert.Equal(Level.Neutral, Thresholds.ForGpu(null));
        Assert.Equal(Level.Neutral, Thresholds.ForBatteryHealth(null));
        Assert.Equal(Level.Neutral, Thresholds.ForCpu(null));
        Assert.Equal(Level.Neutral, Thresholds.ForRam(null));
        Assert.Equal(Level.Neutral, Thresholds.ForDiskRate(null));
        Assert.Equal(Level.Neutral, Thresholds.ForNetworkRate(null));
    }

    [Theory]
    [InlineData(100, Level.Good)]
    [InlineData(41, Level.Good)]
    [InlineData(40, Level.Warn)]
    [InlineData(21, Level.Warn)]
    [InlineData(20, Level.Critical)]
    [InlineData(1, Level.Critical)]
    public void 電量門檻_放電中四十轉黃_二十轉紅(int percent, Level expected)
    {
        Assert.Equal(expected, Thresholds.ForBattery(Battery(percent)));
    }

    [Fact]
    public void 電量很低但接著電源_仍視為正常()
    {
        Assert.Equal(Level.Good, Thresholds.ForBattery(Battery(5, pluggedIn: true, charging: true)));
        Assert.Equal(Level.Good, Thresholds.ForBattery(Battery(5, pluggedIn: true)));
    }

    [Fact]
    public void 沒有電池時不帶警示等級()
    {
        var none = new BatterySnapshot(HasBattery: false, Percent: null, IsPluggedIn: true, IsCharging: false, SecondsRemaining: null);

        Assert.Equal(Level.Neutral, Thresholds.ForBattery(none));
    }
}
