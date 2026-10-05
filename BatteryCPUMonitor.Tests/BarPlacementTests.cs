using System.Drawing;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class BarPlacementTests
{
    // 1920x1080 螢幕，底部工作列高 40。
    private static readonly Rectangle WorkingArea = new(0, 0, 1920, 1040);

    [Fact]
    public void 預設錨點在工作區底邊正中央()
    {
        Assert.Equal(new Point(960, 1040), BarPlacement.DefaultAnchor(WorkingArea));
    }

    [Fact]
    public void 預設位置_水平置中且貼齊工作區底邊()
    {
        Point topLeft = BarPlacement.TopLeftFor(BarPlacement.DefaultAnchor(WorkingArea), new Size(200, 20), WorkingArea);

        Assert.Equal(new Point(860, 1020), topLeft);
    }

    [Fact]
    public void 放大時以同一個錨點為中心_底邊不動()
    {
        Point anchor = BarPlacement.DefaultAnchor(WorkingArea);
        var small = new Rectangle(BarPlacement.TopLeftFor(anchor, new Size(200, 20), WorkingArea), new Size(200, 20));
        var large = new Rectangle(BarPlacement.TopLeftFor(anchor, new Size(600, 60), WorkingArea), new Size(600, 60));

        Assert.Equal(small.Bottom, large.Bottom);
        Assert.True(large.Contains(small));
    }

    [Fact]
    public void 靠近右下角時_整條仍留在工作區內()
    {
        var size = new Size(600, 60);
        Point topLeft = BarPlacement.TopLeftFor(new Point(1900, 1040), size, WorkingArea);

        Assert.Equal(new Point(1320, 980), topLeft);
    }

    [Fact]
    public void 靠近左上角時_整條仍留在工作區內()
    {
        Point topLeft = BarPlacement.TopLeftFor(new Point(10, 5), new Size(200, 20), WorkingArea);

        Assert.Equal(new Point(0, 0), topLeft);
    }

    [Fact]
    public void 副螢幕在左邊時_座標可以是負的()
    {
        var leftScreen = new Rectangle(-1920, 0, 1920, 1040);
        Point topLeft = BarPlacement.TopLeftFor(BarPlacement.DefaultAnchor(leftScreen), new Size(200, 20), leftScreen);

        Assert.Equal(new Point(-1060, 1020), topLeft);
    }

    [Fact]
    public void 由橫條範圍反推的錨點_擺回去位置不變()
    {
        var bar = new Rectangle(300, 500, 200, 20);
        Point anchor = BarPlacement.AnchorOf(bar);

        Assert.Equal(bar.Location, BarPlacement.TopLeftFor(anchor, bar.Size, WorkingArea));
    }
}
