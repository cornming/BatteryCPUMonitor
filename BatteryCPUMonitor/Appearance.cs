using System.Drawing;

namespace BatteryCPUMonitor;

/// <summary>橫條的視覺風格。純資料定義，不碰畫面。</summary>
public enum BarVisualStyle
{
    /// <summary>曜石儀表：左側狀態導軌、極細 2px 微進度條、模組短分隔線。</summary>
    Obsidian,

    /// <summary>航電矩陣：四角校準框線、8 格分段式 LED 負載條、即時迷你 CPU 波形圖。</summary>
    HudMatrix,

    /// <summary>極簡雙色：幾何星芒標記、標籤旁垂直狀態微條、維持最小佔位面積。</summary>
    Editorial,
}

/// <summary>深淺色主題模式。</summary>
public enum ThemeMode
{
    /// <summary>暖曜黑（預設）。</summary>
    Dark,

    /// <summary>暖象牙白。</summary>
    Light,

    /// <summary>跟隨 Windows 系統深淺色設定。</summary>
    System,
}

/// <summary>外觀風格名稱與設定檔字串之間的轉換。</summary>
internal static class BarVisualStyles
{
    public const string ObsidianName = "obsidian";
    public const string HudMatrixName = "hud";
    public const string EditorialName = "editorial";

    public static BarVisualStyle Parse(string? name) =>
        name?.Trim().ToLowerInvariant() switch
        {
            HudMatrixName => BarVisualStyle.HudMatrix,
            EditorialName => BarVisualStyle.Editorial,
            _ => BarVisualStyle.Obsidian,
        };

    public static string ToText(BarVisualStyle style) => style switch
    {
        BarVisualStyle.HudMatrix => HudMatrixName,
        BarVisualStyle.Editorial => EditorialName,
        _ => ObsidianName,
    };
}

/// <summary>主題模式名稱與設定檔字串之間的轉換。</summary>
internal static class ThemeModes
{
    public const string DarkName = "dark";
    public const string LightName = "light";
    public const string SystemName = "system";

    public static ThemeMode Parse(string? name) =>
        name?.Trim().ToLowerInvariant() switch
        {
            LightName => ThemeMode.Light,
            SystemName => ThemeMode.System,
            _ => ThemeMode.Dark,
        };

    public static string ToText(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => LightName,
        ThemeMode.System => SystemName,
        _ => DarkName,
    };

    /// <summary>依使用者選擇與系統是否為淺色，決定實際要畫深色或淺色。</summary>
    public static bool ResolveLight(ThemeMode mode, bool systemLight) => mode switch
    {
        ThemeMode.Light => true,
        ThemeMode.System => systemLight,
        _ => false,
    };
}

/// <summary>
/// 整套介面共用的色票（浮動橫條、右鍵選單、診斷視窗與工作列小工具）。
/// 參考 Anthropic / Claude 的暖曜黑、暖象牙白、陶土橘與鋼青藍色系。
/// </summary>
internal sealed record ThemePalette(
    bool IsLight,
    Color PanelTop,
    Color PanelBottom,
    Color PanelSolid,
    Color Border,
    Color TopSpecular,
    Color Divider,
    Color GridLine,
    Color Track,
    Color Label,
    Color Unit,
    Color Neutral,
    Color Info,
    Color Good,
    Color Warn,
    Color Critical,
    Color Accent,
    Color MenuBackground,
    Color MenuHover,
    Color MenuText,
    Color MenuMuted,
    Color MenuBorder,
    Color DialogBackground,
    Color DialogCard,
    Color DialogText,
    Color DialogMuted,
    Color DialogButtonBg,
    Color DialogButtonHover)
{
    public Color For(Level level) => level switch
    {
        Level.Good => Good,
        Level.Info => Info,
        Level.Warn => Warn,
        Level.Critical => Critical,
        _ => Neutral,
    };

    /// <summary>整體狀態燈（左側導軌／脈動點／星芒）的顏色：正常時為品牌陶土橘，有警示時切換為琥珀黃或珊瑚紅。</summary>
    public Color ForStatusBeacon(Level overall) => overall switch
    {
        Level.Critical => Critical,
        Level.Warn => Warn,
        _ => Accent,
    };
}

/// <summary>集中管理深／淺兩套色票與狀態計算。純計算，不碰系統 API。</summary>
internal static class BarTheme
{
    /// <summary>工作列透明色鍵（深色工作列用暖深灰，減少文字邊緣色邊）。</summary>
    public static readonly Color TaskbarDarkKey = Color.FromArgb(38, 37, 35);

    /// <summary>工作列透明色鍵（淺色工作列用暖淺灰）。</summary>
    public static readonly Color TaskbarLightKey = Color.FromArgb(218, 215, 207);

    public static readonly ThemePalette Dark = new(
        IsLight: false,
        PanelTop: Color.FromArgb(29, 28, 26),            // #1D1C1A
        PanelBottom: Color.FromArgb(20, 20, 19),         // #141413 Anthropic Dark
        PanelSolid: Color.FromArgb(22, 21, 20),          // #161514
        Border: Color.FromArgb(52, 49, 44),              // #34312C
        TopSpecular: Color.FromArgb(20, 250, 249, 245),  // 細微頂部高光
        Divider: Color.FromArgb(46, 44, 40),             // #2E2C28
        GridLine: Color.FromArgb(10, 250, 249, 245),     // 航電細微格線
        Track: Color.FromArgb(42, 40, 36),               // #2A2824
        Label: Color.FromArgb(158, 154, 144),            // #9E9A90 暖灰標籤
        Unit: Color.FromArgb(128, 124, 115),             // #807C73 柔化單位
        Neutral: Color.FromArgb(143, 139, 130),          // #8F8B82
        Info: Color.FromArgb(121, 168, 214),             // #79A8D6 鋼青藍
        Good: Color.FromArgb(123, 184, 130),             // #7BB882 柔霧綠
        Warn: Color.FromArgb(229, 169, 60),              // #E5A93C 琥珀黃
        Critical: Color.FromArgb(228, 93, 71),           // #E45D47 珊瑚紅
        Accent: Color.FromArgb(217, 119, 87),            // #D97757 Anthropic Terracotta
        MenuBackground: Color.FromArgb(24, 23, 21),      // #181715
        MenuHover: Color.FromArgb(42, 39, 35),           // #2A2723
        MenuText: Color.FromArgb(245, 243, 236),         // #F5F3EC
        MenuMuted: Color.FromArgb(142, 138, 128),        // #8E8A80
        MenuBorder: Color.FromArgb(56, 53, 47),          // #38352F
        DialogBackground: Color.FromArgb(20, 20, 19),    // #141413
        DialogCard: Color.FromArgb(29, 28, 26),          // #1D1C1A
        DialogText: Color.FromArgb(248, 246, 240),       // #F8F6F0
        DialogMuted: Color.FromArgb(158, 154, 144),      // #9E9A90
        DialogButtonBg: Color.FromArgb(38, 36, 32),      // #262420
        DialogButtonHover: Color.FromArgb(52, 49, 44));  // #34312C

    public static readonly ThemePalette Light = new(
        IsLight: true,
        PanelTop: Color.FromArgb(252, 251, 248),         // #FCFBF8
        PanelBottom: Color.FromArgb(242, 239, 231),      // #F2EFE7
        PanelSolid: Color.FromArgb(250, 249, 245),       // #FAF9F5 Anthropic Light
        Border: Color.FromArgb(212, 208, 195),           // #D4D0C3
        TopSpecular: Color.FromArgb(180, 255, 255, 255),
        Divider: Color.FromArgb(222, 218, 207),          // #DEDACF
        GridLine: Color.FromArgb(12, 20, 20, 19),
        Track: Color.FromArgb(227, 224, 213),            // #E3E0D5
        Label: Color.FromArgb(110, 106, 97),             // #6E6A61
        Unit: Color.FromArgb(142, 137, 126),             // #8E897E
        Neutral: Color.FromArgb(128, 123, 112),          // #807B70
        Info: Color.FromArgb(56, 104, 153),              // #386899
        Good: Color.FromArgb(54, 116, 68),               // #367444
        Warn: Color.FromArgb(174, 109, 0),               // #AE6D00
        Critical: Color.FromArgb(194, 65, 44),           // #C2412C
        Accent: Color.FromArgb(201, 101, 69),            // #C96545 深陶土橘
        MenuBackground: Color.FromArgb(250, 249, 245),   // #FAF9F5
        MenuHover: Color.FromArgb(236, 233, 223),        // #ECE9DF
        MenuText: Color.FromArgb(20, 20, 19),            // #141413
        MenuMuted: Color.FromArgb(117, 113, 104),        // #757168
        MenuBorder: Color.FromArgb(212, 208, 195),       // #D4D0C3
        DialogBackground: Color.FromArgb(250, 249, 245), // #FAF9F5
        DialogCard: Color.FromArgb(241, 239, 231),       // #F1EFE7
        DialogText: Color.FromArgb(20, 20, 19),          // #141413
        DialogMuted: Color.FromArgb(110, 106, 97),       // #6E6A61
        DialogButtonBg: Color.FromArgb(232, 229, 218),   // #E8E5DA
        DialogButtonHover: Color.FromArgb(220, 216, 204));

    /// <summary>工作列深色色票：提高亮部對比，在 Windows 半透明深色工作列上依然銳利好讀。</summary>
    public static readonly ThemePalette TaskbarDark = Dark with
    {
        Label = Color.FromArgb(182, 178, 168),
        Unit = Color.FromArgb(148, 144, 134),
        Good = Color.FromArgb(136, 204, 144),
        Info = Color.FromArgb(138, 184, 230),
        Warn = Color.FromArgb(240, 182, 72),
        Critical = Color.FromArgb(240, 104, 82),
        Divider = Color.FromArgb(72, 69, 62),
        Track = Color.FromArgb(62, 59, 54),
    };

    /// <summary>工作列淺色色票：在 Windows 淺色工作列上保持清晰對比。</summary>
    public static readonly ThemePalette TaskbarLight = Light with
    {
        Label = Color.FromArgb(76, 73, 66),
        Unit = Color.FromArgb(108, 104, 96),
        Divider = Color.FromArgb(178, 174, 164),
        Track = Color.FromArgb(192, 189, 179),
    };

    public static ThemePalette ForFloating(bool light) => light ? Light : Dark;

    public static ThemePalette ForTaskbar(bool light) => light ? TaskbarLight : TaskbarDark;

    /// <summary>彙整目前所有欄位的最高警示等級，供左側狀態燈／導軌變色使用。</summary>
    public static Level OverallLevel(IReadOnlyList<BarColumn> columns)
    {
        Level worst = Level.Neutral;
        foreach (BarColumn column in columns)
        {
            worst = Worse(worst, column.Top.Level);
            worst = Worse(worst, column.Bottom.Level);
            if (worst == Level.Critical)
            {
                return Level.Critical;
            }
        }

        return worst;
    }

    private static Level Worse(Level current, Level candidate) => candidate switch
    {
        Level.Critical => Level.Critical,
        Level.Warn when current != Level.Critical => Level.Warn,
        _ => current,
    };
}

/// <summary>把數值字串拆成「主數字」與「單位後綴」，讓單位能以較柔和的視覺層次呈現。</summary>
internal static class ValueDisplay
{
    private static readonly string[] KnownUnits = ["GHz", "MHz", "rpm", "°C", "KB", "MB", "GB", "%", "W"];

    public static (string Number, string Unit) Split(string value)
    {
        if (string.IsNullOrEmpty(value) || value == "--")
        {
            return (value, string.Empty);
        }

        foreach (string unit in KnownUnits)
        {
            if (value.Length > unit.Length && value.EndsWith(unit, StringComparison.Ordinal))
            {
                string prefix = value[..^unit.Length];
                if (LooksNumeric(prefix))
                {
                    return (prefix, unit);
                }
            }
        }

        return (value, string.Empty);
    }

    private static bool LooksNumeric(string text)
    {
        bool hasDigit = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsAsciiDigit(c))
            {
                hasDigit = true;
            }
            else if (c is not ('.' or '+' or '-'))
            {
                return false;
            }
        }

        return hasDigit;
    }
}

/// <summary>
/// 固定長度的即時取樣歷史（0–100），供航電矩陣風格繪製迷你即時波形折線圖。
/// </summary>
internal sealed class MetricHistory
{
    private readonly double[] _samples;
    private int _count;
    private int _next;

    public MetricHistory(int capacity = 14)
    {
        if (capacity < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _samples = new double[capacity];
    }

    public int Capacity => _samples.Length;

    public int Count => _count;

    public void Add(double? value)
    {
        if (value is not double reading || !double.IsFinite(reading))
        {
            return;
        }

        _samples[_next] = Math.Clamp(reading, 0.0, 100.0);
        _next = (_next + 1) % _samples.Length;
        if (_count < _samples.Length)
        {
            _count++;
        }
    }

    /// <summary>由舊到新取出目前的取樣序列。</summary>
    public double[] Snapshot()
    {
        if (_count == 0)
        {
            return [];
        }

        var ordered = new double[_count];
        int start = _count == _samples.Length ? _next : 0;
        for (int i = 0; i < _count; i++)
        {
            ordered[i] = _samples[(start + i) % _samples.Length];
        }

        return ordered;
    }
}
