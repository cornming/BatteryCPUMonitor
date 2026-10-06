using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BatteryCPUMonitor.Updates;

/// <summary>要下載的檔案。</summary>
/// <param name="Size">位元組數；來源沒提供時為 0。</param>
/// <param name="Sha256">GitHub 公布的 SHA-256（小寫十六進位）；來源沒提供時為 null。</param>
internal sealed record ReleaseAsset(string Name, Uri Url, long Size, string? Sha256);

/// <summary>GitHub 上最新的正式版本。</summary>
/// <param name="Asset">適合目前這種版本（精簡或完整）的下載檔；找不到時為 null。</param>
internal sealed record LatestRelease(string Tag, Version Version, string Notes, ReleaseAsset? Asset);

/// <summary>更新來源：這個專案在 GitHub 上的位置。</summary>
internal static class ReleaseSource
{
    public const string DownloadPrefix = "https://github.com/cornming/BatteryCPUMonitor/releases/download/";
    public const string ReleasesPage = "https://github.com/cornming/BatteryCPUMonitor/releases";

    public static readonly Uri ApiLatest = new("https://api.github.com/repos/cornming/BatteryCPUMonitor/releases/latest");
    public static readonly Uri PageLatest = new("https://github.com/cornming/BatteryCPUMonitor/releases/latest");

    /// <summary>版本標籤的格式：v2.5.0。用來擋掉不是版本號的轉址內容，避免被拿去組出奇怪的下載網址。</summary>
    private static readonly Regex TagPattern = new(@"\Av\d+\.\d+\.\d+\z", RegexOptions.CultureInvariant);

    public static bool IsValidTag(string? tag) => tag is not null && TagPattern.IsMatch(tag);

    public static string AssetName(string tag, AppVariant variant) =>
        variant == AppVariant.Standalone ? $"BatteryCPUMonitor-{tag}-standalone.exe" : $"BatteryCPUMonitor-{tag}.exe";

    /// <summary>依命名規則組出下載網址（API 查不到、改用轉址查詢版本時使用）。</summary>
    public static Uri AssetUrl(string tag, AppVariant variant) => new($"{DownloadPrefix}{tag}/{AssetName(tag, variant)}");

    /// <summary>下載網址必須在這個專案的 Release 底下（以正規化後的網址檢查，擋掉「../」之類的繞路）。</summary>
    public static bool IsTrustedDownload(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps
        && url.AbsoluteUri.StartsWith(DownloadPrefix, StringComparison.Ordinal);
}

/// <summary>解析 GitHub「最新 Release」的 JSON。純計算。</summary>
internal static class ReleaseParser
{
    private const string Sha256Prefix = "sha256:";

    /// <summary>草稿、預先發行版、看不懂的內容都回傳 null。</summary>
    public static LatestRelease? Parse(string json, AppVariant variant)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Flag(root, "draft") || Flag(root, "prerelease"))
            {
                return null;
            }

            string? tag = Text(root, "tag_name");
            if (!ReleaseSource.IsValidTag(tag) || !AppVersion.TryParse(tag, out Version version))
            {
                return null;
            }

            ReleaseAsset? asset = null;
            if (root.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in assets.EnumerateArray())
                {
                    asset = ParseAsset(element, variant);
                    if (asset is not null)
                    {
                        break;
                    }
                }
            }

            return new LatestRelease(tag!, version, ReleaseNotes.Extract(Text(root, "body")), asset);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ReleaseAsset? ParseAsset(JsonElement element, AppVariant variant)
    {
        string? name = Text(element, "name");
        string? url = Text(element, "browser_download_url");
        if (name is null || url is null || !IsAssetFor(name, variant))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || !ReleaseSource.IsTrustedDownload(uri))
        {
            return null;
        }

        long size = element.TryGetProperty("size", out JsonElement sizeElement) && sizeElement.TryGetInt64(out long s) ? s : 0;
        return new ReleaseAsset(name, uri, Math.Max(0, size), ParseDigest(Text(element, "digest")));
    }

    /// <summary>精簡版是「…exe」；完整版是「…-standalone.exe」。</summary>
    public static bool IsAssetFor(string name, AppVariant variant)
    {
        bool isExe = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        bool isStandalone = name.EndsWith("-standalone.exe", StringComparison.OrdinalIgnoreCase);
        return variant == AppVariant.Standalone ? isStandalone : isExe && !isStandalone;
    }

    /// <summary>GitHub 的 digest 欄位格式為「sha256:64 位十六進位」；格式不符就當作沒有。</summary>
    public static string? ParseDigest(string? digest)
    {
        if (digest is null || !digest.StartsWith(Sha256Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string hex = digest[Sha256Prefix.Length..].Trim().ToLowerInvariant();
        return hex.Length == 64 && hex.All(Uri.IsHexDigit) ? hex : null;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
}

/// <summary>從 Release 說明文字取出給使用者看的「更新內容」。純計算。</summary>
internal static class ReleaseNotes
{
    private const string Heading = "## 更新內容";

    public static string Extract(string? body, int maxLength = 700)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        string text = body.Replace("\r\n", "\n");
        int start = text.IndexOf(Heading, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        var result = new StringBuilder();
        foreach (string raw in text[(start + Heading.Length)..].Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.StartsWith("本版通過", StringComparison.Ordinal) || line.StartsWith("完整差異", StringComparison.Ordinal))
            {
                break;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                line = $"【{line[4..].Trim()}】";
            }
            else if (line.StartsWith("  - ", StringComparison.Ordinal))
            {
                line = "    ◦ " + line[4..];
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                line = "• " + line[2..];
            }

            result.Append(line).Append('\n');
        }

        string notes = Regex.Replace(result.ToString().Trim(), "\n{3,}", "\n\n");
        if (notes.Length <= maxLength)
        {
            return notes;
        }

        int cut = notes.LastIndexOf('\n', maxLength);
        return notes[..(cut > 0 ? cut : maxLength)].TrimEnd() + "\n…";
    }
}
