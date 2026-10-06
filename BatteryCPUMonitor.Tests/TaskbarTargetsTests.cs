using System.Drawing;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class TaskbarTargetsTests
{
    private static readonly ScreenInfo Main = new(@"\\.\DISPLAY1", new Rectangle(0, 0, 1920, 1080), IsPrimary: true);
    private static readonly ScreenInfo Right = new(@"\\.\DISPLAY2", new Rectangle(1920, 0, 2560, 1440), IsPrimary: false);
    private static readonly ScreenInfo Left = new(@"\\.\DISPLAY3", new Rectangle(-1280, 0, 1280, 1024), IsPrimary: false);

    private static readonly ScreenInfo[] All = [Main, Right, Left];

    // ---- 決定實際使用哪個螢幕 ----

    [Fact]
    public void 沒有指定_使用主螢幕()
    {
        Assert.Equal(Main, TaskbarTargets.Resolve(null, All));
    }

    [Fact]
    public void 指定的螢幕有接上_就用它()
    {
        Assert.Equal(Right, TaskbarTargets.Resolve(@"\\.\DISPLAY2", All));
    }

    [Fact]
    public void 裝置名稱比對不分大小寫()
    {
        Assert.Equal(Right, TaskbarTargets.Resolve(@"\\.\display2", All));
    }

    [Fact]
    public void 指定的螢幕被拔掉_暫時改用主螢幕()
    {
        Assert.Equal(Main, TaskbarTargets.Resolve(@"\\.\DISPLAY2", [Main]));
    }

    [Fact]
    public void 主螢幕不是第一個螢幕_仍然選到主螢幕()
    {
        var secondListedButPrimary = Right with { IsPrimary = true };
        var notPrimary = Main with { IsPrimary = false };

        Assert.Equal(secondListedButPrimary, TaskbarTargets.Resolve(null, [notPrimary, secondListedButPrimary]));
    }

    [Fact]
    public void 沒有任何螢幕資料_不會當掉()
    {
        ScreenInfo none = TaskbarTargets.Resolve(@"\\.\DISPLAY2", []);

        Assert.True(none.IsPrimary);
        Assert.Equal(string.Empty, none.DeviceName);
    }

    // ---- 副螢幕的工作列屬於哪個螢幕 ----

    [Fact]
    public void 每個螢幕底部各有一條工作列_各自對應到自己的()
    {
        Rectangle[] taskbars =
        [
            new(1920, 1392, 2560, 48),   // 右邊螢幕的底部
            new(-1280, 984, 1280, 40),   // 左邊螢幕的底部
        ];

        Assert.Equal(0, TaskbarTargets.IndexOfTaskbarOn(Right.Bounds, taskbars));
        Assert.Equal(1, TaskbarTargets.IndexOfTaskbarOn(Left.Bounds, taskbars));
    }

    [Fact]
    public void 這個螢幕沒有顯示工作列_回傳空值()
    {
        Rectangle[] onlyLeft = [new(-1280, 984, 1280, 40)];

        Assert.Null(TaskbarTargets.IndexOfTaskbarOn(Right.Bounds, onlyLeft));
        Assert.Null(TaskbarTargets.IndexOfTaskbarOn(Right.Bounds, []));
    }

    [Fact]
    public void 工作列剛好貼在螢幕邊緣但沒有重疊_不算在這個螢幕上()
    {
        // 左邊螢幕的工作列右緣剛好是 0，與主螢幕左緣相接但沒有重疊。
        Rectangle[] taskbars = [new(-1280, 984, 1280, 40)];

        Assert.Null(TaskbarTargets.IndexOfTaskbarOn(Main.Bounds, taskbars));
    }

    [Fact]
    public void 工作列橫跨兩個螢幕邊界_歸給重疊較多的那個()
    {
        // 異常的幾何：一條工作列有 90% 在右螢幕、10% 在主螢幕。
        Rectangle[] taskbars = [new(1920 - 100, 1000, 1000, 40)];

        Assert.Equal(0, TaskbarTargets.IndexOfTaskbarOn(Right.Bounds, taskbars));
    }

    // ---- 選單文字 ----

    [Theory]
    [InlineData(@"\\.\DISPLAY1", 9, 1)]
    [InlineData(@"\\.\DISPLAY12", 9, 12)]
    [InlineData(@"\\.\DISPLAY2", 9, 2)]
    [InlineData("看不懂的名稱", 4, 4)]
    [InlineData("", 4, 4)]
    [InlineData(@"\\.\DISPLAY", 5, 5)]
    public void 螢幕編號取自裝置名稱結尾的數字(string deviceName, int fallback, int expected)
    {
        Assert.Equal(expected, TaskbarTargets.DisplayNumber(deviceName, fallback));
    }

    [Fact]
    public void 選單文字_主螢幕加註主要()
    {
        Assert.Equal("螢幕 1（主要）　1920×1080", TaskbarTargets.Label(Main, 1, hasTaskbar: true));
    }

    [Fact]
    public void 選單文字_副螢幕顯示解析度()
    {
        Assert.Equal("螢幕 2　2560×1440", TaskbarTargets.Label(Right, 2, hasTaskbar: true));
    }

    [Fact]
    public void 選單文字_沒有工作列的螢幕加註原因()
    {
        Assert.Equal("螢幕 2　2560×1440（沒有工作列）", TaskbarTargets.Label(Right, 2, hasTaskbar: false));
    }

    // ---- 選單的各個選項 ----

    [Fact]
    public void 浮動橫條時_沒有任何螢幕打勾()
    {
        var choices = TaskbarTargets.Choices(All, _ => true, taskbarMode: false, preferredDevice: @"\\.\DISPLAY2");

        Assert.True(choices.All(c => !c.Checked));
    }

    [Fact]
    public void 嵌入中_只有目前使用的螢幕打勾()
    {
        var choices = TaskbarTargets.Choices(All, _ => true, taskbarMode: true, preferredDevice: @"\\.\DISPLAY2");

        Assert.Equal(["螢幕 2　2560×1440"], choices.Where(c => c.Checked).Select(c => c.Label));
    }

    [Fact]
    public void 嵌入中且沒有指定螢幕_主螢幕打勾()
    {
        var choices = TaskbarTargets.Choices(All, _ => true, taskbarMode: true, preferredDevice: null);

        Assert.Equal([Main], choices.Where(c => c.Checked).Select(c => c.Screen));
    }

    [Fact]
    public void 指定的螢幕被拔掉_改由主螢幕打勾()
    {
        var choices = TaskbarTargets.Choices([Main, Left], _ => true, taskbarMode: true, preferredDevice: @"\\.\DISPLAY2");

        Assert.Equal([Main], choices.Where(c => c.Checked).Select(c => c.Screen));
    }

    [Fact]
    public void 沒有工作列的螢幕不能選_其他的可以()
    {
        var choices = TaskbarTargets.Choices(All, screen => screen != Right, taskbarMode: false, preferredDevice: null);

        Assert.Equal([true, false, true], choices.Select(c => c.Enabled));
        Assert.EndsWith("（沒有工作列）", choices[1].Label);
    }

    [Fact]
    public void 每個螢幕各有一個選項_順序與螢幕清單一致()
    {
        var choices = TaskbarTargets.Choices(All, _ => true, taskbarMode: true, preferredDevice: null);

        Assert.Equal(All, choices.Select(c => c.Screen));
    }

    // ---- 存進設定的值 ----

    [Fact]
    public void 選主螢幕存空值_選副螢幕存裝置名稱()
    {
        Assert.Null(TaskbarTargets.SettingFor(Main));
        Assert.Equal(@"\\.\DISPLAY2", TaskbarTargets.SettingFor(Right));
    }
}
