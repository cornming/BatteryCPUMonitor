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

    /// <summary>
    /// 計算新版三種視覺風格的兩列精密版面：支援雙字體、主數字與單位分開排版、左側狀態頭（導軌／波形圖／星芒）、
    /// 欄間分隔線位置，以及每格下方的微進度條或標籤旁垂直微色條。純計算，不碰畫面。
    /// </summary>
    public static StyledGridResult ComputeStyled(
        IReadOnlyList<BarColumn> columns,
        Func<string, int> measureLabel,
        Func<string, int> measureValue,
        Func<string, int> measureUnit,
        int labelHeight,
        int valueHeight,
        int unitHeight,
        StyledGridMetrics metrics)
    {
        int textHeight = Math.Max(labelHeight, valueHeight);
        int slotHeight = textHeight + (metrics.MeterHeight > 0 ? metrics.MeterGap + metrics.MeterHeight : 0);

        int topSlotY = metrics.PaddingY;
        int bottomSlotY = topSlotY + slotHeight + metrics.RowGap;
        int totalHeight = bottomSlotY + slotHeight + metrics.PaddingY;

        int contentStartX = metrics.PaddingLeft + (metrics.HeaderWidth > 0 ? metrics.HeaderWidth + metrics.HeaderGap : 0);
        int x = contentStartX;

        var cells = new List<StyledCellLayout>(columns.Count * 2);
        var dividers = new List<int>(Math.Max(0, columns.Count - 1));

        for (int i = 0; i < columns.Count; i++)
        {
            BarColumn column = columns[i];

            int labelWidth = Math.Max(measureLabel(column.Top.Label), measureLabel(column.Bottom.Label));
            (string topNum, string topUnit) = ValueDisplay.Split(column.Top.Value);
            (string botNum, string botUnit) = ValueDisplay.Split(column.Bottom.Value);
            (string tplNum, string tplUnit) = ValueDisplay.Split(column.ValueTemplate);

            int topNumW = measureValue(topNum);
            int topUnitW = topUnit.Length > 0 ? measureUnit(topUnit) : 0;
            int topTotalW = topNumW + (topUnitW > 0 ? metrics.UnitGap + topUnitW : 0);

            int botNumW = measureValue(botNum);
            int botUnitW = botUnit.Length > 0 ? measureUnit(botUnit) : 0;
            int botTotalW = botNumW + (botUnitW > 0 ? metrics.UnitGap + botUnitW : 0);

            int tplTotalW = measureValue(tplNum) + (tplUnit.Length > 0 ? metrics.UnitGap + measureUnit(tplUnit) : 0);
            int valueWidth = Math.Max(Math.Max(topTotalW, botTotalW), tplTotalW);

            int colWidth = metrics.TickSpace + labelWidth + metrics.LabelGap + valueWidth;
            int valueRight = x + colWidth;

            cells.Add(BuildCell(
                column.Top, topNum, topUnit, topNumW, topTotalW,
                x, topSlotY, colWidth, valueRight,
                textHeight, labelHeight, valueHeight, unitHeight,
                column.TopRatio, metrics));

            cells.Add(BuildCell(
                column.Bottom, botNum, botUnit, botNumW, botTotalW,
                x, bottomSlotY, colWidth, valueRight,
                textHeight, labelHeight, valueHeight, unitHeight,
                column.BottomRatio, metrics));

            if (i < columns.Count - 1)
            {
                dividers.Add(valueRight + metrics.ColumnGap / 2);
                x = valueRight + metrics.ColumnGap;
            }
            else
            {
                x = valueRight;
            }
        }

        var size = new Size(x + metrics.PaddingRight, totalHeight);
        var headerRect = metrics.HeaderWidth > 0
            ? new Rectangle(metrics.PaddingLeft, metrics.PaddingY, metrics.HeaderWidth, Math.Max(0, totalHeight - metrics.PaddingY * 2))
            : Rectangle.Empty;

        return new StyledGridResult(size, headerRect, cells, dividers);
    }

    private static StyledCellLayout BuildCell(
        BarCell cell,
        string numText,
        string unitText,
        int numW,
        int totalValW,
        int colX,
        int slotY,
        int colWidth,
        int valueRight,
        int textHeight,
        int labelHeight,
        int valueHeight,
        int unitHeight,
        double? ratio,
        StyledGridMetrics metrics)
    {
        // 標籤與單位對齊到與數值相近的視覺底線，看起來較沉穩一致。
        int valueY = slotY + (textHeight - valueHeight) / 2;
        int labelY = slotY + (textHeight - labelHeight) / 2;
        int unitY = valueY + Math.Max(0, valueHeight - unitHeight - metrics.UnitBaselineLift);

        int labelX = colX + metrics.TickSpace;
        int valueX = valueRight - totalValW;
        int unitX = valueX + numW + (unitText.Length > 0 ? metrics.UnitGap : 0);

        Rectangle meterRect = metrics.MeterHeight > 0 && ratio is not null
            ? new Rectangle(colX, slotY + textHeight + metrics.MeterGap, colWidth, metrics.MeterHeight)
            : Rectangle.Empty;

        Rectangle tickRect = metrics.TickWidth > 0 && cell.Label.Length > 0
            ? new Rectangle(
                colX,
                slotY + Math.Max(0, (textHeight - metrics.TickHeight) / 2),
                metrics.TickWidth,
                metrics.TickHeight)
            : Rectangle.Empty;

        return new StyledCellLayout(
            Label: cell.Label,
            NumberText: numText,
            UnitText: unitText,
            Level: cell.Level,
            Ratio: ratio,
            LabelPosition: new Point(labelX, labelY),
            NumberPosition: new Point(valueX, valueY),
            UnitPosition: new Point(unitX, unitY),
            MeterRect: meterRect,
            TickRect: tickRect);
    }
}

/// <summary>不同風格與 DPI 下的版面尺寸參數（像素）。</summary>
internal readonly record struct StyledGridMetrics(
    int PaddingLeft,
    int PaddingRight,
    int PaddingY,
    int HeaderWidth,
    int HeaderGap,
    int TickWidth,
    int TickHeight,
    int TickSpace,
    int LabelGap,
    int UnitGap,
    int UnitBaselineLift,
    int ColumnGap,
    int RowGap,
    int MeterHeight,
    int MeterGap)
{
    public static StyledGridMetrics For(BarVisualStyle style, int dpi, bool taskbar)
    {
        int S(int px) => (int)Math.Round(px * dpi / 96.0, MidpointRounding.AwayFromZero);

        if (taskbar)
        {
            return style switch
            {
                BarVisualStyle.HudMatrix => new StyledGridMetrics(
                    PaddingLeft: S(4), PaddingRight: S(4), PaddingY: 0,
                    HeaderWidth: S(32), HeaderGap: S(7),
                    TickWidth: 0, TickHeight: 0, TickSpace: 0,
                    LabelGap: S(5), UnitGap: 0, UnitBaselineLift: S(1),
                    ColumnGap: S(13), RowGap: S(1),
                    MeterHeight: S(2), MeterGap: S(1)),

                BarVisualStyle.Editorial => new StyledGridMetrics(
                    PaddingLeft: S(4), PaddingRight: S(4), PaddingY: 0,
                    HeaderWidth: S(12), HeaderGap: S(6),
                    TickWidth: S(2), TickHeight: S(8), TickSpace: S(5),
                    LabelGap: S(5), UnitGap: 0, UnitBaselineLift: S(1),
                    ColumnGap: S(13), RowGap: S(1),
                    MeterHeight: 0, MeterGap: 0),

                _ => new StyledGridMetrics(
                    PaddingLeft: S(4), PaddingRight: S(4), PaddingY: 0,
                    HeaderWidth: S(3), HeaderGap: S(7),
                    TickWidth: 0, TickHeight: 0, TickSpace: 0,
                    LabelGap: S(5), UnitGap: 0, UnitBaselineLift: S(1),
                    ColumnGap: S(13), RowGap: S(1),
                    MeterHeight: S(2), MeterGap: S(1)),
            };
        }

        return style switch
        {
            BarVisualStyle.HudMatrix => new StyledGridMetrics(
                PaddingLeft: S(10), PaddingRight: S(11), PaddingY: S(6),
                HeaderWidth: S(38), HeaderGap: S(11),
                TickWidth: 0, TickHeight: 0, TickSpace: 0,
                LabelGap: S(6), UnitGap: 0, UnitBaselineLift: S(1),
                ColumnGap: S(17), RowGap: S(3),
                MeterHeight: S(3), MeterGap: S(2)),

            BarVisualStyle.Editorial => new StyledGridMetrics(
                PaddingLeft: S(10), PaddingRight: S(13), PaddingY: S(6),
                HeaderWidth: S(15), HeaderGap: S(9),
                TickWidth: S(2), TickHeight: S(9), TickSpace: S(6),
                LabelGap: S(6), UnitGap: 0, UnitBaselineLift: S(1),
                ColumnGap: S(17), RowGap: S(3),
                MeterHeight: 0, MeterGap: 0),

            _ => new StyledGridMetrics(
                PaddingLeft: S(9), PaddingRight: S(12), PaddingY: S(6),
                HeaderWidth: S(4), HeaderGap: S(9),
                TickWidth: 0, TickHeight: 0, TickSpace: 0,
                LabelGap: S(6), UnitGap: 0, UnitBaselineLift: S(1),
                ColumnGap: S(17), RowGap: S(3),
                MeterHeight: S(2), MeterGap: S(2)),
        };
    }

    /// <summary>工作列高度緊湊時，收斂微進度條以確保兩列文字永遠完整不裁切。</summary>
    public StyledGridMetrics WithoutMeters() => this with { MeterHeight = 0, MeterGap = 0 };
}

internal readonly record struct StyledCellLayout(
    string Label,
    string NumberText,
    string UnitText,
    Level Level,
    double? Ratio,
    Point LabelPosition,
    Point NumberPosition,
    Point UnitPosition,
    Rectangle MeterRect,
    Rectangle TickRect);

internal sealed record StyledGridResult(
    Size Size,
    Rectangle HeaderRect,
    IReadOnlyList<StyledCellLayout> Cells,
    IReadOnlyList<int> DividerXs);
