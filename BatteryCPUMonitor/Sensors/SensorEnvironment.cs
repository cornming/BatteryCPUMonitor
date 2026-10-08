using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace BatteryCPUMonitor.Sensors;

/// <summary>
/// 主程式（一般權限）自己查得到的、跟感測器有關的系統資訊：PawnIO 有沒有裝、PawnIO 服務的狀態、處理器型號。
/// 這些不需要系統管理員權限，所以感測器服務沒起來時也看得到。
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class SensorEnvironment
{
    private const string PawnIoServiceName = "PawnIO";

    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const int ErrorServiceDoesNotExist = 1060;
    private const uint ServiceStopped = 1;
    private const uint ServiceRunning = 4;

    /// <summary>目前的行程是不是以系統管理員權限執行。</summary>
    public static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>官方文件的做法：登錄檔的安裝位置，或 Program Files\PawnIO，底下有 PawnIOLib.dll。</summary>
    public static bool IsPawnIoInstalled()
    {
        string? location = null;
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            location = key?.GetValue("InstallLocation") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // 讀不到就當作沒有登錄，下面還會檢查預設的安裝位置。
        }

        return PawnIoDetector.IsInstalled(location, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), File.Exists);
    }

    /// <summary>
    /// PawnIO 的核心驅動程式是以 Windows 服務的形式存在，啟動類型是「手動」，沒有程式在用的時候不一定在跑。
    /// 「執行中」代表驅動程式已經載入核心；「已停止」代表目前沒載入；「沒有這個服務」代表沒裝好。
    /// </summary>
    public static DriverServiceState QueryPawnIoService()
    {
        IntPtr manager = OpenSCManagerW(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            return DriverServiceState.Unknown;
        }

        try
        {
            IntPtr service = OpenServiceW(manager, PawnIoServiceName, ServiceQueryStatus);
            if (service == IntPtr.Zero)
            {
                return Marshal.GetLastWin32Error() == ErrorServiceDoesNotExist ? DriverServiceState.NotInstalled : DriverServiceState.Unknown;
            }

            try
            {
                if (!QueryServiceStatus(service, out ServiceStatus status))
                {
                    return DriverServiceState.Unknown;
                }

                return status.CurrentState switch
                {
                    ServiceStopped => DriverServiceState.Stopped,
                    ServiceRunning => DriverServiceState.Running,
                    _ => DriverServiceState.Changing,
                };
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    /// <summary>Windows 登錄檔記載的處理器：名稱、識別字串（含 Family／Model／Stepping）與廠牌。讀不到回傳「未知」。</summary>
    public static string ReadProcessor()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            string?[] parts =
            [
                (key?.GetValue("ProcessorNameString") as string)?.Trim(),
                key?.GetValue("Identifier") as string,
                key?.GetValue("VendorIdentifier") as string,
            ];

            string joined = string.Join("｜", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
            return joined.Length > 0 ? joined : "未知";
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return "未知";
        }
    }

    /// <summary>例如「Microsoft Windows 10.0.26200，X64」。</summary>
    public static string DescribeOperatingSystem() =>
        $"{RuntimeInformation.OSDescription}，{RuntimeInformation.OSArchitecture}";

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr OpenServiceW(IntPtr manager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
