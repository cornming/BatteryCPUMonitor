using BatteryCPUMonitor.Updates;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class SelfCheckTests
{
    [Fact]
    public void 報告包含版本與種類()
    {
        string report = SelfCheckRunner.BuildReport();

        Assert.Contains($"version={AppVersion.CurrentText}", report);
        Assert.Contains("variant=lite", report);
    }

    [Fact]
    public void 帶著自我檢查參數啟動_寫出報告並要求程式直接結束()
    {
        using var folder = new TempFolder();
        string file = folder.File("report.txt");

        bool handled = SelfCheckRunner.TryHandle([SelfCheckRunner.Argument, file], out int exitCode);

        Assert.True(handled);
        Assert.Equal(0, exitCode);
        Assert.Equal(SelfCheckRunner.BuildReport(), File.ReadAllText(file));
    }

    [Fact]
    public void 一般啟動不會被當成自我檢查()
    {
        Assert.False(SelfCheckRunner.TryHandle([], out _));
        Assert.False(SelfCheckRunner.TryHandle([UpdateRelauncher.AfterUpdateArgument], out _));
        Assert.False(SelfCheckRunner.TryHandle(["--other", "x"], out _));
    }

    [Fact]
    public void 沒有指定報告路徑_也算處理過了但不寫檔()
    {
        Assert.True(SelfCheckRunner.TryHandle([SelfCheckRunner.Argument], out int exitCode));
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void 報告寫不進去_回傳錯誤碼()
    {
        using var folder = new TempFolder();

        bool handled = SelfCheckRunner.TryHandle([SelfCheckRunner.Argument, folder.File(Path.Combine("no-such-folder", "report.txt"))], out int exitCode);

        Assert.True(handled);
        Assert.Equal(2, exitCode);
    }

    [Theory]
    [InlineData("version=2.6.0\nvariant=lite\n", "2.6.0", true)]
    [InlineData("variant=lite\nversion=2.6.0\n", "2.6.0", true)]
    [InlineData("version=2.6.0-dev\n", "2.6.0", true)]
    [InlineData("version=2.5.0\n", "2.6.0", false)]
    [InlineData("variant=lite\n", "2.6.0", false)]
    [InlineData("", "2.6.0", false)]
    [InlineData("version=不是版本\n", "2.6.0", false)]
    public void 報告裡的版本必須等於預期(string content, string expected, bool matches)
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.File("report.txt"), content);
        AppVersion.TryParse(expected, out Version version);

        Assert.Equal(matches, SelfCheckRunner.ReportMatches(folder.File("report.txt"), version));
    }

    [Fact]
    public void 沒有報告檔_視為不符()
    {
        using var folder = new TempFolder();

        Assert.False(SelfCheckRunner.ReportMatches(folder.File("missing.txt"), new Version(2, 6, 0)));
    }
}
