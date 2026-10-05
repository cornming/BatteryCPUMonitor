namespace BatteryCPUMonitor.Metrics;

/// <summary>某一刻的電池狀態。</summary>
/// <param name="HasBattery">這台電腦有沒有電池（桌機為 false）。</param>
/// <param name="Percent">電量 0–100；讀不到時為 null。</param>
/// <param name="IsPluggedIn">是否接著外部電源。</param>
/// <param name="IsCharging">是否正在充電（接著電源但已充飽時為 false）。</param>
/// <param name="SecondsRemaining">系統估計的剩餘秒數；充電中或尚未估出時為 null。</param>
internal readonly record struct BatterySnapshot(
    bool HasBattery,
    int? Percent,
    bool IsPluggedIn,
    bool IsCharging,
    int? SecondsRemaining)
{
    private const byte FlagCharging = 8;
    private const byte FlagNoBattery = 128;
    private const byte Unknown = 255;
    private const uint UnknownLifeTime = 0xFFFFFFFF;

    /// <summary>把 Win32 SYSTEM_POWER_STATUS 的原始欄位轉成好讀的狀態。</summary>
    public static BatterySnapshot FromRaw(byte acLineStatus, byte batteryFlag, byte lifePercent, uint lifeTimeSeconds)
    {
        bool flagKnown = batteryFlag != Unknown;
        bool noBattery = flagKnown && (batteryFlag & FlagNoBattery) != 0;
        int? percent = lifePercent <= 100 ? lifePercent : null;

        return new BatterySnapshot(
            HasBattery: !noBattery && percent is not null,
            Percent: percent,
            IsPluggedIn: acLineStatus == 1,
            IsCharging: flagKnown && (batteryFlag & FlagCharging) != 0,
            SecondsRemaining: lifeTimeSeconds == UnknownLifeTime ? null : (int)Math.Min(lifeTimeSeconds, int.MaxValue));
    }
}
