using Xunit;

namespace BatteryCPUMonitor.Tests;

public class ByteRateTests
{
    [Theory]
    [InlineData(0.0, "0.0KB")]
    [InlineData(512.0, "0.5KB")]
    [InlineData(1024.0, "1.0KB")]
    [InlineData(46285.0, "45.2KB")]
    [InlineData(102297.6, "99.9KB")]      // 99.9 KB
    [InlineData(102400.0, "100KB")]       // 100 KB 起改為整數
    [InlineData(1047961.6, "1023KB")]     // 1023.4 KB，仍以 KB 顯示
    [InlineData(1048576.0, "1.0MB")]      // 剛好 1 MB
    [InlineData(4089446.4, "3.9MB")]
    [InlineData(524288000.0, "500MB")]
    [InlineData(1073741824.0, "1.0GB")]
    public void 依大小選擇單位_未滿一百顯示一位小數(double bytesPerSecond, string expected)
    {
        Assert.Equal(expected, ByteRate.Format(bytesPerSecond));
    }

    [Fact]
    public void 沒有數值或數值不合理_以兩個減號代替()
    {
        Assert.Equal("--", ByteRate.Format(null));
        Assert.Equal("--", ByteRate.Format(-1));
        Assert.Equal("--", ByteRate.Format(double.NaN));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1047961.6)]
    [InlineData(1048064.0)]     // 1023.5 KB，進位到 MB
    [InlineData(104805171.2)]   // 99.95 MB
    [InlineData(1099511627776.0)]
    public void 字串最長六個字元_欄寬才不會跳動(double bytesPerSecond)
    {
        Assert.True(ByteRate.Format(bytesPerSecond).Length <= 6);
    }
}
