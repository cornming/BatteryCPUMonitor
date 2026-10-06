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

    public const double GpuWarn = 60;
    public const double GpuCritical = 85;

    // 溫度（°C）。各家晶片的耐受度不同，這裡取一般筆電與桌機通用的保守值：
    // 超過黃色門檻代表正在高負載，超過紅色門檻代表快要降頻或需要檢查散熱。
    public const double CpuTemperatureWarn = 70;
    public const double CpuTemperatureCritical = 85;

    public const double GpuTemperatureWarn = 75;
    public const double GpuTemperatureCritical = 88;

    // 電池健康度越低越差：低於 80% 開始留意，低於 60% 建議考慮更換。
    public const double BatteryHealthWarn = 80;
    public const double BatteryHealthCritical = 60;

    // 磁碟與網路的顏色不是「警報」，而是讓人一眼看出有東西正在大量讀寫或傳輸。
    public const double DiskWarnBytesPerSecond = 10 * 1024 * 1024;
    public const double DiskCriticalBytesPerSecond = 100 * 1024 * 1024;

    public const double NetworkWarnBytesPerSecond = 1 * 1024 * 1024;
    public const double NetworkCriticalBytesPerSecond = 10 * 1024 * 1024;

    public static Level ForCpu(double? percent) => Rising(percent, CpuWarn, CpuCritical);

    public static Level ForRam(double? percent) => Rising(percent, RamWarn, RamCritical);

    public static Level ForGpu(double? percent) => Rising(percent, GpuWarn, GpuCritical);

    public static Level ForCpuTemperature(double? celsius) => Rising(celsius, CpuTemperatureWarn, CpuTemperatureCritical);

    public static Level ForGpuTemperature(double? celsius) => Rising(celsius, GpuTemperatureWarn, GpuTemperatureCritical);

    public static Level ForBatteryHealth(double? percent) =>
        percent is not double value ? Level.Neutral
        : value < BatteryHealthCritical ? Level.Critical
        : value < BatteryHealthWarn ? Level.Warn
        : Level.Good;

    public static Level ForDiskRate(double? bytesPerSecond) =>
        Rising(bytesPerSecond, DiskWarnBytesPerSecond, DiskCriticalBytesPerSecond);

    public static Level ForNetworkRate(double? bytesPerSecond) =>
        Rising(bytesPerSecond, NetworkWarnBytesPerSecond, NetworkCriticalBytesPerSecond);

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

    private static Level Rising(double? reading, double warn, double critical) =>
        reading is not double value ? Level.Neutral
        : value >= critical ? Level.Critical
        : value >= warn ? Level.Warn
        : Level.Good;
}
