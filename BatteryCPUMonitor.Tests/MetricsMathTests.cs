using BatteryCPUMonitor.Metrics;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class MetricsMathTests
{
    [Fact]
    public void CPU_閒置時間佔一半_使用率為五成()
    {
        // kernel 已包含 idle：kernel 600（其中 idle 500）+ user 400 = 總計 1000，忙碌 500。
        Assert.Equal(50.0, CpuMath.UsageFromDeltas(idleDelta: 500, kernelDelta: 600, userDelta: 400));
    }

    [Fact]
    public void CPU_完全閒置_使用率為零()
    {
        Assert.Equal(0.0, CpuMath.UsageFromDeltas(idleDelta: 1000, kernelDelta: 1000, userDelta: 0));
    }

    [Fact]
    public void CPU_完全滿載_使用率為一百()
    {
        Assert.Equal(100.0, CpuMath.UsageFromDeltas(idleDelta: 0, kernelDelta: 300, userDelta: 700));
    }

    [Fact]
    public void CPU_兩次取樣之間沒有時間差_回傳空值()
    {
        Assert.Null(CpuMath.UsageFromDeltas(idleDelta: 0, kernelDelta: 0, userDelta: 0));
    }

    [Fact]
    public void CPU_異常數值不會超出零到一百()
    {
        Assert.Equal(0.0, CpuMath.UsageFromDeltas(idleDelta: 2000, kernelDelta: 600, userDelta: 400));
    }

    [Fact]
    public void 記憶體_十六GB用掉四GB_使用率為二成五()
    {
        const ulong gb = 1024UL * 1024 * 1024;
        Assert.Equal(25.0, MemoryMath.UsedPercent(totalBytes: 16 * gb, availableBytes: 12 * gb));
    }

    [Fact]
    public void 記憶體_總量為零_回傳空值()
    {
        Assert.Null(MemoryMath.UsedPercent(totalBytes: 0, availableBytes: 0));
    }

    [Fact]
    public void 記憶體_可用量大於總量_視為零()
    {
        Assert.Equal(0.0, MemoryMath.UsedPercent(totalBytes: 100, availableBytes: 200));
    }
}
