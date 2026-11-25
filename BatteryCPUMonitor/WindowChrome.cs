using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BatteryCPUMonitor;

/// <summary>
/// Windows DWM 外觀與系統深淺色整合：
/// 在 Windows 11 上套用原生抗鋸齒圓角、邊框色與深色標題列；在舊版 Windows 上安全略過不報錯。
/// </summary>
internal static class WindowChrome
{
    private const int Windows11FirstBuild = 22000;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;

    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= Windows11FirstBuild;

    /// <summary>Windows 應用程式模式是否為淺色（找不到機碼時預設深色）。</summary>
    public static bool IsSystemAppsLight() => ReadDword(PersonalizeKey, "AppsUseLightTheme") == 1;

    /// <summary>Windows 工作列（系統模式）是否為淺色。</summary>
    public static bool IsSystemTaskbarLight() => ReadDword(PersonalizeKey, "SystemUsesLightTheme") == 1;

    /// <summary>在 Windows 11 套用原生抗鋸齒圓角與暖石細邊框，回傳是否由 DWM 接管圓角。</summary>
    public static bool TryApplyFloatingChrome(IntPtr handle, BarVisualStyle style, Color borderColor)
    {
        if (!IsWindows11 || handle == IntPtr.Zero)
        {
            return false;
        }

        int corner = style == BarVisualStyle.HudMatrix ? DWMWCP_ROUNDSMALL : DWMWCP_ROUND;
        uint colorRef = (uint)(borderColor.R | (borderColor.G << 8) | (borderColor.B << 16));

        bool rounded = TrySetDwmInt(handle, DWMWA_WINDOW_CORNER_PREFERENCE, corner);
        TrySetDwmUint(handle, DWMWA_BORDER_COLOR, colorRef);
        return rounded;
    }

    /// <summary>套用 Windows 10 20H1+ / Windows 11 的視窗標題列深淺色。</summary>
    public static void TryApplyTitleBarTheme(IntPtr handle, bool dark)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        TrySetDwmInt(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);
    }

    private static bool TrySetDwmInt(IntPtr handle, int attribute, int value)
    {
        try
        {
            return DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int)) == 0;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    private static bool TrySetDwmUint(IntPtr handle, int attribute, uint value)
    {
        try
        {
            return DwmSetWindowAttribute(handle, attribute, ref value, sizeof(uint)) == 0;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    private static int? ReadDword(string subKey, string name)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKey);
            return key?.GetValue(name) as int?;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref uint pvAttribute, int cbAttribute);
}
