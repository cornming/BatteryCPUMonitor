using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace BatteryCPUMonitor.Tests;

/// <summary>測試用的假網路：依網址回傳事先準備好的內容，並記下被要求過哪些網址。</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.AbsoluteUri);
        return Task.FromResult(respond(request));
    }
}

internal static class UpdateTestData
{
    public const string Tag = "v2.6.0";

    public static string LiteName(string tag = Tag) => $"BatteryCPUMonitor-{tag}.exe";

    public static string StandaloneName(string tag = Tag) => $"BatteryCPUMonitor-{tag}-standalone.exe";

    public static string DownloadUrl(string name, string tag = Tag) =>
        $"https://github.com/cornming/BatteryCPUMonitor/releases/download/{tag}/{name}";

    /// <summary>一個看起來像 Windows 執行檔的假檔案：以「MZ」開頭，大小足夠通過基本檢查。</summary>
    public static byte[] FakeExecutable(byte marker, int length = 150_000)
    {
        byte[] bytes = new byte[length];
        Array.Fill(bytes, marker);
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        return bytes;
    }

    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>仿照 GitHub API 回傳的格式。digest 傳 null 代表不附雜湊值。</summary>
    public static string ReleaseJson(
        string tag = Tag,
        string? liteDigest = null,
        string? standaloneDigest = null,
        long liteSize = 150_000,
        long standaloneSize = 50_000_000,
        bool draft = false,
        bool prerelease = false,
        string? body = null)
    {
        static string Digest(string? digest) => digest is null ? string.Empty : $", \"digest\": \"{digest}\"";

        string notes = (body ?? "## 更新內容\n\n### 新功能\n\n- 範例功能 (abc1234)\n\n本版通過 10 項自動測試。\n")
            .Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

        return $$"""
            {
              "tag_name": "{{tag}}",
              "draft": {{draft.ToString().ToLowerInvariant()}},
              "prerelease": {{prerelease.ToString().ToLowerInvariant()}},
              "body": "{{notes}}",
              "assets": [
                { "name": "{{StandaloneName(tag)}}", "size": {{standaloneSize}}, "browser_download_url": "{{DownloadUrl(StandaloneName(tag), tag)}}"{{Digest(standaloneDigest)}} },
                { "name": "{{LiteName(tag)}}", "size": {{liteSize}}, "browser_download_url": "{{DownloadUrl(LiteName(tag), tag)}}"{{Digest(liteDigest)}} }
              ]
            }
            """;
    }

    public static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    public static HttpResponseMessage Status(HttpStatusCode code) => new(code);
}

/// <summary>每個測試各自一個暫存資料夾，測完整個刪掉。</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BatteryCPUMonitor.Tests." + Guid.NewGuid().ToString("N"));

    public TempFolder() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 暫存資料夾刪不掉不影響測試結果。
        }
    }
}
