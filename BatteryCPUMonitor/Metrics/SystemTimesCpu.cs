using System.Runtime.InteropServices;

namespace BatteryCPUMonitor.Metrics;

/// <summary>
/// 以 GetSystemTimes 計算 CPU 使用率。效能計數器無法使用時（例如系統的計數器損毀）的備援做法。
/// </summary>
internal sealed class SystemTimesCpu
{
    private bool _hasPrevious;
    private long _idle;
    private long _kernel;
    private long _user;

    /// <summary>回傳上一次呼叫到這一次呼叫之間的平均使用率（0–100）；第一次呼叫回傳 null。</summary>
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
