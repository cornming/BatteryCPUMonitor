using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace BatteryCPUMonitor;

/// <summary>
/// 統一負責三種視覺風格（曜石儀表、航電矩陣、極簡雙色）的 GDI / GDI+ 繪製。
/// 浮動橫條模式使用 GDI+ ClearType 與抗鋸齒，讓深／淺色漸層與微格線上文字都銳利無色塊；
/// 工作列模式（色鍵透明）使用 GDI 搭配明確色鍵底色與像素對齊繪製，避免半透明毛邊。
/// </summary>
internal static class BarRenderer
{
    public const TextFormatFlags TextFlags =
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

    public static Size MeasureFloatingText(Graphics g, string text, Font font)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Size.Empty;
        }

        TextRenderingHint oldHint = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        try
        {
            using StringFormat format = CreateFloatingStringFormat();
            SizeF size = g.MeasureString(text, font, PointF.Empty, format);
            return new Size((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height));
        }
        finally
        {
            g.TextRenderingHint = oldHint;
        }
    }

    private static StringFormat CreateFloatingStringFormat()
    {
        var format = new StringFormat(StringFormat.GenericTypographic);
        format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
        return format;
    }

    public static void PaintFloating(
        Graphics g,
        Size clientSize,
        StyledGridResult layout,
        ThemeFontSet fonts,
        ThemePalette palette,
        BarVisualStyle style,
        Level overallLevel,
        IReadOnlyList<double> cpuHistory,
        bool pulseBright,
        bool drawOuterBorder,
        int cornerRadius)
    {
        if (clientSize.Width <= 0 || clientSize.Height <= 0)
        {
            return;
        }

        int dpi = fonts.Dpi;
        var fullRect = new Rectangle(0, 0, clientSize.Width, clientSize.Height);

        // 1. 機身微漸層背景
        using (var bgBrush = new LinearGradientBrush(fullRect, palette.PanelTop, palette.PanelBottom, LinearGradientMode.Vertical))
        {
            g.FillRectangle(bgBrush, fullRect);
        }

        // 2. 航電矩陣：細微技術格線底紋
        if (style == BarVisualStyle.HudMatrix)
        {
            DrawMicroGrid(g, clientSize, palette.GridLine, Scale(12, dpi));
        }

        // 3. 頂部細微高光，營造陽極處理金屬／暖曜黑質感
        using (var specularPen = new Pen(palette.TopSpecular, 1f))
        {
            int inset = Math.Min(cornerRadius, clientSize.Width / 4);
            g.DrawLine(specularPen, inset, 1, Math.Max(inset, clientSize.Width - inset - 1), 1);
        }

        // 4. 外框線（Windows 10 或 DrawToBitmap 截圖時特別有幫助；Windows 11 DWM 也會同步畫原生細框）
        if (drawOuterBorder)
        {
            DrawOuterBorder(g, clientSize, palette.Border, cornerRadius);
        }

        // 5. 航電矩陣：四角陶土橘校準 L 框線
        if (style == BarVisualStyle.HudMatrix)
        {
            DrawCornerBrackets(g, clientSize, palette.ForStatusBeacon(overallLevel), dpi);
        }

        // 6. 左側識別頭（狀態導軌／LIVE 波形圖／Claude 幾何星芒）
        Color beaconColor = palette.ForStatusBeacon(overallLevel);
        DrawHeader(g, layout.HeaderRect, offsetY: 0, fonts, palette, style, beaconColor, cpuHistory, pulseBright, isTaskbar: false);

        // 7. 欄間垂直細分隔線
        DrawDividers(g, layout.DividerXs, offsetY: 0, layout.Size.Height, palette.Divider, style, isTaskbar: false);

        // 8. 數值格（文字＋微進度條／LED 格／垂直狀態微條）
        DrawCells(g, layout.Cells, offsetY: 0, fonts, palette, style, textBackColor: palette.PanelSolid, isTaskbar: false);
    }

    public static void PaintTaskbar(
        Graphics g,
        int offsetY,
        StyledGridResult layout,
        ThemeFontSet fonts,
        ThemePalette palette,
        BarVisualStyle style,
        Level overallLevel,
        IReadOnlyList<double> cpuHistory,
        bool pulseBright,
        Color colorKey)
    {
        Color beaconColor = palette.ForStatusBeacon(overallLevel);
        DrawHeader(g, layout.HeaderRect, offsetY, fonts, palette, style, beaconColor, cpuHistory, pulseBright, isTaskbar: true);
        DrawDividers(g, layout.DividerXs, offsetY, layout.Size.Height, palette.Divider, style, isTaskbar: true);
        DrawCells(g, layout.Cells, offsetY, fonts, palette, style, textBackColor: colorKey, isTaskbar: true);
    }

    /// <summary>
    /// 在工作列中以實心儀表膠囊繪製（當使用者選擇與 Windows 工作列相反的深淺主題時使用）：
    /// 膠囊外部維持透明色鍵，膠囊外輪廓採像素對齊零毛邊繪製，內部則享有與浮動橫條一致的漸層與 GDI+ ClearType 質感。
    /// </summary>
    public static void PaintTaskbarCapsule(
        Graphics g,
        Size clientSize,
        StyledGridResult layout,
        ThemeFontSet fonts,
        ThemePalette palette,
        BarVisualStyle style,
        Level overallLevel,
        IReadOnlyList<double> cpuHistory,
        bool pulseBright,
        Color colorKey)
    {
        if (clientSize.Width <= 2 || clientSize.Height <= 2)
        {
            return;
        }

        int dpi = fonts.Dpi;
        int minOuterMarginY = Math.Max(1, Scale(2, dpi));
        int preferredMarginY = Math.Max(2, Scale(3, dpi));
        int neededHeight = layout.Size.Height + Scale(4, dpi);
        int maxCapsuleH = Math.Max(1, clientSize.Height - minOuterMarginY * 2);
        int capsuleH = Math.Min(maxCapsuleH, Math.Max(neededHeight, clientSize.Height - preferredMarginY * 2));
        int capsuleY = Math.Max(0, (clientSize.Height - capsuleH) / 2);
        var capsule = new Rectangle(0, capsuleY, clientSize.Width, capsuleH);

        // 1. 像素對齊膠囊底框（避免外緣抗鋸齒與 colorKey 混色產生雜邊）
        using (var bgBrush = new LinearGradientBrush(capsule, palette.PanelTop, palette.PanelBottom, LinearGradientMode.Vertical))
        {
            g.FillRectangle(bgBrush, capsule);
        }

        // 2. 航電矩陣微格線
        if (style == BarVisualStyle.HudMatrix)
        {
            GraphicsState state = g.Save();
            g.SetClip(capsule);
            DrawMicroGrid(g, new Size(capsule.Right, capsule.Bottom), palette.GridLine, Scale(12, dpi));
            g.Restore(state);
        }

        // 3. 頂部微高光
        using (var specularPen = new Pen(palette.TopSpecular, 1f))
        {
            g.DrawLine(specularPen, capsule.Left + 2, capsule.Top + 1, Math.Max(capsule.Left + 2, capsule.Right - 3), capsule.Top + 1);
        }

        // 4. 像素對齊外框；非航電風格在四角切去 1px 還原為 colorKey，呈現俐落微圓角且完全無毛邊
        using (var borderPen = new Pen(palette.Border, 1f))
        {
            g.DrawRectangle(borderPen, capsule.X, capsule.Y, capsule.Width - 1, capsule.Height - 1);
        }

        if (style != BarVisualStyle.HudMatrix && capsule.Width > 4 && capsule.Height > 4)
        {
            using var keyBrush = new SolidBrush(colorKey);
            using var cornerBrush = new SolidBrush(palette.Border);
            int l = capsule.Left;
            int r = capsule.Right - 1;
            int t = capsule.Top;
            int b = capsule.Bottom - 1;

            g.FillRectangle(keyBrush, l, t, 1, 1);
            g.FillRectangle(keyBrush, r, t, 1, 1);
            g.FillRectangle(keyBrush, l, b, 1, 1);
            g.FillRectangle(keyBrush, r, b, 1, 1);

            g.FillRectangle(cornerBrush, l + 1, t + 1, 1, 1);
            g.FillRectangle(cornerBrush, r - 1, t + 1, 1, 1);
            g.FillRectangle(cornerBrush, l + 1, b - 1, 1, 1);
            g.FillRectangle(cornerBrush, r - 1, b - 1, 1, 1);
        }
        else if (style == BarVisualStyle.HudMatrix)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(capsule.X, capsule.Y);
            DrawCornerBrackets(g, capsule.Size, palette.ForStatusBeacon(overallLevel), dpi);
            g.Restore(state);
        }

        // 5. 膠囊內部全部是不透明暖曜黑／暖象牙白底，可直接用高品質抗鋸齒與 ClearType 繪製
        int contentOffsetY = capsule.Y + Math.Max(0, (capsule.Height - layout.Size.Height) / 2);
        Color beaconColor = palette.ForStatusBeacon(overallLevel);
        DrawHeader(g, layout.HeaderRect, contentOffsetY, fonts, palette, style, beaconColor, cpuHistory, pulseBright, isTaskbar: false);
        DrawDividers(g, layout.DividerXs, contentOffsetY, layout.Size.Height, palette.Divider, style, isTaskbar: false);
        DrawCells(g, layout.Cells, contentOffsetY, fonts, palette, style, textBackColor: palette.PanelSolid, isTaskbar: false);
    }

    public static int CornerRadiusFor(BarVisualStyle style, int dpi) => style switch
    {
        BarVisualStyle.HudMatrix => Scale(4, dpi),
        BarVisualStyle.Editorial => Scale(12, dpi),
        _ => Scale(9, dpi),
    };

    private static void DrawMicroGrid(Graphics g, Size size, Color gridColor, int step)
    {
        if (step < 4)
        {
            return;
        }

        using var pen = new Pen(gridColor, 1f);
        for (int x = step; x < size.Width; x += step)
        {
            g.DrawLine(pen, x, 0, x, size.Height);
        }

        for (int y = step; y < size.Height; y += step)
        {
            g.DrawLine(pen, 0, y, size.Width, y);
        }
    }

    private static void DrawOuterBorder(Graphics g, Size size, Color borderColor, int cornerRadius)
    {
        SmoothingMode oldMode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            using var pen = new Pen(borderColor, 1f);
            if (cornerRadius <= 2)
            {
                g.DrawRectangle(pen, 0, 0, size.Width - 1, size.Height - 1);
                return;
            }

            using GraphicsPath path = CreateRoundedRectPath(new RectangleF(0.5f, 0.5f, size.Width - 1f, size.Height - 1f), cornerRadius);
            g.DrawPath(pen, path);
        }
        finally
        {
            g.SmoothingMode = oldMode;
        }
    }

    private static void DrawCornerBrackets(Graphics g, Size size, Color accentColor, int dpi)
    {
        int arm = Math.Max(4, Scale(5, dpi));
        int inset = Scale(2, dpi);
        float thickness = Math.Max(1.25f, dpi / 96f * 1.4f);

        using var pen = new Pen(Color.FromArgb(220, accentColor), thickness)
        {
            StartCap = LineCap.Square,
            EndCap = LineCap.Square,
        };

        int right = size.Width - 1 - inset;
        int bottom = size.Height - 1 - inset;

        // 左上
        g.DrawLine(pen, inset, inset + arm, inset, inset);
        g.DrawLine(pen, inset, inset, inset + arm, inset);
        // 右上
        g.DrawLine(pen, right - arm, inset, right, inset);
        g.DrawLine(pen, right, inset, right, inset + arm);
        // 左下
        g.DrawLine(pen, inset, bottom - arm, inset, bottom);
        g.DrawLine(pen, inset, bottom, inset + arm, bottom);
        // 右下
        g.DrawLine(pen, right - arm, bottom, right, bottom);
        g.DrawLine(pen, right, bottom, right, bottom - arm);
    }

    private static void DrawHeader(
        Graphics g,
        Rectangle rect,
        int offsetY,
        ThemeFontSet fonts,
        ThemePalette palette,
        BarVisualStyle style,
        Color beaconColor,
        IReadOnlyList<double> cpuHistory,
        bool pulseBright,
        bool isTaskbar)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        int dpi = fonts.Dpi;
        var area = new Rectangle(rect.X, rect.Y + offsetY, rect.Width, rect.Height);

        switch (style)
        {
            case BarVisualStyle.Obsidian:
                DrawObsidianRail(g, area, beaconColor, palette.IsLight, pulseBright, isTaskbar, dpi);
                break;

            case BarVisualStyle.HudMatrix:
                DrawHudSparkPanel(g, area, fonts, palette, beaconColor, cpuHistory, pulseBright, isTaskbar, dpi);
                break;

            case BarVisualStyle.Editorial:
                DrawEditorialSpark(g, area, beaconColor, palette.PanelSolid, isTaskbar, dpi);
                break;
        }
    }

    private static void DrawObsidianRail(
        Graphics g,
        Rectangle area,
        Color beaconColor,
        bool isLight,
        bool pulseBright,
        bool isTaskbar,
        int dpi)
    {
        int railW = Math.Max(2, isTaskbar ? Scale(2, dpi) : Scale(3, dpi));
        int railH = Math.Max(8, (int)Math.Round(area.Height * 0.72));
        int railX = area.X + (area.Width - railW) / 2;
        int railY = area.Y + (area.Height - railH) / 2;
        var railRect = new Rectangle(railX, railY, railW, railH);

        if (isTaskbar)
        {
            using var brush = new SolidBrush(beaconColor);
            g.FillRectangle(brush, railRect);
            return;
        }

        SmoothingMode oldMode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            if (!isLight)
            {
                int glowAlpha = pulseBright ? 58 : 34;
                using var glowBrush = new SolidBrush(Color.FromArgb(glowAlpha, beaconColor));
                var glowRect = Rectangle.Inflate(railRect, Scale(2, dpi), Scale(2, dpi));
                using GraphicsPath glowPath = CreateRoundedRectPath(glowRect, glowRect.Width / 2f);
                g.FillPath(glowBrush, glowPath);
            }

            Color core = pulseBright ? beaconColor : Blend(beaconColor, isLight ? Color.White : Color.Black, 0.14f);
            using var railBrush = new SolidBrush(core);
            using GraphicsPath railPath = CreateRoundedRectPath(railRect, railW / 2f);
            g.FillPath(railBrush, railPath);
        }
        finally
        {
            g.SmoothingMode = oldMode;
        }
    }

    private static void DrawHudSparkPanel(
        Graphics g,
        Rectangle area,
        ThemeFontSet fonts,
        ThemePalette palette,
        Color beaconColor,
        IReadOnlyList<double> cpuHistory,
        bool pulseBright,
        bool isTaskbar,
        int dpi)
    {
        int dotSize = Math.Max(3, Scale(4, dpi));
        int dotX = area.X;
        int dotY = area.Y + Scale(2, dpi);

        // 頂端心跳脈動點 + LIVE 標籤
        Color dotColor = pulseBright ? beaconColor : Blend(beaconColor, palette.PanelSolid, 0.42f);
        if (isTaskbar)
        {
            using var dotBrush = new SolidBrush(dotColor);
            g.FillRectangle(dotBrush, dotX, dotY, dotSize, dotSize);
        }
        else
        {
            SmoothingMode oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                if (pulseBright && !palette.IsLight)
                {
                    using var haloBrush = new SolidBrush(Color.FromArgb(70, beaconColor));
                    g.FillEllipse(haloBrush, dotX - 1.5f, dotY - 1.5f, dotSize + 3f, dotSize + 3f);
                }

                using var dotBrush = new SolidBrush(dotColor);
                g.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);
            }
            finally
            {
                g.SmoothingMode = oldMode;
            }
        }

        int liveX = dotX + dotSize + Scale(2, dpi);
        int liveY = area.Y;
        DrawText(g, "LIVE", fonts.Micro, new Point(liveX, liveY), palette.Label, isTaskbar ? (palette.IsLight ? BarTheme.TaskbarLightKey : BarTheme.TaskbarDarkKey) : null);

        // 波形折線圖區
        int headerTop = Math.Max(dotY + dotSize + Scale(2, dpi), liveY + Scale(10, dpi));
        int chartH = Math.Max(6, area.Bottom - headerTop - Scale(1, dpi));
        headerTop = Math.Max(area.Y, Math.Min(headerTop, area.Bottom - chartH));
        var chart = new Rectangle(area.X, headerTop, area.Width, chartH);

        // 基準底線與 50% 參考微線（浮動模式）
        using (var basePen = new Pen(palette.Divider, 1f))
        {
            g.DrawLine(basePen, chart.Left, chart.Bottom - 1, chart.Right - 1, chart.Bottom - 1);
        }

        if (!isTaskbar && chart.Height >= 10)
        {
            int midY = chart.Top + chart.Height / 2;
            using var midPen = new Pen(Color.FromArgb(palette.IsLight ? 40 : 48, palette.Divider), 1f)
            {
                DashStyle = DashStyle.Dot,
            };
            g.DrawLine(midPen, chart.Left, midY, chart.Right - 1, midY);
        }

        if (cpuHistory.Count < 2 || chart.Width < 6)
        {
            // 尚未累積 2 筆歷史時畫一條中位微線作為佔位
            using var flatPen = new Pen(palette.Info, 1f);
            int midY = chart.Bottom - Math.Max(2, chart.Height / 3);
            g.DrawLine(flatPen, chart.Left, midY, chart.Right - 1, midY);
        }
        else
        {
            Color lineColor = beaconColor == palette.Accent ? palette.Info : beaconColor;
            var points = new PointF[cpuHistory.Count];
            for (int i = 0; i < cpuHistory.Count; i++)
            {
                float x = chart.Left + (chart.Width - 1) * (i / (float)(cpuHistory.Count - 1));
                float ratio = (float)Math.Clamp(cpuHistory[i] / 100.0, 0.0, 1.0);
                float y = chart.Bottom - 1 - ratio * (chart.Height - 2);
                points[i] = new PointF(isTaskbar ? MathF.Round(x) : x, isTaskbar ? MathF.Round(y) : y);
            }

            if (isTaskbar)
            {
                using var linePen = new Pen(lineColor, 1f);
                g.DrawLines(linePen, points);
            }
            else
            {
                SmoothingMode oldMode = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                try
                {
                    using (var fillPath = new GraphicsPath())
                    {
                        fillPath.AddLines(points);
                        fillPath.AddLine(points[^1], new PointF(chart.Right - 1, chart.Bottom - 1));
                        fillPath.AddLine(new PointF(chart.Right - 1, chart.Bottom - 1), new PointF(chart.Left, chart.Bottom - 1));
                        fillPath.CloseFigure();

                        using var fillBrush = new SolidBrush(Color.FromArgb(44, lineColor));
                        g.FillPath(fillBrush, fillPath);
                    }

                    using var linePen = new Pen(lineColor, Math.Max(1.25f, dpi / 96f * 1.25f))
                    {
                        LineJoin = LineJoin.Round,
                        StartCap = LineCap.Round,
                        EndCap = LineCap.Round,
                    };
                    g.DrawLines(linePen, points);
                }
                finally
                {
                    g.SmoothingMode = oldMode;
                }
            }
        }

        // 右側細分隔線（區隔波形區與第一欄）
        int sepX = area.Right + Scale(4, dpi);
        using var sepPen = new Pen(palette.Divider, 1f);
        g.DrawLine(sepPen, sepX, area.Top + Scale(2, dpi), sepX, area.Bottom - Scale(2, dpi));
    }

    private static void DrawEditorialSpark(
        Graphics g,
        Rectangle area,
        Color beaconColor,
        Color centerColor,
        bool isTaskbar,
        int dpi)
    {
        float cx = area.X + area.Width / 2f;
        float cy = area.Y + area.Height / 2f;
        float radius = Math.Min(area.Width, area.Height) * 0.42f;

        if (isTaskbar)
        {
            int icx = (int)Math.Round(cx);
            int icy = (int)Math.Round(cy);
            int ir = Math.Max(3, (int)Math.Round(radius));
            int diag = Math.Max(2, (int)Math.Round(ir * 0.72));
            using var pen = new Pen(beaconColor, 1f);
            g.DrawLine(pen, icx, icy - ir, icx, icy + ir);
            g.DrawLine(pen, icx - diag, icy - diag, icx + diag, icy + diag);
            g.DrawLine(pen, icx - diag, icy + diag, icx + diag, icy - diag);
            return;
        }

        SmoothingMode oldMode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            float stroke = Math.Max(1.4f, dpi / 96f * 1.55f);
            using var pen = new Pen(beaconColor, stroke)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };

            // 6 芒幾何星芒（致敬 Claude 星芒語彙）
            for (int i = 0; i < 3; i++)
            {
                double angle = (i * 60.0 - 90.0) * Math.PI / 180.0;
                float dx = (float)(Math.Cos(angle) * radius);
                float dy = (float)(Math.Sin(angle) * radius);
                g.DrawLine(pen, cx - dx, cy - dy, cx + dx, cy + dy);
            }

            float hole = Math.Max(1.3f, dpi / 96f * 1.5f);
            using var centerBrush = new SolidBrush(centerColor);
            g.FillEllipse(centerBrush, cx - hole, cy - hole, hole * 2f, hole * 2f);
        }
        finally
        {
            g.SmoothingMode = oldMode;
        }
    }

    private static void DrawDividers(
        Graphics g,
        IReadOnlyList<int> dividerXs,
        int offsetY,
        int layoutHeight,
        Color dividerColor,
        BarVisualStyle style,
        bool isTaskbar)
    {
        if (dividerXs.Count == 0)
        {
            return;
        }

        int margin = Math.Max(4, (int)Math.Round(layoutHeight * (style == BarVisualStyle.Editorial ? 0.22 : 0.16)));
        int y1 = offsetY + margin;
        int y2 = offsetY + layoutHeight - margin;
        if (y2 <= y1)
        {
            return;
        }

        if (!isTaskbar && style == BarVisualStyle.HudMatrix)
        {
            using var gradBrush = new LinearGradientBrush(
                new Point(0, y1),
                new Point(0, y2),
                Color.FromArgb(0, dividerColor),
                Color.FromArgb(0, dividerColor));
            gradBrush.InterpolationColors = new ColorBlend
            {
                Colors = [Color.FromArgb(0, dividerColor), dividerColor, dividerColor, Color.FromArgb(0, dividerColor)],
                Positions = [0f, 0.22f, 0.78f, 1f],
            };
            using var pen = new Pen(gradBrush, 1f);
            foreach (int x in dividerXs)
            {
                g.DrawLine(pen, x, y1, x, y2);
            }

            return;
        }

        using var solidPen = new Pen(dividerColor, 1f);
        foreach (int x in dividerXs)
        {
            g.DrawLine(solidPen, x, y1, x, y2);
        }
    }

    private static void DrawCells(
        Graphics g,
        IReadOnlyList<StyledCellLayout> cells,
        int offsetY,
        ThemeFontSet fonts,
        ThemePalette palette,
        BarVisualStyle style,
        Color textBackColor,
        bool isTaskbar)
    {
        foreach (StyledCellLayout cell in cells)
        {
            Color valueColor = palette.For(cell.Level);

            // 極簡雙色風格：標籤左側 2px 垂直狀態微條
            if (cell.TickRect.Width > 0 && cell.TickRect.Height > 0)
            {
                var tickRect = new Rectangle(cell.TickRect.X, cell.TickRect.Y + offsetY, cell.TickRect.Width, cell.TickRect.Height);
                using var tickBrush = new SolidBrush(valueColor);
                g.FillRectangle(tickBrush, tickRect);
            }

            Color? textBg = isTaskbar ? textBackColor : null;

            if (cell.Label.Length > 0)
            {
                DrawText(
                    g,
                    cell.Label,
                    fonts.Label,
                    new Point(cell.LabelPosition.X, cell.LabelPosition.Y + offsetY),
                    palette.Label,
                    textBg);
            }

            if (cell.NumberText.Length > 0)
            {
                DrawText(
                    g,
                    cell.NumberText,
                    fonts.Value,
                    new Point(cell.NumberPosition.X, cell.NumberPosition.Y + offsetY),
                    valueColor,
                    textBg);
            }

            if (cell.UnitText.Length > 0)
            {
                DrawText(
                    g,
                    cell.UnitText,
                    fonts.Unit,
                    new Point(cell.UnitPosition.X, cell.UnitPosition.Y + offsetY),
                    palette.Unit,
                    textBg);
            }

            if (cell.MeterRect.Width > 0 && cell.MeterRect.Height > 0 && cell.Ratio is double ratio)
            {
                var meterRect = new Rectangle(cell.MeterRect.X, cell.MeterRect.Y + offsetY, cell.MeterRect.Width, cell.MeterRect.Height);
                if (style == BarVisualStyle.HudMatrix)
                {
                    DrawSegmentedLedMeter(g, meterRect, ratio, valueColor, palette.Track, fonts.Dpi);
                }
                else
                {
                    DrawSmoothMicroMeter(g, meterRect, ratio, valueColor, palette.Track, isTaskbar);
                }
            }
        }
    }

    private static void DrawSmoothMicroMeter(Graphics g, Rectangle rect, double ratio, Color fillColor, Color trackColor, bool isTaskbar)
    {
        int fillWidth = Math.Clamp((int)Math.Round(rect.Width * ratio, MidpointRounding.AwayFromZero), ratio > 0 ? 2 : 0, rect.Width);

        if (isTaskbar || rect.Height <= 2)
        {
            using (var trackBrush = new SolidBrush(trackColor))
            {
                g.FillRectangle(trackBrush, rect);
            }

            if (fillWidth > 0)
            {
                using var fillBrush = new SolidBrush(fillColor);
                g.FillRectangle(fillBrush, rect.X, rect.Y, fillWidth, rect.Height);
            }

            return;
        }

        SmoothingMode oldMode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            float r = rect.Height / 2f;
            using (var trackBrush = new SolidBrush(trackColor))
            using (GraphicsPath trackPath = CreateRoundedRectPath(rect, r))
            {
                g.FillPath(trackBrush, trackPath);
            }

            if (fillWidth > 0)
            {
                var fillRect = new Rectangle(rect.X, rect.Y, fillWidth, rect.Height);
                using var fillBrush = new SolidBrush(fillColor);
                using GraphicsPath fillPath = CreateRoundedRectPath(fillRect, Math.Min(r, fillWidth / 2f));
                g.FillPath(fillBrush, fillPath);
            }
        }
        finally
        {
            g.SmoothingMode = oldMode;
        }
    }

    private static void DrawSegmentedLedMeter(Graphics g, Rectangle rect, double ratio, Color fillColor, Color trackColor, int dpi)
    {
        const int segments = 8;
        int gap = Math.Max(1, Scale(1, dpi));
        int totalGap = gap * (segments - 1);
        int usable = Math.Max(segments, rect.Width - totalGap);
        int lit = ratio <= 0 ? 0 : Math.Clamp((int)Math.Round(ratio * segments, MidpointRounding.AwayFromZero), 1, segments);

        using var litBrush = new SolidBrush(fillColor);
        using var offBrush = new SolidBrush(trackColor);

        for (int i = 0; i < segments; i++)
        {
            int x0 = rect.X + (usable * i / segments) + i * gap;
            int x1 = rect.X + (usable * (i + 1) / segments) + i * gap;
            int w = Math.Max(1, x1 - x0);
            g.FillRectangle(i < lit ? litBrush : offBrush, x0, rect.Y, w, rect.Height);
        }
    }

    private static GraphicsPath CreateRoundedRectPath(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
        if (d <= 0.5f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        from.A,
        (int)Math.Round(from.R + (to.R - from.R) * amount),
        (int)Math.Round(from.G + (to.G - from.G) * amount),
        (int)Math.Round(from.B + (to.B - from.B) * amount));

    /// <summary>
    /// 浮動模式使用 GDI+ ClearType 直接疊畫在漸層與細微格線上，深淺主題都不留色塊或黑邊；
    /// 工作列模式（LWA_COLORKEY）必須透過 GDI 並傳入色鍵底色，確保文字邊緣不產生雜邊。
    /// </summary>
    private static void DrawText(Graphics g, string text, Font font, Point position, Color foreColor, Color? opaqueBackColor)
    {
        if (opaqueBackColor is Color backColor)
        {
            TextRenderer.DrawText(g, text, font, position, foreColor, backColor, TextFlags);
            return;
        }

        TextRenderingHint oldHint = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        try
        {
            using var brush = new SolidBrush(foreColor);
            using StringFormat format = CreateFloatingStringFormat();
            g.DrawString(text, font, brush, position.X, position.Y, format);
        }
        finally
        {
            g.TextRenderingHint = oldHint;
        }
    }

    private static int Scale(int px, int dpi) => (int)Math.Round(px * dpi / 96.0, MidpointRounding.AwayFromZero);
}
