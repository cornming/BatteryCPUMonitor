using Xunit;

namespace BatteryCPUMonitor.Tests;

public class AutoStartCommandTests
{
    private const string Exe = @"C:\Tools\Battery CPU\BatteryCPUMonitor.exe";

    [Fact]
    public void 指令會加上引號_路徑有空白也能執行()
    {
        Assert.Equal("\"" + Exe + "\"", AutoStartCommand.Build(Exe));
    }

    [Fact]
    public void 自己寫入的指令_判定為指向本程式()
    {
        Assert.True(AutoStartCommand.PointsTo(AutoStartCommand.Build(Exe), Exe));
    }

    [Fact]
    public void 比對時忽略大小寫與引號()
    {
        Assert.True(AutoStartCommand.PointsTo(Exe.ToUpperInvariant(), Exe));
    }

    [Fact]
    public void 指向別的路徑_判定為未啟用()
    {
        Assert.False(AutoStartCommand.PointsTo("\"C:\\Old\\BatteryCPUMonitor.exe\"", Exe));
    }

    [Fact]
    public void 登錄檔沒有值_判定為未啟用()
    {
        Assert.False(AutoStartCommand.PointsTo(null, Exe));
        Assert.False(AutoStartCommand.PointsTo("", Exe));
    }
}
