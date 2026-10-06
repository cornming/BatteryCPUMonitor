using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace BatteryCPUMonitor.Sensors;

/// <summary>
/// 以系統管理員權限啟動感測器服務。會跳出 Windows 的 UAC 確認視窗，使用者按「否」就是拒絕。
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class ElevatedHostLauncher : ISensorHostLauncher
{
    private const int ErrorCancelled = 1223; // ERROR_CANCELLED：使用者在 UAC 視窗按了「否」

    public void Launch(string pipeName)
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("找不到程式路徑。");
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("取不到目前的使用者。");

        var startInfo = new ProcessStartInfo(exe, new SensorHostArguments(pipeName, sid, Environment.ProcessId).Build())
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
        };

        try
        {
            Process.Start(startInfo)?.Dispose();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw new SensorLaunchDeclinedException();
        }
    }
}
