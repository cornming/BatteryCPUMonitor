using System.Drawing;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class TaskbarPlacementTests
{
    // 1920x1080 螢幕。Windows 11 的工作列高 48，在最下方。
    private static readonly Rectangle Taskbar11 = new(0, 1032, 1920, 48);
    private static readonly Rectangle Tray11 = new(1700, 1032, 220, 48);

    [Fact]
    public void 橫式與直式工作列的判斷()
    {
        Assert.True(TaskbarPlacement.IsHorizontal(Taskbar11));
        Assert.False(TaskbarPlacement.IsHorizontal(new Rectangle(0, 0, 62, 1080)));
        Assert.False(TaskbarPlacement.IsHorizontal(Rectangle.Empty));
    }

    [Fact]
    public void Windows11_緊貼系統匣左邊_佔滿工作列高度()
    {
        Rectangle widget = TaskbarPlacement.Windows11(Taskbar11, Tray11, width: 400, gap: 6, extraAvoid: 0, fallbackTrayWidth: 200);

        // 右緣 = 系統匣左緣 1700 − 間距 6 = 1694 → 左緣 1294；座標是相對於工作列左上角。
        Assert.Equal(new Rectangle(1294, 0, 400, 48), widget);
    }

    [Fact]
    public void Windows11_工作列靠左對齊時_再往左避開小工具按鈕()
    {
        Rectangle widget = TaskbarPlacement.Windows11(Taskbar11, Tray11, width: 400, gap: 6, extraAvoid: 150, fallbackTrayWidth: 200);

        Assert.Equal(1144, widget.X);
    }

    [Fact]
    public void Windows11_找不到系統匣時_以預估寬度推算()
    {
        Rectangle widget = TaskbarPlacement.Windows11(Taskbar11, Rectangle.Empty, width: 400, gap: 6, extraAvoid: 0, fallbackTrayWidth: 200);

        // 假設系統匣從 1920 − 200 = 1720 開始。
        Assert.Equal(1314, widget.X);
    }

    [Fact]
    public void Windows11_副螢幕在左邊_座標仍是相對於工作列()
    {
        var taskbar = new Rectangle(-1920, 1032, 1920, 48);
        var tray = new Rectangle(-220, 1032, 220, 48);

        Rectangle widget = TaskbarPlacement.Windows11(taskbar, tray, width: 400, gap: 6, extraAvoid: 0, fallbackTrayWidth: 200);

        Assert.Equal(new Rectangle(1294, 0, 400, 48), widget);
    }

    [Fact]
    public void Windows11_內容比可用空間還寬_不會跑到工作列左邊界之外()
    {
        Rectangle widget = TaskbarPlacement.Windows11(Taskbar11, Tray11, width: 2000, gap: 6, extraAvoid: 0, fallbackTrayWidth: 200);

        Assert.Equal(0, widget.X);
    }

    // Windows 10：工作列高 40。容器（ReBarWindow32）從搜尋列之後到系統匣之前，工作清單佔滿容器。
    private static readonly Rectangle Container10 = new(400, 1040, 1200, 40);
    private static readonly Rectangle TaskList10 = new(400, 1040, 1200, 40);

    [Fact]
    public void Windows10_小工具放在容器最右邊_工作清單縮短讓出空間()
    {
        var placement = TaskbarPlacement.Windows10(Container10, TaskList10, width: 300, gap: 4);

        Assert.NotNull(placement);
        // 小工具從 1200 − 300 = 900 開始；工作清單寬度 = 900 − 間距 4 = 896。座標都相對於容器。
        Assert.Equal(new Rectangle(900, 0, 300, 40), placement.Value.Widget);
        Assert.Equal(new Rectangle(0, 0, 896, 40), placement.Value.TaskList);
    }

    [Fact]
    public void Windows10_工作清單不是從容器最左邊開始時_保留它原本的起點()
    {
        var taskList = new Rectangle(460, 1042, 1140, 36);

        var placement = TaskbarPlacement.Windows10(Container10, taskList, width: 300, gap: 4);

        Assert.NotNull(placement);
        Assert.Equal(new Rectangle(60, 2, 836, 36), placement.Value.TaskList);
    }

    [Fact]
    public void Windows10_已經縮短過_再算一次結果不變()
    {
        var squeezed = new Rectangle(400, 1040, 896, 40);

        var placement = TaskbarPlacement.Windows10(Container10, squeezed, width: 300, gap: 4);

        Assert.NotNull(placement);
        Assert.Equal(new Rectangle(0, 0, 896, 40), placement.Value.TaskList);
    }

    [Fact]
    public void Windows10_空間不夠時不動作()
    {
        Assert.Null(TaskbarPlacement.Windows10(Container10, TaskList10, width: 1300, gap: 4));
        Assert.Null(TaskbarPlacement.Windows10(Container10, TaskList10, width: 1198, gap: 4));
    }

    [Fact]
    public void Windows10_離開時把工作清單還原成佔滿容器()
    {
        var squeezed = new Rectangle(460, 1042, 836, 36);

        Assert.Equal(new Rectangle(60, 2, 1140, 36), TaskbarPlacement.Windows10Restore(Container10, squeezed));
    }

    [Theory]
    [InlineData(48, 9f)]   // Windows 11：兩列 15 像素 + 間距 1 = 31，放得下
    [InlineData(31, 9f)]   // 剛好放得下
    [InlineData(30, 8.5f)] // 9 點放不下，退一級（14 × 2 + 1 = 29）
    [InlineData(27, 8f)]   // 13 × 2 + 1 = 27
    [InlineData(10, 7f)]   // 都放不下時用最小的
    public void 挑出放得進工作列高度的最大字體(int availableHeight, float expected)
    {
        // 測試用的字高：9 點 15 像素、8.5 點 14、8 點 13、7.5 點 12、7 點 11。
        static int RowHeight(float points) => (int)(points * 2) - 3;

        float points = TaskbarPlacement.FitFontPoints([9f, 8.5f, 8f, 7.5f, 7f], RowHeight, rowGap: 1, availableHeight);

        Assert.Equal(expected, points);
    }
}
