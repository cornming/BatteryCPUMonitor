using BatteryCPUMonitor.Metrics;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class BatterySnapshotTests
{
    private const uint UnknownLifeTime = 0xFFFFFFFF;

    [Fact]
    public void 放電中的筆電_有電量與剩餘時間()
    {
        var s = BatterySnapshot.FromRaw(acLineStatus: 0, batteryFlag: 0, lifePercent: 82, lifeTimeSeconds: 5400);

        Assert.True(s.HasBattery);
        Assert.Equal(82, s.Percent);
        Assert.False(s.IsPluggedIn);
        Assert.False(s.IsCharging);
        Assert.Equal(5400, s.SecondsRemaining);
    }

    [Fact]
    public void 充電中的筆電_標記為充電中且沒有剩餘時間()
    {
        var s = BatterySnapshot.FromRaw(acLineStatus: 1, batteryFlag: 8, lifePercent: 40, lifeTimeSeconds: UnknownLifeTime);

        Assert.True(s.HasBattery);
        Assert.True(s.IsPluggedIn);
        Assert.True(s.IsCharging);
        Assert.Null(s.SecondsRemaining);
    }

    [Fact]
    public void 接著電源但已充飽_不算充電中()
    {
        var s = BatterySnapshot.FromRaw(acLineStatus: 1, batteryFlag: 1, lifePercent: 100, lifeTimeSeconds: UnknownLifeTime);

        Assert.True(s.HasBattery);
        Assert.True(s.IsPluggedIn);
        Assert.False(s.IsCharging);
    }

    [Fact]
    public void 桌機沒有電池_不會把255當成電量()
    {
        var s = BatterySnapshot.FromRaw(acLineStatus: 1, batteryFlag: 128, lifePercent: 255, lifeTimeSeconds: UnknownLifeTime);

        Assert.False(s.HasBattery);
        Assert.Null(s.Percent);
        Assert.False(s.IsCharging);
    }

    [Fact]
    public void 狀態全部未知_視為沒有電池()
    {
        var s = BatterySnapshot.FromRaw(acLineStatus: 255, batteryFlag: 255, lifePercent: 255, lifeTimeSeconds: UnknownLifeTime);

        Assert.False(s.HasBattery);
        Assert.False(s.IsPluggedIn);
        Assert.False(s.IsCharging);
    }
}
