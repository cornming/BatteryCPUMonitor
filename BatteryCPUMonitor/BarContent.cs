using System.Globalization;
using BatteryCPUMonitor.Metrics;

namespace BatteryCPUMonitor;

/// <summary>橫條上的一格：標籤、數值，以及數值的警示等級。</summary>
internal readonly record struct BarCell(string Label, string Value, Level Level);

/// <summary>橫條上的一欄，上下各一格。</summary>
/// <param name="ValueTemplate">
/// 用來預留數值寬度的範本字串（例如「100%」），數字位數變動時欄寬才不會跟著跳動。
/// </param>
internal readonly record struct BarColumn(BarCell Top, BarCell Bottom, string ValueTemplate);

/// <summary>使用者選擇要顯示哪些項目。</summary>
internal readonly record struct VisibleItems(bool Battery, bool CpuRam, bool Disk, bool Network)
{
    public static VisibleItems All => new(true, true, true, true);
}

/// <summary>決定橫條上要顯示的內容。純計算，不碰畫面。</summary>
internal static class BarContent
{
    private const string PercentTemplate = "100%";
    private const string RateTemplate = "1023KB";

    /// <summary>
    /// 依序產生各欄：電池、CPU／RAM、磁碟讀寫、網路上下傳。
    /// 沒有電池的電腦不會有電池欄；全部被關掉時至少保留 CPU／RAM。
    /// </summary>
    public static IReadOnlyList<BarColumn> Build(MetricsSnapshot m, VisibleItems visible)
    {
        var columns = new List<BarColumn>(4);

        if (visible.Battery && m.Battery.HasBattery)
        {
            Level level = Thresholds.ForBattery(m.Battery);
            columns.Add(new BarColumn(
                new BarCell("電量", Percent(m.Battery.Percent), level),
                PowerCell(m.Battery, level),
                PercentTemplate));
        }

        if (visible.CpuRam || (columns.Count == 0 && !visible.Disk && !visible.Network))
        {
            columns.Add(new BarColumn(
                new BarCell("CPU", Percent(m.CpuPercent), Thresholds.ForCpu(m.CpuPercent)),
                new BarCell("RAM", Percent(m.RamPercent), Thresholds.ForRam(m.RamPercent)),
                PercentTemplate));
        }

        if (visible.Disk)
        {
            columns.Add(new BarColumn(
                new BarCell("讀取", ByteRate.Format(m.DiskReadBytesPerSecond), Thresholds.ForDiskRate(m.DiskReadBytesPerSecond)),
                new BarCell("寫入", ByteRate.Format(m.DiskWriteBytesPerSecond), Thresholds.ForDiskRate(m.DiskWriteBytesPerSecond)),
                RateTemplate));
        }

        if (visible.Network)
        {
            double? up = m.Network?.UploadBytesPerSecond;
            double? down = m.Network?.DownloadBytesPerSecond;
            columns.Add(new BarColumn(
                new BarCell("上傳", ByteRate.Format(up), Thresholds.ForNetworkRate(up)),
                new BarCell("下載", ByteRate.Format(down), Thresholds.ForNetworkRate(down)),
                RateTemplate));
        }

        return columns;
    }

    /// <summary>系統匣圖示的提示文字，例如「電量 82% · CPU 12% · RAM 61% · ↑1.1KB ↓8.6KB」。</summary>
    public static string TrayText(MetricsSnapshot m)
    {
        var parts = new List<string>(4);

        if (m.Battery.HasBattery)
        {
            parts.Add($"電量 {Percent(m.Battery.Percent)}");
        }

        parts.Add($"CPU {Percent(m.CpuPercent)}");
        parts.Add($"RAM {Percent(m.RamPercent)}");

        if (m.Network is NetworkRate rate)
        {
            parts.Add($"↑{ByteRate.Format(rate.UploadBytesPerSecond)} ↓{ByteRate.Format(rate.DownloadBytesPerSecond)}");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>電池欄的下格：充電中、已接電源，或估計的剩餘時間。</summary>
    private static BarCell PowerCell(BatterySnapshot battery, Level level)
    {
        if (battery.IsCharging)
        {
            return new BarCell("電源", "充電中", level);
        }

        if (battery.IsPluggedIn)
        {
            return new BarCell("電源", "已接上", level);
        }

        return battery.SecondsRemaining is int seconds
            ? new BarCell("剩餘", Duration(seconds), level)
            : new BarCell("電源", "電池", level);
    }

    private static string Percent(double? value) =>
        value is null
            ? "--"
            : ((int)Math.Round(value.Value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture) + "%";

    /// <summary>秒數轉成「時:分」，例如 5400 秒為「1:30」。</summary>
    internal static string Duration(int seconds)
    {
        int minutes = Math.Max(0, seconds) / 60;
        return string.Create(CultureInfo.InvariantCulture, $"{minutes / 60}:{minutes % 60:00}");
    }
}

/// <summary>把「位元組／秒」轉成好讀的字串。為了節省空間不附「/s」。</summary>
internal static class ByteRate
{
    /// <summary>例如 46285 → 「45.2KB」、4089446 → 「3.9MB」；沒有數值時為「--」。</summary>
    public static string Format(double? bytesPerSecond)
    {
        if (bytesPerSecond is not double value || !double.IsFinite(value) || value < 0)
        {
            return "--";
        }

        double kb = value / 1024;
        if (kb < 1023.5)
        {
            return Number(kb) + "KB";
        }

        double mb = kb / 1024;
        return mb < 1023.5 ? Number(mb) + "MB" : Number(mb / 1024) + "GB";
    }

    // 未滿 100 顯示一位小數，100 以上顯示整數，字串最長六個字元。
    private static string Number(double value) =>
        value >= 99.95
            ? Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.0", CultureInfo.InvariantCulture);
}
