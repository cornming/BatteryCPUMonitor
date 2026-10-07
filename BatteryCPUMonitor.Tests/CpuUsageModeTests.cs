using BatteryCPUMonitor.Metrics;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class CpuUsageModeTests
{
    [Theory]
    [InlineData("time", CpuUsageMode.Time)]
    [InlineData("utility", CpuUsageMode.Utility)]
    [InlineData("UTILITY", CpuUsageMode.Utility)]
    [InlineData("  utility ", CpuUsageMode.Utility)]
    [InlineData("Time", CpuUsageMode.Time)]
    public void 設定檔裡的文字可以解析(string text, CpuUsageMode expected)
    {
        Assert.Equal(expected, CpuUsageModes.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("fast")]
    [InlineData("0")]
    public void 看不懂的值一律當作預設的忙碌時間(string text)
    {
        Assert.Equal(CpuUsageMode.Time, CpuUsageModes.Parse(text));
    }

    [Fact]
    public void 空值也當作預設()
    {
        Assert.Equal(CpuUsageMode.Time, CpuUsageModes.Parse(null));
    }

    [Theory]
    [InlineData(CpuUsageMode.Time)]
    [InlineData(CpuUsageMode.Utility)]
    public void 轉成文字再解析回來_結果不變(CpuUsageMode mode)
    {
        Assert.Equal(mode, CpuUsageModes.Parse(CpuUsageModes.ToText(mode)));
    }

    [Fact]
    public void 依忙碌時間時_採用時間算出的數值()
    {
        Assert.Equal(60.0, CpuUsageModes.Choose(CpuUsageMode.Time, utility: 87, time: 60));
    }

    [Fact]
    public void 依處理器效能時_採用效能計數器的數值()
    {
        Assert.Equal(87.0, CpuUsageModes.Choose(CpuUsageMode.Utility, utility: 87, time: 60));
    }

    [Fact]
    public void 選定的算法暫時沒有數值_改用另一種_不顯示空白()
    {
        Assert.Equal(87.0, CpuUsageModes.Choose(CpuUsageMode.Time, utility: 87, time: null));
        Assert.Equal(60.0, CpuUsageModes.Choose(CpuUsageMode.Utility, utility: null, time: 60));
    }

    [Fact]
    public void 兩種都沒有數值_回傳空值()
    {
        Assert.Null(CpuUsageModes.Choose(CpuUsageMode.Time, null, null));
        Assert.Null(CpuUsageModes.Choose(CpuUsageMode.Utility, null, null));
    }

    [Fact]
    public void 數值為零是有效的數值_不會被當成沒有()
    {
        Assert.Equal(0.0, CpuUsageModes.Choose(CpuUsageMode.Time, utility: 5, time: 0));
        Assert.Equal(0.0, CpuUsageModes.Choose(CpuUsageMode.Utility, utility: 0, time: 5));
    }
}
