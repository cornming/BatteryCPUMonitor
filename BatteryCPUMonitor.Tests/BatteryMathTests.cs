using BatteryCPUMonitor.Metrics;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class BatteryMathTests
{
    private const uint Unknown = BatteryMath.UnknownCapacity;

    private static (uint, uint, uint, int)[] One(uint designed, uint full, uint remaining, int rate) =>
        [(designed, full, remaining, rate)];

    [Fact]
    public void 健康度為滿充容量除以設計容量()
    {
        BatteryDetail? detail = BatteryMath.Combine(One(50000, 45000, 30000, -12000), pluggedIn: false, charging: false);

        Assert.Equal(90.0, detail!.Value.HealthPercent);
    }

    [Fact]
    public void 新電池的滿充容量可能略高於設計容量_健康度以一百為上限()
    {
        BatteryDetail? detail = BatteryMath.Combine(One(50000, 51000, 30000, 0), pluggedIn: true, charging: false);

        Assert.Equal(100.0, detail!.Value.HealthPercent);
    }

    [Fact]
    public void 使用電池時功率一律為負_不管驅動程式回報的符號()
    {
        Assert.Equal(-12.0, BatteryMath.Combine(One(50000, 45000, 30000, -12000), pluggedIn: false, charging: false)!.Value.PowerWatts);
        Assert.Equal(-12.0, BatteryMath.Combine(One(50000, 45000, 30000, 12000), pluggedIn: false, charging: false)!.Value.PowerWatts);
    }

    [Fact]
    public void 充電中功率一律為正_不管驅動程式回報的符號()
    {
        Assert.Equal(45.0, BatteryMath.Combine(One(50000, 45000, 30000, 45000), pluggedIn: true, charging: true)!.Value.PowerWatts);
        Assert.Equal(45.0, BatteryMath.Combine(One(50000, 45000, 30000, -45000), pluggedIn: true, charging: true)!.Value.PowerWatts);
    }

    [Fact]
    public void 充電中_依剩餘容量與充電功率估計充滿時間()
    {
        // 還差 15000 毫瓦時，以 30000 毫瓦充電 → 半小時。
        BatteryDetail? detail = BatteryMath.Combine(One(50000, 45000, 30000, 30000), pluggedIn: true, charging: true);

        Assert.Equal(1800, detail!.Value.SecondsToFull);
    }

    [Fact]
    public void 沒在充電_不估計充滿時間()
    {
        Assert.Null(BatteryMath.Combine(One(50000, 45000, 30000, -12000), pluggedIn: false, charging: false)!.Value.SecondsToFull);
        Assert.Null(BatteryMath.Combine(One(50000, 45000, 45000, 0), pluggedIn: true, charging: false)!.Value.SecondsToFull);
    }

    [Fact]
    public void 韌體沒回報功率_功耗與充滿時間為空值_健康度仍可計算()
    {
        BatteryDetail? detail = BatteryMath.Combine(One(50000, 45000, 30000, BatteryMath.UnknownRate), pluggedIn: true, charging: true);

        Assert.Null(detail!.Value.PowerWatts);
        Assert.Null(detail.Value.SecondsToFull);
        Assert.Equal(90.0, detail.Value.HealthPercent);
    }

    [Fact]
    public void 韌體沒回報容量_健康度為空值_功耗仍可顯示()
    {
        BatteryDetail? detail = BatteryMath.Combine(One(Unknown, Unknown, Unknown, -8000), pluggedIn: false, charging: false);

        Assert.Null(detail!.Value.HealthPercent);
        Assert.Equal(-8.0, detail.Value.PowerWatts);
    }

    [Fact]
    public void 兩顆電池_容量與功率各自加總()
    {
        (uint, uint, uint, int)[] two = [(40000, 36000, 20000, -6000), (20000, 18000, 10000, -4000)];

        BatteryDetail? detail = BatteryMath.Combine(two, pluggedIn: false, charging: false);

        Assert.Equal(90.0, detail!.Value.HealthPercent);
        Assert.Equal(-10.0, detail.Value.PowerWatts);
    }

    [Fact]
    public void 沒有電池_回傳空值()
    {
        Assert.Null(BatteryMath.Combine([], pluggedIn: true, charging: false));
    }
}
