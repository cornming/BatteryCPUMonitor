using System.Drawing;

namespace BatteryCPUMonitor;

/// <summary>要畫在橫條上的一段文字與它的位置。</summary>
/// <param name="Level">數值的警示等級；標籤文字為 null。</param>
internal readonly record struct TextRun(string Text, int X, int Y, Level? Level);

/// <summary>排版用的間距，單位為像素（已依 DPI 換算）。</summary>
internal readonly record struct GridSpacing(int PaddingX, int PaddingY, int LabelGap, int ColumnGap, int RowGap)
{
    /// <summary>以 96 DPI 為基準，依螢幕 DPI 等比例放大。</summary>
    public static GridSpacing ForDpi(int dpi)
    {
        int Scale(int pixelsAt96) => (int)Math.Round(pixelsAt96 * dpi / 96.0, MidpointRounding.AwayFromZero);
        return new GridSpacing(Scale(9), Scale(4), Scale(6), Scale(14), Scale(1));
    }
}

internal sealed record GridResult(Size Size, IReadOnlyList<TextRun> Runs);

/// <summary>
/// 把各欄排成兩列的格狀版面：每欄「標籤靠左、數值靠右」，上下兩格對齊。純計算，不碰畫面。
/// </summary>
internal static class GridLayout
{
    /// <param name="columns">要顯示的欄。</param>
    /// <param name="measureWidth">量測一段文字寬度（像素）的函式。</param>
    /// <param name="rowHeight">一列文字的高度（像素）。</param>
    /// <param name="spacing">各種間距。</param>
    public static GridResult Compute(
        IReadOnlyList<BarColumn> columns,
        Func<string, int> measureWidth,
        int rowHeight,
        GridSpacing spacing)
    {
        var runs = new List<TextRun>(columns.Count * 4);
        int topY = spacing.PaddingY;
        int bottomY = topY + rowHeight + spacing.RowGap;
        int x = spacing.PaddingX;

        for (int i = 0; i < columns.Count; i++)
        {
            BarColumn column = columns[i];

            int labelWidth = Math.Max(measureWidth(column.Top.Label), measureWidth(column.Bottom.Label));
            int topValueWidth = measureWidth(column.Top.Value);
            int bottomValueWidth = measureWidth(column.Bottom.Value);
            int valueWidth = Math.Max(Math.Max(topValueWidth, bottomValueWidth), measureWidth(column.ValueTemplate));

            int valueRight = x + labelWidth + spacing.LabelGap + valueWidth;

            runs.Add(new TextRun(column.Top.Label, x, topY, null));
            runs.Add(new TextRun(column.Top.Value, valueRight - topValueWidth, topY, column.Top.Level));
            runs.Add(new TextRun(column.Bottom.Label, x, bottomY, null));
            runs.Add(new TextRun(column.Bottom.Value, valueRight - bottomValueWidth, bottomY, column.Bottom.Level));

            x = valueRight + (i < columns.Count - 1 ? spacing.ColumnGap : 0);
        }

        var size = new Size(x + spacing.PaddingX, bottomY + rowHeight + spacing.PaddingY);
        return new GridResult(size, runs);
    }
}
