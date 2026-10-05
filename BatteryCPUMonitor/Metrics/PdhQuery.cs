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
    private const int PdhMoreData = unchecked((int)0x800007D2);

    // PDH_FMT_COUNTERVALUE_ITEM_W 的配置（32 與 64 位元相同）：
    // 位移 0 為名稱指標，位移 8 為狀態碼，位移 16 為 double 數值，每筆 24 位元組。
    private const int ItemSize = 24;
    private const int ItemStatusOffset = 8;
    private const int ItemValueOffset = 16;

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

    /// <summary>
    /// 讀取含萬用字元（例如 <c>\GPU Engine(*)\...</c>）的計數器，回傳每個執行個體的名稱與數值。
    /// 還沒有有效資料的執行個體會被略過；整個計數器讀不到時回傳 null。
    /// </summary>
    public IReadOnlyList<(string Instance, double Value)>? ReadArray(IntPtr? counter)
    {
        if (_query == IntPtr.Zero || counter is not IntPtr handle)
        {
            return null;
        }

        // 執行個體的數量隨時會變（例如程式開開關關），所需的緩衝區大小也跟著變，最多重試幾次。
        for (int attempt = 0; attempt < 3; attempt++)
        {
            uint size = 0;
            int status = PdhGetFormattedCounterArrayW(handle, PdhFmtDouble, ref size, out _, IntPtr.Zero);
            if (status != PdhMoreData || size == 0)
            {
                return status == ErrorSuccess ? [] : null;
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                status = PdhGetFormattedCounterArrayW(handle, PdhFmtDouble, ref size, out uint count, buffer);
                if (status == PdhMoreData)
                {
                    continue;
                }

                if (status != ErrorSuccess)
                {
                    return null;
                }

                var items = new List<(string Instance, double Value)>((int)count);
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = buffer + i * ItemSize;
                    uint itemStatus = (uint)Marshal.ReadInt32(item, ItemStatusOffset);
                    if (itemStatus != PdhCStatusValidData && itemStatus != PdhCStatusNewData)
                    {
                        continue;
                    }

                    double value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, ItemValueOffset));
                    string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item));
                    if (name is not null && double.IsFinite(value))
                    {
                        items.Add((name, value));
                    }
                }

                return items;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return null;
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
    private static extern int PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern int PdhCloseQuery(IntPtr query);
}
