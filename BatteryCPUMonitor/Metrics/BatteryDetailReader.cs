using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BatteryCPUMonitor.Metrics;

/// <summary>
/// 直接向電池驅動程式查詢設計容量、滿充容量與充放電功率。
/// 這是 Windows 公開的電池介面，一般使用者即可呼叫，不需要系統管理員權限。
/// </summary>
internal sealed class BatteryDetailReader : IDisposable
{
    private const uint DigcfPresent = 0x02;
    private const uint DigcfDeviceInterface = 0x10;
    private const int ErrorNoMoreItems = 259;

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareReadWrite = 0x03;
    private const uint OpenExisting = 3;

    private const uint IoctlBatteryQueryTag = 0x00294040;
    private const uint IoctlBatteryQueryInformation = 0x00294044;
    private const uint IoctlBatteryQueryStatus = 0x0029404C;

    /// <summary>容量以相對單位回報（非毫瓦時）；此時功率的單位不明，不採用。</summary>
    private const uint BatteryCapacityRelative = 0x40000000;

    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(15);

    private readonly List<SafeFileHandle> _batteries = [];
    private long _lastOpenAttempt;
    private bool _everAttempted;

    /// <summary>讀取所有電池的彙整資訊；沒有電池或讀取失敗時回傳 null。</summary>
    /// <param name="pluggedIn">是否接著外部電源，用來校正功率的正負號。</param>
    /// <param name="charging">是否正在充電。</param>
    public BatteryDetail? Read(bool pluggedIn, bool charging)
    {
        if (_batteries.Count == 0 && !TryOpenBatteries())
        {
            return null;
        }

        var readings = new List<(uint Designed, uint FullCharged, uint Remaining, int Rate)>(_batteries.Count);
        foreach (SafeFileHandle battery in _batteries)
        {
            if (TryQuery(battery, out var reading))
            {
                readings.Add(reading);
            }
        }

        if (readings.Count == 0)
        {
            // 電池可能被拔掉或裝置重新列舉了；關掉舊的控制代碼，過一陣子再重新尋找。
            CloseBatteries();
            return null;
        }

        return BatteryMath.Combine(readings, pluggedIn, charging);
    }

    public void Dispose() => CloseBatteries();

    /// <summary>
    /// 列出所有電池裝置的路徑。<paramref name="lastError"/> 是列舉結束時的錯誤碼，
    /// 正常結束應為 259（沒有更多項目）。
    /// </summary>
    internal static List<string> EnumerateDevicePaths(out int lastError)
    {
        var paths = new List<string>();
        Guid batteryClass = new("72631E54-78A4-11D0-BCF7-00AA00B7B32A");

        IntPtr devices = SetupDiGetClassDevsW(ref batteryClass, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (devices == new IntPtr(-1))
        {
            lastError = Marshal.GetLastWin32Error();
            return paths;
        }

        try
        {
            for (uint index = 0; index < 16; index++)
            {
                var interfaceData = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(devices, IntPtr.Zero, ref batteryClass, index, ref interfaceData))
                {
                    lastError = Marshal.GetLastWin32Error();
                    return paths;
                }

                // 第一次呼叫只問需要多大的緩衝區。
                SetupDiGetDeviceInterfaceDetailW(devices, ref interfaceData, IntPtr.Zero, 0, out uint required, IntPtr.Zero);
                if (required == 0)
                {
                    continue;
                }

                IntPtr detail = Marshal.AllocHGlobal((int)required);
                try
                {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize：64 位元為 8，32 位元為 6；路徑字串從位移 4 開始。
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetailW(devices, ref interfaceData, detail, required, out _, IntPtr.Zero))
                    {
                        string? path = Marshal.PtrToStringUni(detail + 4);
                        if (!string.IsNullOrEmpty(path))
                        {
                            paths.Add(path);
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }

            lastError = ErrorNoMoreItems;
            return paths;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(devices);
        }
    }

    private bool TryOpenBatteries()
    {
        // 沒有電池的電腦不必每秒都去找一次。
        long now = Environment.TickCount64;
        if (_everAttempted && now - _lastOpenAttempt < RetryInterval.TotalMilliseconds)
        {
            return false;
        }

        _everAttempted = true;
        _lastOpenAttempt = now;

        foreach (string path in EnumerateDevicePaths(out _))
        {
            SafeFileHandle handle = CreateFileW(path, GenericRead | GenericWrite, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                handle.Dispose();
            }
            else
            {
                _batteries.Add(handle);
            }
        }

        return _batteries.Count > 0;
    }

    private void CloseBatteries()
    {
        foreach (SafeFileHandle battery in _batteries)
        {
            battery.Dispose();
        }

        _batteries.Clear();
    }

    private static bool TryQuery(SafeFileHandle battery, out (uint Designed, uint FullCharged, uint Remaining, int Rate) reading)
    {
        reading = default;

        // 每顆電池有一個「標籤」，換電池時會變；之後的查詢都要帶著它。
        uint waitMilliseconds = 0;
        if (!DeviceIoControl(battery, IoctlBatteryQueryTag, ref waitMilliseconds, sizeof(uint), out uint tag, sizeof(uint), out _, IntPtr.Zero)
            || tag == 0)
        {
            return false;
        }

        var infoQuery = new BATTERY_QUERY_INFORMATION { BatteryTag = tag, InformationLevel = 0 /* BatteryInformation */ };
        if (!DeviceIoControl(battery, IoctlBatteryQueryInformation, ref infoQuery, Marshal.SizeOf<BATTERY_QUERY_INFORMATION>(),
                out BATTERY_INFORMATION info, Marshal.SizeOf<BATTERY_INFORMATION>(), out _, IntPtr.Zero))
        {
            return false;
        }

        var statusQuery = new BATTERY_WAIT_STATUS { BatteryTag = tag };
        if (!DeviceIoControl(battery, IoctlBatteryQueryStatus, ref statusQuery, Marshal.SizeOf<BATTERY_WAIT_STATUS>(),
                out BATTERY_STATUS status, Marshal.SizeOf<BATTERY_STATUS>(), out _, IntPtr.Zero))
        {
            return false;
        }

        bool relative = (info.Capabilities & BatteryCapacityRelative) != 0;
        reading = (
            info.DesignedCapacity,
            info.FullChargedCapacity,
            relative ? BatteryMath.UnknownCapacity : status.Capacity,
            relative ? BatteryMath.UnknownRate : status.Rate);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BATTERY_QUERY_INFORMATION
    {
        public uint BatteryTag;
        public int InformationLevel;
        public int AtRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BATTERY_INFORMATION
    {
        public uint Capabilities;
        public byte Technology;
        public byte Reserved1;
        public byte Reserved2;
        public byte Reserved3;
        public uint Chemistry;
        public uint DesignedCapacity;
        public uint FullChargedCapacity;
        public uint DefaultAlert1;
        public uint DefaultAlert2;
        public uint CriticalBias;
        public uint CycleCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BATTERY_WAIT_STATUS
    {
        public uint BatteryTag;
        public uint Timeout;
        public uint PowerState;
        public uint LowCapacity;
        public uint HighCapacity;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BATTERY_STATUS
    {
        public uint PowerState;
        public uint Capacity;
        public uint Voltage;
        public int Rate;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, ref uint inBuffer, int inBufferSize, out uint outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, ref BATTERY_QUERY_INFORMATION inBuffer, int inBufferSize, out BATTERY_INFORMATION outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, ref BATTERY_WAIT_STATUS inBuffer, int inBufferSize, out BATTERY_STATUS outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);
}
