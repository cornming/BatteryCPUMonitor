using System.Security;
using Microsoft.Win32;

namespace BatteryCPUMonitor;

/// <summary>開機自動啟動的指令字串處理。純計算。</summary>
internal static class AutoStartCommand
{
    public static string Build(string exePath) => $"\"{exePath}\"";

    /// <summary>登錄檔裡的指令是否指向這個執行檔（忽略引號與大小寫）。</summary>
    public static bool PointsTo(string? registryValue, string exePath) =>
        !string.IsNullOrWhiteSpace(registryValue)
        && !string.IsNullOrWhiteSpace(exePath)
        && string.Equals(registryValue.Trim().Trim('"'), exePath, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 開機自動啟動。寫在「目前使用者」的 Run 機碼，不需要系統管理員權限。
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BatteryCPUMonitor";

    private static string ExePath => Environment.ProcessPath ?? string.Empty;

    /// <summary>已設定開機啟動，而且指向目前這個執行檔。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
            return AutoStartCommand.PointsTo(key?.GetValue(ValueName) as string, ExePath);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return false;
        }
    }

    /// <summary>開啟或關閉開機啟動；成功回傳 true。</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                if (string.IsNullOrEmpty(ExePath))
                {
                    return false;
                }

                key.SetValue(ValueName, AutoStartCommand.Build(ExePath));
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return false;
        }
    }

    private static bool IsExpected(Exception ex) =>
        ex is UnauthorizedAccessException or SecurityException or IOException;
}
