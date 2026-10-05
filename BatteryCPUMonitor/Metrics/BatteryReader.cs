using System.Runtime.InteropServices;

namespace BatteryCPUMonitor.Metrics;

/// <summary>讀取電池狀態。不需要系統管理員權限。</summary>
internal static class BatteryReader
{
    public static BatterySnapshot Read()
    {
        if (!GetSystemPowerStatus(out var status))
        {
            return default;
        }

        return BatterySnapshot.FromRaw(
            status.ACLineStatus,
            status.BatteryFlag,
            status.BatteryLifePercent,
            status.BatteryLifeTime);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}
