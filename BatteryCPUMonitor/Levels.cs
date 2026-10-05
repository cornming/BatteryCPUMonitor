using BatteryCPUMonitor.Metrics;

namespace BatteryCPUMonitor;

/// <summary>數值的警示等級，決定顯示的顏色。</summary>
public enum Level
{
    /// <summary>不帶狀態的文字（分隔符號、尚無數值）。</summary>
    Neutral,

    /// <summary>正常，綠色。</summary>
    Good,

    /// <summary>需要留意，黃色。</summary>
    Warn,

    /// <summary>吃緊，紅色。</summary>
    Critical,
}

/// <summary>各項數值的警示門檻。免設定，直接採用固定的預設值。</summary>
internal static class Thresholds
{
    public const double CpuWarn = 60;
    public const double CpuCritical = 85;

    public const double RamWarn = 80;
    public const double RamCritical = 90;

    public const int BatteryWarn = 40;
    public const int BatteryCritical = 20;

    public static Level ForCpu(double? percent) => Rising(percent, CpuWarn, CpuCritical);

    public static Level ForRam(double? percent) => Rising(percent, RamWarn, RamCritical);

    /// <summary>電量越低越緊急；只要接著電源就視為正常。</summary>
    public static Level ForBattery(BatterySnapshot battery)
    {
        if (!battery.HasBattery || battery.Percent is not int percent)
        {
            return Level.Neutral;
        }

        if (battery.IsPluggedIn || battery.IsCharging)
        {
            return Level.Good;
        }

        return percent <= BatteryCritical ? Level.Critical
            : percent <= BatteryWarn ? Level.Warn
            : Level.Good;
    }

    private static Level Rising(double? percent, double warn, double critical) =>
        percent is not double value ? Level.Neutral
        : value >= critical ? Level.Critical
        : value >= warn ? Level.Warn
        : Level.Good;
}
