using System.Runtime.InteropServices;

namespace BatteryCPUMonitor.Metrics;

/// <summary>
/// Windows 效能計數器（PDH）的精簡包裝：一次收集、逐一讀值。
/// 與工作管理員使用相同的資料來源，不需要系統管理員權限。
/// </summary>
internal sealed class PdhQuery : IDisposable
{
    private const int ErrorSuccess = 0;
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhFmtNoCap100 = 0x00008000;
    private const uint PdhCStatusValidData = 0;
    private const uint PdhCStatusNewData = 1;

    private IntPtr _query;

    private PdhQuery(IntPtr query) => _query = query;

    public static PdhQuery? TryOpen()
    {
        try
        {
            return PdhOpenQueryW(null, IntPtr.Zero, out IntPtr query) == ErrorSuccess ? new PdhQuery(query) : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// 加入一個計數器。路徑一律用英文名稱，任何語系的 Windows 都適用。
    /// 這台電腦沒有該計數器時回傳 null。
    /// </summary>
    public IntPtr? TryAdd(string englishCounterPath)
    {
        if (_query == IntPtr.Zero)
        {
            return null;
        }

        return PdhAddEnglishCounterW(_query, englishCounterPath, IntPtr.Zero, out IntPtr counter) == ErrorSuccess
            ? counter
            : null;
    }

    /// <summary>收集一筆樣本。速率類的計數器要收集兩次以後才讀得到值。</summary>
    public bool Collect() => _query != IntPtr.Zero && PdhCollectQueryData(_query) == ErrorSuccess;

    /// <summary>讀取最近一次收集到的值；還沒有有效資料時回傳 null。</summary>
    /// <param name="counter"><see cref="TryAdd"/> 回傳的計數器。</param>
    /// <param name="allowAbove100">百分比類計數器預設上限為 100；設為 true 可取得超過 100 的原始值。</param>
    public double? Read(IntPtr? counter, bool allowAbove100 = false)
    {
        if (_query == IntPtr.Zero || counter is not IntPtr handle)
        {
            return null;
        }

        uint format = allowAbove100 ? PdhFmtDouble | PdhFmtNoCap100 : PdhFmtDouble;
        if (PdhGetFormattedCounterValue(handle, format, IntPtr.Zero, out PDH_FMT_COUNTERVALUE value) != ErrorSuccess)
        {
            return null;
        }

        if (value.CStatus != PdhCStatusValidData && value.CStatus != PdhCStatusNewData)
        {
            return null;
        }

        return double.IsFinite(value.DoubleValue) ? value.DoubleValue : null;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }

    // DWORD CStatus 之後是一個含 double 的 union，對齊到 8 位元組。
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int PdhAddEnglishCounterW(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern int PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out PDH_FMT_COUNTERVALUE value);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern int PdhCloseQuery(IntPtr query);
}
