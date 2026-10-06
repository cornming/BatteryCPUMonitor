using System.Drawing;
using System.Globalization;

namespace BatteryCPUMonitor;

/// <summary>一個螢幕的基本資料。</summary>
/// <param name="DeviceName">Windows 給螢幕的裝置名稱，例如 <c>\\.\DISPLAY2</c>；拔掉再插回去仍是同一個名稱。</param>
internal readonly record struct ScreenInfo(string DeviceName, Rectangle Bounds, bool IsPrimary);

/// <summary>選單裡「嵌入工作列」底下的一個選項（一個螢幕）。</summary>
internal readonly record struct MonitorChoice(ScreenInfo Screen, string Label, bool Enabled, bool Checked);

/// <summary>決定要把內容嵌在哪個螢幕的工作列上。純計算，不碰畫面。</summary>
internal static class TaskbarTargets
{
    /// <summary>
    /// 找出實際要使用的螢幕：優先用指定的那個；沒有指定，或指定的螢幕目前沒接上時，改用主螢幕。
    /// 設定檔裡的指定不會因此被清掉，螢幕接回來之後自然會回到原本選的那個。
    /// </summary>
    public static ScreenInfo Resolve(string? preferredDevice, IReadOnlyList<ScreenInfo> screens)
    {
        if (screens.Count == 0)
        {
            return new ScreenInfo(string.Empty, Rectangle.Empty, IsPrimary: true);
        }

        if (!string.IsNullOrEmpty(preferredDevice))
        {
            foreach (ScreenInfo screen in screens)
            {
                if (string.Equals(screen.DeviceName, preferredDevice, StringComparison.OrdinalIgnoreCase))
                {
                    return screen;
                }
            }
        }

        foreach (ScreenInfo screen in screens)
        {
            if (screen.IsPrimary)
            {
                return screen;
            }
        }

        return screens[0];
    }

    /// <summary>
    /// 在一批副螢幕工作列當中，找出屬於這個螢幕的那一條：與螢幕範圍重疊最多的。
    /// 都沒有重疊（這個螢幕沒有顯示工作列）時回傳 null。
    /// </summary>
    public static int? IndexOfTaskbarOn(Rectangle screenBounds, IReadOnlyList<Rectangle> taskbars)
    {
        int? best = null;
        long bestArea = 0;

        for (int i = 0; i < taskbars.Count; i++)
        {
            Rectangle overlap = Rectangle.Intersect(screenBounds, taskbars[i]);
            long area = (long)overlap.Width * overlap.Height;
            if (area > bestArea)
            {
                (best, bestArea) = (i, area);
            }
        }

        return best;
    }

    /// <summary>
    /// Windows 設定畫面裡螢幕的編號，與裝置名稱結尾的數字一致（<c>\\.\DISPLAY2</c> 是螢幕 2）。
    /// 看不出數字時使用備用編號。
    /// </summary>
    public static int DisplayNumber(string deviceName, int fallbackNumber)
    {
        int end = deviceName.Length;
        int start = end;
        while (start > 0 && char.IsAsciiDigit(deviceName[start - 1]))
        {
            start--;
        }

        return start < end && int.TryParse(deviceName.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            ? number
            : fallbackNumber;
    }

    /// <summary>選單上的文字，例如「螢幕 2　2560×1440」；主螢幕加註「（主要）」，沒有工作列的加註「（沒有工作列）」。</summary>
    public static string Label(ScreenInfo screen, int fallbackNumber, bool hasTaskbar)
    {
        int number = DisplayNumber(screen.DeviceName, fallbackNumber);
        string primary = screen.IsPrimary ? "（主要）" : string.Empty;
        string size = string.Create(CultureInfo.InvariantCulture, $"{screen.Bounds.Width}×{screen.Bounds.Height}");
        string missing = hasTaskbar ? string.Empty : "（沒有工作列）";
        return $"螢幕 {number}{primary}　{size}{missing}";
    }

    /// <summary>
    /// 產生選單的各個選項，每個螢幕一項。沒有工作列的螢幕不能選；
    /// 目前嵌入中的那個螢幕打勾（浮動橫條時沒有任何一項打勾）。
    /// </summary>
    public static IReadOnlyList<MonitorChoice> Choices(
        IReadOnlyList<ScreenInfo> screens,
        Func<ScreenInfo, bool> hasTaskbar,
        bool taskbarMode,
        string? preferredDevice)
    {
        string current = Resolve(preferredDevice, screens).DeviceName;
        var choices = new List<MonitorChoice>(screens.Count);

        for (int i = 0; i < screens.Count; i++)
        {
            ScreenInfo screen = screens[i];
            bool available = hasTaskbar(screen);
            bool isCurrent = taskbarMode && string.Equals(screen.DeviceName, current, StringComparison.OrdinalIgnoreCase);
            choices.Add(new MonitorChoice(screen, Label(screen, i + 1, available), available, isCurrent));
        }

        return choices;
    }

    /// <summary>選了某個螢幕之後要存進設定的值：主螢幕存 null（永遠跟著目前的主螢幕走），其他螢幕存裝置名稱。</summary>
    public static string? SettingFor(ScreenInfo screen) => screen.IsPrimary ? null : screen.DeviceName;
}
