using System.Drawing;
using BatteryCPUMonitor.Metrics;
using BatteryCPUMonitor.Sensors;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class AppearanceTests
{
    [Theory]
    [InlineData("obsidian", BarVisualStyle.Obsidian)]
    [InlineData("hud", BarVisualStyle.HudMatrix)]
    [InlineData("HUD", BarVisualStyle.HudMatrix)]
    [InlineData("editorial", BarVisualStyle.Editorial)]
    [InlineData("unknown", BarVisualStyle.Obsidian)]
    [InlineData(null, BarVisualStyle.Obsidian)]
    public void 外觀風格字串解析與轉回(string? raw, BarVisualStyle expected)
    {
        Assert.Equal(expected, BarVisualStyles.Parse(raw));
        Assert.Equal(expected, BarVisualStyles.Parse(BarVisualStyles.ToText(expected)));
    }

    [Theory]
    [InlineData("dark", ThemeMode.Dark)]
    [InlineData("light", ThemeMode.Light)]
    [InlineData("system", ThemeMode.System)]
    [InlineData("SYSTEM", ThemeMode.System)]
    [InlineData("invalid", ThemeMode.Dark)]
    [InlineData(null, ThemeMode.Dark)]
    public void 深淺主題字串解析與轉回(string? raw, ThemeMode expected)
    {
        Assert.Equal(expected, ThemeModes.Parse(raw));
        Assert.Equal(expected, ThemeModes.Parse(ThemeModes.ToText(expected)));
    }

    [Theory]
    [InlineData(ThemeMode.Dark, true, false)]
    [InlineData(ThemeMode.Dark, false, false)]
    [InlineData(ThemeMode.Light, false, true)]
    [InlineData(ThemeMode.System, true, true)]
    [InlineData(ThemeMode.System, false, false)]
    public void 深淺主題依系統設定解析(ThemeMode mode, bool systemLight, bool expectedLight)
    {
        Assert.Equal(expectedLight, ThemeModes.ResolveLight(mode, systemLight));
    }

    [Theory]
    [InlineData("82%", "82", "%")]
    [InlineData("+45.0W", "+45.0", "W")]
    [InlineData("-12.3W", "-12.3", "W")]
    [InlineData("45.2KB", "45.2", "KB")]
    [InlineData("3.2GB", "3.2", "GB")]
    [InlineData("72°C", "72", "°C")]
    [InlineData("3.4GHz", "3.4", "GHz")]
    [InlineData("1204rpm", "1204", "rpm")]
    [InlineData("--", "--", "")]
    [InlineData("0:45", "0:45", "")]
    [InlineData("充電中", "充電中", "")]
    [InlineData("", "", "")]
    public void 數值與單位拆分(string input, string expectedNumber, string expectedUnit)
    {
        (string number, string unit) = ValueDisplay.Split(input);

        Assert.Equal(expectedNumber, number);
        Assert.Equal(expectedUnit, unit);
    }

    [Fact]
    public void 整體警示等級取所有欄位中最嚴重的狀態()
    {
        var goodOnly = new[]
        {
            new BarColumn(new BarCell("CPU", "12%", Level.Good), new BarCell("RAM", "50%", Level.Info), "100%"),
        };
        var withWarn = new[]
        {
            new BarColumn(new BarCell("CPU", "70%", Level.Warn), new BarCell("RAM", "50%", Level.Good), "100%"),
        };
        var withCritical = new[]
        {
            new BarColumn(new BarCell("CPU", "70%", Level.Warn), new BarCell("RAM", "95%", Level.Critical), "100%"),
        };

        Assert.Equal(Level.Neutral, BarTheme.OverallLevel(goodOnly));
        Assert.Equal(Level.Warn, BarTheme.OverallLevel(withWarn));
        Assert.Equal(Level.Critical, BarTheme.OverallLevel(withCritical));
    }

    [Fact]
    public void 波形歷史環狀緩衝區維持固定容量並由舊到新輸出()
    {
        var history = new MetricHistory(capacity: 4);
        history.Add(10);
        history.Add(null);
        history.Add(double.NaN);
        history.Add(25);
        history.Add(120); // 超過上限應夾在 100
        history.Add(-5);  // 低於下限應夾在 0
        history.Add(42);

        Assert.Equal(4, history.Count);
        Assert.Equal([25.0, 100.0, 0.0, 42.0], history.Snapshot());
    }

    [Fact]
    public void 百分比與溫度欄位會產生進度比例_速率與資訊欄位不產生()
    {
        var snapshot = new MetricsSnapshot(
            Battery: new BatterySnapshot(HasBattery: true, Percent: 80, IsPluggedIn: true, IsCharging: false, SecondsRemaining: null),
            CpuPercent: 25,
            RamPercent: 60,
            DiskReadBytesPerSecond: 1024,
            DiskWriteBytesPerSecond: 2048,
            Network: new NetworkRate(512, 1024))
        with
        {
            BatteryDetail = new BatteryDetail(PowerWatts: 30, HealthPercent: 90, SecondsToFull: null),
            GpuPercent = 40,
            GpuMemoryBytes = 2L * 1024 * 1024 * 1024,
            Sensors = new SensorValues(CpuTemperature: 65, GpuTemperature: 100, CpuPowerWatts: 20, GpuPowerWatts: 40, GpuClockMHz: 1800, Fans: []),
        };

        var columns = BarContent.Build(snapshot, VisibleItems.All);

        // 0: 電池 (電量 80%, 電源 已接上)
        Assert.Equal(0.8, columns[0].TopRatio!.Value, precision: 3);
        Assert.Null(columns[0].BottomRatio);

        // 1: 電池詳情 (功耗, 健康 90%)
        Assert.Null(columns[1].TopRatio);
        Assert.Equal(0.9, columns[1].BottomRatio!.Value, precision: 3);

        // 2: CPU 25%, RAM 60%
        Assert.Equal(0.25, columns[2].TopRatio!.Value, precision: 3);
        Assert.Equal(0.60, columns[2].BottomRatio!.Value, precision: 3);

        // 3: GPU 40%, 顯存
        Assert.Equal(0.40, columns[3].TopRatio!.Value, precision: 3);
        Assert.Null(columns[3].BottomRatio);

        // 4: 溫度 (65°C -> 0.5, 100°C -> 1.0)
        Assert.Equal(0.5, columns[4].TopRatio!.Value, precision: 3);
        Assert.Equal(1.0, columns[4].BottomRatio!.Value, precision: 3);
    }

    [Theory]
    [InlineData(BarVisualStyle.Obsidian)]
    [InlineData(BarVisualStyle.HudMatrix)]
    [InlineData(BarVisualStyle.Editorial)]
    public void 新版三種風格版面計算_涵蓋左側標頭_欄間分隔線與穩定寬度(BarVisualStyle style)
    {
        static int MeasureLabel(string s) => s.Length * 9;
        static int MeasureValue(string s) => s.Length * 8;
        static int MeasureUnit(string s) => s.Length * 7;

        var idle = new[]
        {
            new BarColumn(new BarCell("CPU", "5%", Level.Good), new BarCell("RAM", "9%", Level.Good), "100%", 0.05, 0.09),
            new BarColumn(new BarCell("上傳", "1.1KB", Level.Good), new BarCell("下載", "8.6KB", Level.Good), "1023KB"),
        };
        var busy = new[]
        {
            new BarColumn(new BarCell("CPU", "100%", Level.Critical), new BarCell("RAM", "99%", Level.Critical), "100%", 1.0, 0.99),
            new BarColumn(new BarCell("上傳", "999KB", Level.Warn), new BarCell("下載", "100MB", Level.Critical), "1023KB"),
        };

        StyledGridMetrics metrics = StyledGridMetrics.For(style, 96, taskbar: false);
        StyledGridResult rIdle = GridLayout.ComputeStyled(idle, MeasureLabel, MeasureValue, MeasureUnit, 14, 16, 13, metrics);
        StyledGridResult rBusy = GridLayout.ComputeStyled(busy, MeasureLabel, MeasureValue, MeasureUnit, 14, 16, 13, metrics);

        Assert.Equal(rIdle.Size, rBusy.Size);
        Assert.True(rIdle.HeaderRect.Width > 0);
        Assert.Single(rIdle.DividerXs);
        Assert.Equal(4, rIdle.Cells.Count);

        if (style == BarVisualStyle.Editorial)
        {
            Assert.True(rIdle.Cells[0].TickRect.Width > 0);
            Assert.Equal(Rectangle.Empty, rIdle.Cells[0].MeterRect);
        }
        else
        {
            Assert.True(rIdle.Cells[0].MeterRect.Width > 0);
            Assert.Equal(Rectangle.Empty, rIdle.Cells[2].MeterRect); // 速率欄不畫進度條
        }
    }

    [Theory]
    [InlineData(BarVisualStyle.Obsidian, false)]
    [InlineData(BarVisualStyle.Obsidian, true)]
    [InlineData(BarVisualStyle.HudMatrix, false)]
    [InlineData(BarVisualStyle.HudMatrix, true)]
    [InlineData(BarVisualStyle.Editorial, false)]
    [InlineData(BarVisualStyle.Editorial, true)]
    public void 繪製三種風格與深淺主題到點陣圖_不擲出例外且確實產出畫面(BarVisualStyle style, bool light)
    {
        var snapshot = new MetricsSnapshot(
            Battery: new BatterySnapshot(HasBattery: true, Percent: 82, IsPluggedIn: true, IsCharging: true, SecondsRemaining: null),
            CpuPercent: 24,
            RamPercent: 61,
            DiskReadBytesPerSecond: 45.2 * 1024,
            DiskWriteBytesPerSecond: 3.9 * 1024 * 1024,
            Network: new NetworkRate(1.1 * 1024, 8.6 * 1024))
        with
        {
            BatteryDetail = new BatteryDetail(45.0, 91, 2700),
            GpuPercent = 35,
            GpuMemoryBytes = 3.2 * 1024 * 1024 * 1024,
        };

        var columns = BarContent.Build(snapshot, VisibleItems.Default);
        using ThemeFontSet fonts = ThemeFontSet.Create(9.25f, 96);

        StyledGridResult layout;
        using (var probeBmp = new Bitmap(1, 1))
        using (Graphics measureG = Graphics.FromImage(probeBmp))
        {
            int labelH = BarRenderer.MeasureFloatingText(measureG, "電A", fonts.Label).Height;
            int valueH = BarRenderer.MeasureFloatingText(measureG, "00", fonts.Value).Height;
            int unitH = BarRenderer.MeasureFloatingText(measureG, "%W", fonts.Unit).Height;

            StyledGridMetrics metrics = StyledGridMetrics.For(style, 96, taskbar: false);
            layout = GridLayout.ComputeStyled(
                columns,
                s => BarRenderer.MeasureFloatingText(measureG, s, fonts.Label).Width,
                s => BarRenderer.MeasureFloatingText(measureG, s, fonts.Value).Width,
                s => BarRenderer.MeasureFloatingText(measureG, s, fonts.Unit).Width,
                labelH,
                valueH,
                unitH,
                metrics);
        }

        ThemePalette palette = BarTheme.ForFloating(light);
        using var bmp = new Bitmap(layout.Size.Width, layout.Size.Height);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            BarRenderer.PaintFloating(
                g,
                layout.Size,
                layout,
                fonts,
                palette,
                style,
                BarTheme.OverallLevel(columns),
                [14, 18, 22, 19, 25, 24],
                pulseBright: true,
                drawOuterBorder: true,
                cornerRadius: BarRenderer.CornerRadiusFor(style, 96));
        }

        // 確認實際有畫出與底色明顯不同的文字／儀表像素
        int foregroundPixels = 0;
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                Color c = bmp.GetPixel(x, y);
                int diff = Math.Abs(c.R - palette.PanelSolid.R) + Math.Abs(c.G - palette.PanelSolid.G) + Math.Abs(c.B - palette.PanelSolid.B);
                if (diff > 60)
                {
                    foregroundPixels++;
                }
            }
        }

        Assert.True(foregroundPixels > 200, $"{style}/{light} 前景像素過少：{foregroundPixels}");

        // 若測試環境指定 RENDER_PREVIEW_DIR，順便匯出實際 GDI+ 繪製結果供肉眼檢查外觀。
        string? previewDir = Environment.GetEnvironmentVariable("RENDER_PREVIEW_DIR");
        if (!string.IsNullOrEmpty(previewDir))
        {
            Directory.CreateDirectory(previewDir);
            string suffix = light ? "light" : "dark";
            bmp.Save(Path.Combine(previewDir, $"{style}-{suffix}.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
