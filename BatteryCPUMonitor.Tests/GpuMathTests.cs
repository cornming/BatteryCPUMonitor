using BatteryCPUMonitor.Metrics;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class GpuMathTests
{
    private const string Card = "luid_0x00000000_0x0000ABCD_phys_0";
    private const string OtherCard = "luid_0x00000000_0x0000FFFF_phys_0";

    private static (string, double) Engine(int pid, string card, int engine, string type, double value) =>
        ($"pid_{pid}_{card}_eng_{engine}_engtype_{type}", value);

    [Fact]
    public void 同一個引擎上各程式的用量相加()
    {
        var samples = new[]
        {
            Engine(100, Card, 0, "3D", 20),
            Engine(200, Card, 0, "3D", 15.5),
        };

        Assert.Equal(35.5, GpuMath.Utilization(samples));
    }

    [Fact]
    public void 整體使用率取最忙的引擎_而不是全部相加()
    {
        var samples = new[]
        {
            Engine(100, Card, 0, "3D", 30),
            Engine(100, Card, 3, "VideoDecode", 70),
            Engine(200, Card, 5, "Copy", 5),
        };

        Assert.Equal(70.0, GpuMath.Utilization(samples));
    }

    [Fact]
    public void 多張顯示卡_取最忙的那一張()
    {
        var samples = new[]
        {
            Engine(100, Card, 0, "3D", 10),
            Engine(100, OtherCard, 0, "3D", 80),
        };

        Assert.Equal(80.0, GpuMath.Utilization(samples));
    }

    [Fact]
    public void 相加後超過一百_以一百為上限()
    {
        var samples = new[]
        {
            Engine(100, Card, 0, "3D", 70),
            Engine(200, Card, 0, "3D", 60),
        };

        Assert.Equal(100.0, GpuMath.Utilization(samples));
    }

    [Fact]
    public void 沒有任何資料_回傳空值()
    {
        Assert.Null(GpuMath.Utilization(null));
        Assert.Null(GpuMath.Utilization([]));
        Assert.Null(GpuMath.Utilization([("看不懂的名稱", 50.0)]));
    }

    [Theory]
    [InlineData("pid_1234_luid_0x00000000_0x0000ABCD_phys_0_eng_3_engtype_VideoDecode", "luid_0x00000000_0x0000ABCD_phys_0_eng_3")]
    [InlineData("pid_8_luid_0x00000000_0x0000ABCD_phys_0_eng_0_engtype_3D", "luid_0x00000000_0x0000ABCD_phys_0_eng_0")]
    [InlineData("luid_0x00000000_0x0000ABCD_phys_0_eng_1", "luid_0x00000000_0x0000ABCD_phys_0_eng_1")]
    public void 分組依據為顯示卡加引擎編號(string instance, string expected)
    {
        Assert.Equal(expected, GpuMath.EngineKey(instance));
    }

    [Fact]
    public void 顯示記憶體_多張顯示卡時取用量最大的()
    {
        var samples = new[] { (Card, 128.0 * 1024 * 1024), (OtherCard, 3.5 * 1024 * 1024 * 1024) };

        Assert.Equal(3.5 * 1024 * 1024 * 1024, GpuMath.DedicatedMemoryBytes(samples));
    }

    [Fact]
    public void 顯示記憶體_沒有資料回傳空值()
    {
        Assert.Null(GpuMath.DedicatedMemoryBytes(null));
        Assert.Null(GpuMath.DedicatedMemoryBytes([]));
    }
}
