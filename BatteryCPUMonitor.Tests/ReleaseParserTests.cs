using BatteryCPUMonitor.Updates;
using Xunit;
using static BatteryCPUMonitor.Tests.UpdateTestData;

namespace BatteryCPUMonitor.Tests;

public class ReleaseParserTests
{
    private static readonly string LiteHash = new('a', 64);
    private static readonly string StandaloneHash = new('b', 64);

    [Fact]
    public void 精簡版選到沒有standalone的檔案()
    {
        LatestRelease? release = ReleaseParser.Parse(ReleaseJson(liteDigest: "sha256:" + LiteHash), AppVariant.Lite);

        Assert.NotNull(release);
        Assert.Equal(Tag, release.Tag);
        Assert.Equal(new Version(2, 6, 0, 0), release.Version);
        Assert.NotNull(release.Asset);
        Assert.Equal(LiteName(), release.Asset.Name);
        Assert.Equal(DownloadUrl(LiteName()), release.Asset.Url.AbsoluteUri);
        Assert.Equal(150_000L, release.Asset.Size);
        Assert.Equal(LiteHash, release.Asset.Sha256);
    }

    [Fact]
    public void 完整版選到standalone的檔案()
    {
        LatestRelease? release = ReleaseParser.Parse(ReleaseJson(standaloneDigest: "sha256:" + StandaloneHash), AppVariant.Standalone);

        Assert.NotNull(release?.Asset);
        Assert.Equal(StandaloneName(), release.Asset.Name);
        Assert.Equal(50_000_000L, release.Asset.Size);
        Assert.Equal(StandaloneHash, release.Asset.Sha256);
    }

    [Fact]
    public void 沒有附雜湊值時_雜湊值為空()
    {
        LatestRelease? release = ReleaseParser.Parse(ReleaseJson(), AppVariant.Lite);

        Assert.Null(release?.Asset?.Sha256);
        Assert.NotNull(release?.Asset);
    }

    [Theory]
    [InlineData("md5:abcdef")]
    [InlineData("sha256:太短")]
    [InlineData("sha256:" + "z123456789012345678901234567890123456789012345678901234567890123")]
    [InlineData("")]
    public void 雜湊值格式不對就當作沒有(string digest)
    {
        Assert.Null(ReleaseParser.ParseDigest(digest));
    }

    [Fact]
    public void 雜湊值統一轉成小寫()
    {
        Assert.Equal(new string('a', 64), ReleaseParser.ParseDigest("SHA256:" + new string('A', 64)));
    }

    [Fact]
    public void 草稿與預先發行版不採用()
    {
        Assert.Null(ReleaseParser.Parse(ReleaseJson(draft: true), AppVariant.Lite));
        Assert.Null(ReleaseParser.Parse(ReleaseJson(prerelease: true), AppVariant.Lite));
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("v2.6")]
    [InlineData("2.6.0")]
    [InlineData("v2.6.0/../x")]
    [InlineData("v2.6.0-beta")]
    public void 標籤不是vX_Y_Z的格式就不採用(string tag)
    {
        Assert.Null(ReleaseParser.Parse(ReleaseJson(tag: tag), AppVariant.Lite));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{ \"tag_name\": 5 }")]
    public void 看不懂的內容回傳空值而不是丟出例外(string json)
    {
        Assert.Null(ReleaseParser.Parse(json, AppVariant.Lite));
    }

    [Fact]
    public void 沒有任何下載檔時_版本仍然回傳但沒有下載檔()
    {
        LatestRelease? release = ReleaseParser.Parse("""{ "tag_name": "v2.6.0", "assets": [] }""", AppVariant.Lite);

        Assert.NotNull(release);
        Assert.Null(release.Asset);
    }

    [Fact]
    public void 下載網址在別的網站就不採用()
    {
        string json = ReleaseJson().Replace("https://github.com/cornming/", "https://evil.example.com/cornming/");

        Assert.Null(ReleaseParser.Parse(json, AppVariant.Lite)?.Asset);
    }

    [Fact]
    public void 下載網址用點點繞到別的路徑就不採用()
    {
        string json = ReleaseJson().Replace(
            "/releases/download/v2.6.0/BatteryCPUMonitor-v2.6.0.exe",
            "/releases/download/../../../evil/BatteryCPUMonitor-v2.6.0.exe");

        Assert.Null(ReleaseParser.Parse(json, AppVariant.Lite)?.Asset);
    }

    [Fact]
    public void 下載網址不是https就不採用()
    {
        string json = ReleaseJson().Replace("https://github.com/", "http://github.com/");

        Assert.Null(ReleaseParser.Parse(json, AppVariant.Lite)?.Asset);
    }

    [Theory]
    [InlineData("BatteryCPUMonitor-v2.6.0.exe", AppVariant.Lite, true)]
    [InlineData("BatteryCPUMonitor-v2.6.0-standalone.exe", AppVariant.Lite, false)]
    [InlineData("BatteryCPUMonitor-v2.6.0-standalone.exe", AppVariant.Standalone, true)]
    [InlineData("BatteryCPUMonitor-v2.6.0.exe", AppVariant.Standalone, false)]
    [InlineData("source.zip", AppVariant.Lite, false)]
    [InlineData("source.zip", AppVariant.Standalone, false)]
    public void 依種類挑選下載檔(string name, AppVariant variant, bool expected)
    {
        Assert.Equal(expected, ReleaseParser.IsAssetFor(name, variant));
    }

    [Fact]
    public void 依命名規則組出下載網址()
    {
        Assert.Equal(DownloadUrl(LiteName()), ReleaseSource.AssetUrl(Tag, AppVariant.Lite).AbsoluteUri);
        Assert.Equal(DownloadUrl(StandaloneName()), ReleaseSource.AssetUrl(Tag, AppVariant.Standalone).AbsoluteUri);
        Assert.True(ReleaseSource.IsTrustedDownload(ReleaseSource.AssetUrl(Tag, AppVariant.Lite)));
    }

    [Theory]
    [InlineData("v2.6.0", true)]
    [InlineData("v10.0.123", true)]
    [InlineData("v2.6", false)]
    [InlineData("2.6.0", false)]
    [InlineData("v2.6.0/x", false)]
    [InlineData("v2.6.0\n", false)]
    [InlineData("", false)]
    public void 版本標籤格式檢查(string tag, bool expected)
    {
        Assert.Equal(expected, ReleaseSource.IsValidTag(tag));
    }

    // ---- 更新內容 ----

    private const string RealBody =
        "## 下載哪一個？\n\n| 檔案 | 說明 |\n| --- | --- |\n| `a.exe` | 免安裝 |\n\n兩個檔案功能相同。\n\n" +
        "## 更新內容\n\n### 新功能\n\n- 標題一 (abc1234)\n  - 細節甲\n  - 細節乙\n\n### 其他調整\n\n- 補上測試 (def5678)\n\n" +
        "本版通過 213 項自動測試。\n\n完整差異：https://github.com/cornming/BatteryCPUMonitor/compare/v2.5.0...v2.6.0\n";

    [Fact]
    public void 更新內容只取更新內容那一段_並整理成好讀的格式()
    {
        string notes = ReleaseNotes.Extract(RealBody);

        Assert.Equal(
            "【新功能】\n\n• 標題一 (abc1234)\n    ◦ 細節甲\n    ◦ 細節乙\n\n【其他調整】\n\n• 補上測試 (def5678)",
            notes);
    }

    [Fact]
    public void 更新內容不含下載說明與測試數量與差異連結()
    {
        string notes = ReleaseNotes.Extract(RealBody);

        Assert.DoesNotContain("下載哪一個", notes);
        Assert.DoesNotContain("自動測試", notes);
        Assert.DoesNotContain("完整差異", notes);
    }

    [Fact]
    public void 沒有更新內容這一段_回傳空字串()
    {
        Assert.Equal(string.Empty, ReleaseNotes.Extract("隨便寫寫"));
        Assert.Equal(string.Empty, ReleaseNotes.Extract(null));
        Assert.Equal(string.Empty, ReleaseNotes.Extract(""));
    }

    [Fact]
    public void 太長的更新內容會在整行處截斷並加上刪節號()
    {
        string body = "## 更新內容\n\n" + string.Join("\n", Enumerable.Range(1, 100).Select(i => $"- 第 {i} 項調整的說明文字"));

        string notes = ReleaseNotes.Extract(body, maxLength: 200);

        Assert.True(notes.Length <= 205, $"長度 = {notes.Length}");
        Assert.EndsWith("\n…", notes);
        Assert.StartsWith("• 第 1 項", notes);
    }

    [Fact]
    public void 換行是Windows格式也能處理()
    {
        Assert.Equal("• 範例", ReleaseNotes.Extract("## 更新內容\r\n\r\n- 範例\r\n"));
    }
}
