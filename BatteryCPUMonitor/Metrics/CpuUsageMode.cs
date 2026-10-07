namespace BatteryCPUMonitor.Metrics;

/// <summary>CPU 使用率的算法。兩種算法的數字常常差很多，哪一種「對」取決於你拿它跟誰比。</summary>
public enum CpuUsageMode
{
    /// <summary>
    /// 依處理器的忙碌時間：忙碌時間 ÷ 總時間（跨所有邏輯處理器）。
    /// 新版 Windows 11 的工作管理員用的是這種算法，所以預設採用。
    /// </summary>
    Time,

    /// <summary>
    /// 依處理器效能（Processor Utility）：把 CPU 加速（Turbo）與降頻算進去。
    /// CPU 跑在標稱頻率以上時，數字會比忙碌時間高；舊版 Windows 10 的工作管理員用這種算法。
    /// </summary>
    Utility,
}

internal static class CpuUsageModes
{
    public const string TimeName = "time";
    public const string UtilityName = "utility";

    /// <summary>設定檔裡存的是文字；看不懂的值一律當作預設的「忙碌時間」，不要因為一個壞值讓整個設定檔作廢。</summary>
    public static CpuUsageMode Parse(string? text) =>
        string.Equals(text?.Trim(), UtilityName, StringComparison.OrdinalIgnoreCase) ? CpuUsageMode.Utility : CpuUsageMode.Time;

    public static string ToText(CpuUsageMode mode) => mode == CpuUsageMode.Utility ? UtilityName : TimeName;

    /// <summary>
    /// 依算法挑出要顯示的數值。選定的算法暫時沒有數值時（剛啟動、計數器不可用）改用另一種，不要顯示空白。
    /// </summary>
    public static double? Choose(CpuUsageMode mode, double? utility, double? time) =>
        mode == CpuUsageMode.Utility ? utility ?? time : time ?? utility;
}
