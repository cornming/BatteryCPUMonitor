using System.Globalization;
using BatteryCPUMonitor.Metrics;

namespace BatteryCPUMonitor;

/// <summary>橫條上的一段文字與它的警示等級。</summary>
internal readonly record struct BarSegment(string Text, Level Level);

/// <summary>決定橫條上要顯示的文字與各段的警示等級。純計算，不碰畫面。</summary>
internal static class BarText
{
    private const string Separator = " / ";

    /// <summary>
    /// 例如「電量:82% / 充電中 / CPU:12% / RAM:61%」，電量、CPU、RAM 各自帶有警示等級；
    /// 沒有電池的電腦不顯示電量。
    /// </summary>
    public static IReadOnlyList<BarSegment> Build(BatterySnapshot battery, double? cpuPercent, double? ramPercent)
    {
        var segments = new List<BarSegment>(9);

        void Add(string text, Level level)
        {
            if (segments.Count > 0)
            {
                segments.Add(new BarSegment(Separator, Level.Neutral));
            }

            segments.Add(new BarSegment(text, level));
        }

        if (battery.HasBattery)
        {
            Level batteryLevel = Thresholds.ForBattery(battery);
            Add($"電量:{Whole(battery.Percent)}%", batteryLevel);

            if (battery.IsCharging)
            {
                Add("充電中", batteryLevel);
            }
            else if (battery.IsPluggedIn)
            {
                Add("已接電源", batteryLevel);
            }
        }

        Add($"CPU:{Whole(cpuPercent)}%", Thresholds.ForCpu(cpuPercent));
        Add($"RAM:{Whole(ramPercent)}%", Thresholds.ForRam(ramPercent));

        return segments;
    }

    /// <summary>不含顏色資訊的整行文字。</summary>
    public static string Format(BatterySnapshot battery, double? cpuPercent, double? ramPercent) =>
        Plain(Build(battery, cpuPercent, ramPercent));

    public static string Plain(IEnumerable<BarSegment> segments) =>
        string.Concat(segments.Select(segment => segment.Text));

    private static string Whole(double? value) =>
        value is null
            ? "--"
            : ((int)Math.Round(value.Value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
}
