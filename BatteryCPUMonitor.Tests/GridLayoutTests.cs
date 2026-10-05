using System.Drawing;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class GridLayoutTests
{
    // 測試用的量測方式：每個字元一律 10 像素寬，方便心算。
    private static int Measure(string text) => text.Length * 10;

    private const int RowHeight = 20;
    private static readonly GridSpacing Spacing = new(PaddingX: 8, PaddingY: 4, LabelGap: 6, ColumnGap: 14, RowGap: 2);

    private static readonly BarColumn CpuRam = new(
        new BarCell("CPU", "5%", Level.Good),
        new BarCell("RAM", "61%", Level.Warn),
        "100%");

    private static readonly BarColumn Network = new(
        new BarCell("上傳", "1.1KB", Level.Good),
        new BarCell("下載", "123KB", Level.Critical),
        "1023KB");

    [Fact]
    public void 單欄_標籤靠左_數值靠右對齊到範本寬度()
    {
        GridResult result = GridLayout.Compute([CpuRam], Measure, RowHeight, Spacing);

        // 標籤寬 30、間距 6、數值欄寬以範本「100%」計為 40 → 數值右緣 = 8 + 30 + 6 + 40 = 84。
        Assert.Equal(new TextRun("CPU", 8, 4, null), result.Runs[0]);
        Assert.Equal(new TextRun("5%", 84 - 20, 4, Level.Good), result.Runs[1]);
        Assert.Equal(new TextRun("RAM", 8, 26, null), result.Runs[2]);
        Assert.Equal(new TextRun("61%", 84 - 30, 26, Level.Warn), result.Runs[3]);
    }

    [Fact]
    public void 單欄_整體大小包含四周留白與兩列文字()
    {
        GridResult result = GridLayout.Compute([CpuRam], Measure, RowHeight, Spacing);

        // 寬 = 84 + 右留白 8；高 = 4 + 20 + 2 + 20 + 4。
        Assert.Equal(new Size(92, 50), result.Size);
    }

    [Fact]
    public void 多欄_欄與欄之間有間距_最後一欄之後沒有()
    {
        GridResult result = GridLayout.Compute([CpuRam, Network], Measure, RowHeight, Spacing);

        // 第二欄從 84 + 14 = 98 開始；標籤寬 20、數值欄寬 60 → 右緣 98 + 20 + 6 + 60 = 184。
        Assert.Equal(new TextRun("上傳", 98, 4, null), result.Runs[4]);
        Assert.Equal(new TextRun("1.1KB", 184 - 50, 4, Level.Good), result.Runs[5]);
        Assert.Equal(new TextRun("123KB", 184 - 50, 26, Level.Critical), result.Runs[7]);
        Assert.Equal(new Size(192, 50), result.Size);
    }

    [Fact]
    public void 數值位數改變時_整體寬度不變()
    {
        var idle = new BarColumn(new BarCell("CPU", "5%", Level.Good), new BarCell("RAM", "9%", Level.Good), "100%");
        var busy = new BarColumn(new BarCell("CPU", "100%", Level.Critical), new BarCell("RAM", "99%", Level.Critical), "100%");

        Assert.Equal(
            GridLayout.Compute([idle], Measure, RowHeight, Spacing).Size,
            GridLayout.Compute([busy], Measure, RowHeight, Spacing).Size);
    }

    [Fact]
    public void 數值比範本還寬時_欄寬跟著撐開而不會被裁掉()
    {
        var wide = new BarColumn(new BarCell("電量", "82%", Level.Good), new BarCell("電源", "充電中充電中", Level.Good), "100%");

        GridResult result = GridLayout.Compute([wide], Measure, RowHeight, Spacing);

        // 數值欄寬 60 → 右緣 = 8 + 20 + 6 + 60 = 94，最寬的數值從標籤後方的間距處開始。
        Assert.Equal(94 - 60, result.Runs[3].X);
        Assert.Equal(102, result.Size.Width);
    }

    [Fact]
    public void 沒有任何欄_只剩留白()
    {
        GridResult result = GridLayout.Compute([], Measure, RowHeight, Spacing);

        Assert.Empty(result.Runs);
        Assert.Equal(new Size(16, 50), result.Size);
    }

    [Theory]
    [InlineData(96, 9, 4, 6, 14, 1)]
    [InlineData(144, 14, 6, 9, 21, 2)]
    [InlineData(192, 18, 8, 12, 28, 2)]
    public void 間距依DPI等比例放大(int dpi, int paddingX, int paddingY, int labelGap, int columnGap, int rowGap)
    {
        Assert.Equal(new GridSpacing(paddingX, paddingY, labelGap, columnGap, rowGap), GridSpacing.ForDpi(dpi));
    }
}
