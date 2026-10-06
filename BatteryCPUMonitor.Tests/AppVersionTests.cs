using BatteryCPUMonitor.Updates;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("v2.5.0", 2, 5, 0)]
    [InlineData("2.5.0", 2, 5, 0)]
    [InlineData("V10.20.30", 10, 20, 30)]
    [InlineData("2.6.0-dev", 2, 6, 0)]
    [InlineData("2.6.0+abc123", 2, 6, 0)]
    [InlineData("0.0.0-dev", 0, 0, 0)]
    [InlineData("2.5", 2, 5, 0)]
    [InlineData("  v2.5.0  ", 2, 5, 0)]
    public void 各種版本文字都能解析(string text, int major, int minor, int build)
    {
        Assert.True(AppVersion.TryParse(text, out Version version));
        Assert.Equal(new Version(major, minor, build, 0), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("latest")]
    [InlineData("v")]
    [InlineData("2")]
    [InlineData("a.b.c")]
    public void 不是版本的文字不能解析(string text)
    {
        Assert.False(AppVersion.TryParse(text, out _));
    }

    [Fact]
    public void 空值不能解析()
    {
        Assert.False(AppVersion.TryParse(null, out _));
    }

    [Fact]
    public void 兩位版本與三位版本視為相同()
    {
        AppVersion.TryParse("2.5", out Version a);
        AppVersion.TryParse("2.5.0", out Version b);

        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData("2.10.0", "2.9.0", true)]
    [InlineData("2.5.1", "2.5.0", true)]
    [InlineData("3.0.0", "2.99.99", true)]
    [InlineData("2.5.0", "2.5.0", false)]
    [InlineData("2.4.9", "2.5.0", false)]
    [InlineData("2.5.0", "2.5.0-dev", false)]
    public void 版本新舊要依數字比較而不是依字串(string latest, string current, bool expected)
    {
        AppVersion.TryParse(latest, out Version l);
        AppVersion.TryParse(current, out Version c);

        Assert.Equal(expected, UpdatePolicy.IsNewer(l, c));
    }

    [Fact]
    public void 三位與四位的相同版本視為同一版_不會被當成新版本()
    {
        Assert.False(UpdatePolicy.IsNewer(new Version(2, 6, 0, 0), new Version(2, 6, 0)));
        Assert.False(UpdatePolicy.IsNewer(new Version(2, 6, 0), new Version(2, 6, 0, 0)));
        Assert.True(UpdatePolicy.IsNewer(new Version(2, 6, 1), new Version(2, 6, 0, 0)));
    }

    [Fact]
    public void 顯示用的版本只有三位()
    {
        Assert.Equal("2.5.0", AppVersion.Display(new Version(2, 5, 0, 0)));
    }

    [Theory]
    [InlineData("0.0.0", true)]
    [InlineData("0.0.0-dev", true)]
    [InlineData("2.5.0", false)]
    [InlineData("0.1.0", false)]
    public void 沒有正式版號的是開發版(string text, bool expected)
    {
        AppVersion.TryParse(text, out Version version);

        Assert.Equal(expected, UpdatePolicy.IsDevelopment(version));
    }

    [Theory]
    [InlineData(@"C:\Tools\BatteryCPUMonitor.exe", true)]
    [InlineData(@"C:\Users\me\Downloads\BatteryCPUMonitor-v2.5.0-standalone.exe", true)]
    [InlineData(@"c:\tools\batterycpumonitor.EXE", true)]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe", false)]
    [InlineData(@"C:\Users\me\.nuget\testhost.exe", false)]
    [InlineData(@"C:\Tools\BatteryCPUMonitor.dll", false)]
    [InlineData(@"C:\Tools\SomethingElse.exe", false)]
    [InlineData("", false)]
    public void 只有下載來的那個exe才能替換自己(string path, bool expected)
    {
        Assert.Equal(expected, UpdatePolicy.CanSelfUpdate(path));
    }

    [Fact]
    public void 沒有行程路徑時不能替換()
    {
        Assert.False(UpdatePolicy.CanSelfUpdate(null));
    }

    [Fact]
    public void 測試程式本身是精簡版()
    {
        Assert.Equal(AppVariant.Lite, AppVariantInfo.Current);
        Assert.Equal("lite", AppVariantInfo.Name(AppVariant.Lite));
        Assert.Equal("standalone", AppVariantInfo.Name(AppVariant.Standalone));
    }
}
