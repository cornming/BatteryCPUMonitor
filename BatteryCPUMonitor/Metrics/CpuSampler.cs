using System.Runtime.InteropServices;

namespace BatteryCPUMonitor.Metrics;

/// <summary>
/// CPU 使用率取樣器。每次呼叫 <see cref="Sample"/> 都立即回傳，不會讓畫面卡住；
/// 數值是「上一次呼叫到這一次呼叫」之間的平均使用率。
/// </summary>
/// <remarks>
/// 優先使用效能計數器「Processor Utility」，這個數字比較接近工作管理員顯示的 CPU%。
/// 計數器無法使用時（例如系統的效能計數器損毀），改用 GetSystemTimes 計算。
/// 兩種方式都不需要系統管理員權限。
/// </remarks>
internal sealed class CpuSampler : IDisposable
{
    private readonly PdhCpuCounter? _counter = PdhCpuCounter.TryCreate();
    private readonly SystemTimesCpu _fallback = new();

    /// <summary>回傳 0–100；剛啟動、還沒有兩筆樣本可比較時回傳 null。</summary>
    public double? Sample()
    {
        // 備援也每次取樣，基準點才會持續更新，隨時可以接手。
        double? fallback = _fallback.Sample();
        return _counter?.Sample() ?? fallback;
    }

    public void Dispose() => _counter?.Dispose();
}

/// <summary>透過 PDH 讀取「Processor Utility」效能計數器。</summary>
internal sealed class PdhCpuCounter : IDisposable
{
    // 用英文名稱加入計數器，任何語系的 Windows 都適用。
    private const string CounterPath = @"\Processor Information(_Total)\% Processor Utility";

    private const int ErrorSuccess = 0;
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhCStatusValidData = 0;
    private const uint PdhCStatusNewData = 1;

    private IntPtr _query;
    private readonly IntPtr _counter;

    private PdhCpuCounter(IntPtr query, IntPtr counter)
    {
        _query = query;
        _counter = counter;
    }

    public static PdhCpuCounter? TryCreate()
    {
        try
        {
            if (PdhOpenQueryW(null, IntPtr.Zero, out IntPtr query) != ErrorSuccess)
            {
                return null;
            }

            if (PdhAddEnglishCounterW(query, CounterPath, IntPtr.Zero, out IntPtr counter) != ErrorSuccess)
            {
                PdhCloseQuery(query);
                return null;
            }

            // 使用率要兩筆樣本相減才算得出來，先收第一筆當基準。
            PdhCollectQueryData(query);
            return new PdhCpuCounter(query, counter);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public double? Sample()
    {
        if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != ErrorSuccess)
        {
            return null;
        }

        if (PdhGetFormattedCounterValue(_counter, PdhFmtDouble, IntPtr.Zero, out PDH_FMT_COUNTERVALUE value) != ErrorSuccess)
        {
            return null;
        }

        if (value.CStatus != PdhCStatusValidData && value.CStatus != PdhCStatusNewData)
        {
            return null;
        }

        return double.IsFinite(value.DoubleValue) ? Math.Clamp(value.DoubleValue, 0, 100) : null;
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

/// <summary>以 GetSystemTimes 計算 CPU 使用率的備援做法。</summary>
internal sealed class SystemTimesCpu
{
    private bool _hasPrevious;
    private long _idle;
    private long _kernel;
    private long _user;

    public double? Sample()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user))
        {
            return null;
        }

        double? usage = _hasPrevious
            ? CpuMath.UsageFromDeltas(idle - _idle, kernel - _kernel, user - _user)
            : null;

        (_idle, _kernel, _user, _hasPrevious) = (idle, kernel, user, true);
        return usage;
    }

    // FILETIME 是兩個 32 位元整數，記憶體配置與 long 相同。
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);
}

internal static class CpuMath
{
    /// <summary>
    /// 由兩次取樣之間的時間差算出使用率。注意 Windows 回報的 kernel 時間已經包含 idle 時間。
    /// </summary>
    public static double? UsageFromDeltas(long idleDelta, long kernelDelta, long userDelta)
    {
        long total = kernelDelta + userDelta;
        if (total <= 0)
        {
            return null;
        }

        return Math.Clamp((total - idleDelta) * 100.0 / total, 0, 100);
    }
}
