namespace BatteryCPUMonitor.Metrics;

internal static class CpuClock
{
    /// <summary>
    /// 目前的 CPU 頻率（MHz）= 標稱頻率 × 效能百分比 ÷ 100。
    /// 渦輪加速時效能百分比會超過 100，所以頻率會高於標稱值。任何一個數值缺漏或不合理就回傳 null。
    /// </summary>
    public static double? CurrentMegahertz(double? nominalMegahertz, double? performancePercent)
    {
        if (nominalMegahertz is not double nominal || performancePercent is not double performance
            || !double.IsFinite(nominal) || !double.IsFinite(performance)
            || nominal <= 0 || performance <= 0)
        {
            return null;
        }

        double current = nominal * performance / 100.0;
        return current is > 0 and <= 20000 ? current : null;
    }
}
