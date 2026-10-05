using System.Globalization;
using BatteryCPUMonitor.Metrics;

namespace BatteryCPUMonitor;

/// <summary>決定橫條上要顯示的文字與顏色。純計算，不碰畫面。</summary>
internal static class BarText
{
    private const string Separator = " / ";

    /// <summary>例如「電量:82% / 充電中 / CPU:12% / RAM:61%」；沒有電池的電腦不顯示電量。</summary>
    public static string Format(BatterySnapshot battery, double? cpuPercent, double? ramPercent)
    {
        var parts = new List<string>(4);

        if (battery.HasBattery)
        {
            parts.Add($"電量:{Whole(battery.Percent)}%");

            if (battery.IsCharging)
            {
                parts.Add("充電中");
            }
            else if (battery.IsPluggedIn)
            {
                parts.Add("已接電源");
            }
        }

        parts.Add($"CPU:{Whole(cpuPercent)}%");
        parts.Add($"RAM:{Whole(ramPercent)}%");

        return string.Join(Separator, parts);
    }

    /// <summary>CPU 越忙字越紅：0% 為綠色，100% 為紅色。尚無數值時為綠色。</summary>
    public static (int R, int G, int B) ColorForCpu(double? cpuPercent)
    {
        int red = (int)Math.Round(255 * Math.Clamp(cpuPercent ?? 0, 0, 100) / 100, MidpointRounding.AwayFromZero);
        return (red, 255 - red, 0);
    }

    private static string Whole(double? value) =>
        value is null
            ? "--"
            : ((int)Math.Round(value.Value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
}
