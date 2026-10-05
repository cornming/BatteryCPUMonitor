using System.Runtime.InteropServices;

namespace BatteryCPUMonitor.Metrics;

/// <summary>讀取實體記憶體使用率。不需要系統管理員權限。</summary>
internal static class MemoryReader
{
    /// <summary>回傳 0–100；讀取失敗時回傳 null。</summary>
    public static double? ReadUsedPercent()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status)
            ? MemoryMath.UsedPercent(status.ullTotalPhys, status.ullAvailPhys)
            : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}

internal static class MemoryMath
{
    /// <summary>已使用比例 = (總量 − 可用量) ÷ 總量。</summary>
    public static double? UsedPercent(ulong totalBytes, ulong availableBytes)
    {
        if (totalBytes == 0)
        {
            return null;
        }

        if (availableBytes >= totalBytes)
        {
            return 0;
        }

        return (totalBytes - availableBytes) * 100.0 / totalBytes;
    }
}
