namespace BatteryCPUMonitor.Metrics;

/// <summary>電池的進階資訊。電池韌體沒有回報的項目為 null。</summary>
/// <param name="PowerWatts">目前的充放電功率（瓦）：充電為正、放電為負。</param>
/// <param name="HealthPercent">健康度 = 目前滿充容量 ÷ 出廠設計容量（0–100）。</param>
/// <param name="SecondsToFull">充電中時，依目前充電功率估計的充滿秒數。</param>
internal readonly record struct BatteryDetail(double? PowerWatts, double? HealthPercent, int? SecondsToFull);

/// <summary>由電池回報的原始數值算出進階資訊。純計算。</summary>
internal static class BatteryMath
{
    /// <summary>Windows 以這個值表示「容量未知」。</summary>
    public const uint UnknownCapacity = 0xFFFFFFFF;

    /// <summary>Windows 以這個值表示「功率未知」。</summary>
    public const int UnknownRate = int.MinValue;

    /// <summary>
    /// 彙整一顆或多顆電池的數值。
    /// </summary>
    /// <param name="batteries">
    /// 每顆電池的設計容量、滿充容量、目前容量（毫瓦時）與充放電功率（毫瓦）。
    /// </param>
    /// <param name="pluggedIn">是否接著外部電源。</param>
    /// <param name="charging">是否正在充電。</param>
    /// <remarks>
    /// 功率的正負號各家驅動程式不一致，所以不採信原始符號，改依電源狀態決定：
    /// 使用電池時一律為負（放電），充電中一律為正。
    /// </remarks>
    public static BatteryDetail? Combine(
        IReadOnlyCollection<(uint Designed, uint FullCharged, uint Remaining, int Rate)> batteries,
        bool pluggedIn,
        bool charging)
    {
        if (batteries.Count == 0)
        {
            return null;
        }

        long designed = 0, full = 0, remaining = 0, rate = 0;
        bool capacityKnown = true, remainingKnown = true, rateKnown = true;

        foreach ((uint d, uint f, uint r, int rt) in batteries)
        {
            if (d is 0 or UnknownCapacity || f is 0 or UnknownCapacity)
            {
                capacityKnown = false;
            }
            else
            {
                designed += d;
                full += f;
            }

            if (r == UnknownCapacity)
            {
                remainingKnown = false;
            }
            else
            {
                remaining += r;
            }

            if (rt == UnknownRate)
            {
                rateKnown = false;
            }
            else
            {
                rate += rt;
            }
        }

        if (!pluggedIn)
        {
            rate = -Math.Abs(rate);
        }
        else if (charging)
        {
            rate = Math.Abs(rate);
        }

        double? power = rateKnown ? rate / 1000.0 : null;
        double? health = capacityKnown ? Math.Min(100.0, full * 100.0 / designed) : null;

        int? secondsToFull = null;
        if (capacityKnown && remainingKnown && rateKnown && rate > 0 && full > remaining)
        {
            secondsToFull = (int)Math.Min(int.MaxValue, (full - remaining) * 3600 / rate);
        }

        return new BatteryDetail(power, health, secondsToFull);
    }
}
