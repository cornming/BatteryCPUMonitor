using System.Reflection;

namespace BatteryCPUMonitor.Updates;

/// <summary>發布檔案有兩種：需要另外安裝 .NET 的精簡版，與把執行階段一起包進去的免安裝完整版。</summary>
public enum AppVariant
{
    /// <summary>精簡版（檔案名稱沒有 -standalone）。</summary>
    Lite,

    /// <summary>免安裝完整版（檔案名稱結尾為 -standalone.exe）。</summary>
    Standalone,
}

internal static class AppVariantInfo
{
    public const string MetadataKey = "Variant";
    public const string StandaloneValue = "standalone";

    /// <summary>目前執行的是哪一種。建置完整版時，專案檔會把「Variant = standalone」寫進執行檔。</summary>
    public static AppVariant Current { get; } = typeof(AppVariantInfo).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(a => a.Key == MetadataKey && a.Value == StandaloneValue)
            ? AppVariant.Standalone
            : AppVariant.Lite;

    public static string Name(AppVariant variant) => variant == AppVariant.Standalone ? StandaloneValue : "lite";
}

internal static class AppVersion
{
    /// <summary>
    /// 把「v2.5.0」「2.5.0」「2.5.0-dev」「2.5.0+abc」這類文字轉成版本。
    /// 後綴（-dev、+build）會被忽略；缺的位數補零，所以「2.5」等於「2.5.0」。
    /// </summary>
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        if (trimmed.Length > 0 && trimmed[0] is 'v' or 'V')
        {
            trimmed = trimmed[1..];
        }

        int cut = trimmed.IndexOfAny(['-', '+']);
        if (cut >= 0)
        {
            trimmed = trimmed[..cut];
        }

        if (!Version.TryParse(trimmed, out Version? parsed))
        {
            return false;
        }

        version = Normalize(parsed);
        return true;
    }

    /// <summary>
    /// 統一成四位。.NET 的 <see cref="Version"/> 把沒寫的位數記成 -1，所以 2.6.0 與 2.6.0.0 並不相等，比較之前要先統一。
    /// </summary>
    public static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));

    /// <summary>目前這支程式的版本文字，例如「2.5.0」；從原始碼建置的開發版是「0.0.0-dev」。</summary>
    public static string CurrentText { get; } = ReadCurrentText();

    public static Version Current { get; } = TryParse(CurrentText, out Version v) ? v : new Version(0, 0, 0, 0);

    /// <summary>顯示用的版本，例如「2.5.0」。</summary>
    public static string Display(Version version) => $"{version.Major}.{version.Minor}.{version.Build}";

    private static string ReadCurrentText()
    {
        string? text = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        int plus = text?.IndexOf('+') ?? -1;
        if (plus >= 0)
        {
            text = text![..plus];
        }

        return string.IsNullOrWhiteSpace(text) ? "0.0.0" : text;
    }
}

/// <summary>決定要不要更新、能不能更新的規則。純計算。</summary>
internal static class UpdatePolicy
{
    public static bool IsNewer(Version latest, Version current) => AppVersion.Normalize(latest) > AppVersion.Normalize(current);

    /// <summary>從原始碼建置、沒有正式版號（0.0.0）的開發版，不做更新。</summary>
    public static bool IsDevelopment(Version current)
    {
        Version version = AppVersion.Normalize(current);
        return version.Major == 0 && version.Minor == 0 && version.Build == 0;
    }

    /// <summary>
    /// 目前這個行程是不是「下載來的那個 exe」。
    /// 用 dotnet 執行、或在測試程式裡執行時，行程本身是別人的執行檔，絕對不能拿來替換。
    /// </summary>
    public static bool CanSelfUpdate(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath)
            || !processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 路徑可能用反斜線或斜線，檔名取最後一段（不靠作業系統的路徑規則，在哪裡都得到一樣的結果）。
        string fileName = processPath[(processPath.LastIndexOfAny(['\\', '/']) + 1)..];
        return fileName.StartsWith("BatteryCPUMonitor", StringComparison.OrdinalIgnoreCase);
    }
}
