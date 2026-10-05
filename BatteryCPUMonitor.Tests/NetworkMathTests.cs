using BatteryCPUMonitor.Metrics;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class NetworkMathTests
{
    private static Dictionary<string, (long Sent, long Received)> Totals(params (string Id, long Sent, long Received)[] nics) =>
        nics.ToDictionary(n => n.Id, n => (n.Sent, n.Received));

    [Fact]
    public void 加總各介面兩次取樣之間的流量差()
    {
        var before = Totals(("wifi", 1000, 5000), ("lan", 200, 300));
        var after = Totals(("wifi", 1500, 9000), ("lan", 250, 300));

        Assert.Equal((550L, 4000L), NetworkMath.SumDeltas(before, after));
    }

    [Fact]
    public void 中途新連上的介面_不會把過去累計的流量算進來()
    {
        var before = Totals(("wifi", 1000, 5000));
        var after = Totals(("wifi", 1100, 5200), ("vpn", 900000, 900000));

        Assert.Equal((100L, 200L), NetworkMath.SumDeltas(before, after));
    }

    [Fact]
    public void 介面消失_不影響其他介面的計算()
    {
        var before = Totals(("wifi", 1000, 5000), ("lan", 200, 300));
        var after = Totals(("lan", 260, 400));

        Assert.Equal((60L, 100L), NetworkMath.SumDeltas(before, after));
    }

    [Fact]
    public void 計數器歸零造成的負值_視為零()
    {
        var before = Totals(("wifi", 1000, 5000));
        var after = Totals(("wifi", 10, 6000));

        Assert.Equal((0L, 1000L), NetworkMath.SumDeltas(before, after));
    }

    [Fact]
    public void 流量差除以經過的秒數得到速度()
    {
        Assert.Equal(new NetworkRate(UploadBytesPerSecond: 500, DownloadBytesPerSecond: 2000), NetworkMath.ToRate(1000, 4000, seconds: 2));
    }

    [Fact]
    public void 沒有經過時間_回傳空值()
    {
        Assert.Null(NetworkMath.ToRate(1000, 4000, seconds: 0));
    }
}
