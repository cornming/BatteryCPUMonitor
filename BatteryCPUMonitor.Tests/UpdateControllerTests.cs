using System.Net;
using BatteryCPUMonitor.Updates;
using Xunit;
using static BatteryCPUMonitor.Tests.UpdateTestData;

namespace BatteryCPUMonitor.Tests;

public class UpdateControllerTests
{
    private const string ApiUrl = "https://api.github.com/repos/cornming/BatteryCPUMonitor/releases/latest";
    private const string PageUrl = "https://github.com/cornming/BatteryCPUMonitor/releases/latest";

    private static readonly Version Current = new(2, 5, 0);

    /// <summary>一組完整的測試環境：假網路、一個假的「目前程式」、可以調整的試跑與重新啟動結果。</summary>
    private sealed class Rig : IDisposable
    {
        private readonly TempFolder _folder = new();
        private readonly byte[] _newContent = FakeExecutable(2);

        public Rig(
            string? apiJson = null,
            Func<HttpRequestMessage, HttpResponseMessage>? extra = null,
            Func<HttpRequestMessage, HttpResponseMessage>? noRedirect = null)
        {
            CurrentExe = _folder.File("BatteryCPUMonitor.exe");
            File.WriteAllBytes(CurrentExe, OldContent);

            string json = apiJson ?? ReleaseJson(liteDigest: "sha256:" + Sha256(_newContent), liteSize: _newContent.Length);
            Http = new FakeHandler(request =>
            {
                string url = request.RequestUri!.AbsoluteUri;
                if (extra is not null)
                {
                    return extra(request);
                }

                return url switch
                {
                    ApiUrl => Json(json),
                    _ when url == DownloadUrl(LiteName()) => Bytes(_newContent),
                    _ => Status(HttpStatusCode.NotFound),
                };
            });
            NoRedirect = new FakeHandler(noRedirect ?? (_ => Status(HttpStatusCode.NotFound)));
        }

        public static byte[] OldContent { get; } = FakeExecutable(1);

        public string CurrentExe { get; set; }

        public string Folder => _folder.Path;

        public FakeHandler Http { get; }

        public FakeHandler NoRedirect { get; }

        public bool SelfCheckResult { get; set; } = true;

        public bool RelaunchResult { get; set; } = true;

        public List<string> SelfChecked { get; } = [];

        public List<string> Relaunched { get; } = [];

        public byte[] NewContent => _newContent;

        public UpdateController Controller(Version? current = null, AppVariant variant = AppVariant.Lite) => new(
            new HttpClient(Http),
            new HttpClient(NoRedirect),
            CurrentExe,
            current ?? Current,
            variant,
            (file, version, _) =>
            {
                SelfChecked.Add($"{Path.GetFileName(file)}|{AppVersion.Display(version)}");
                return Task.FromResult(SelfCheckResult);
            },
            exe =>
            {
                Relaunched.Add(exe);
                return RelaunchResult;
            });

        public byte[] ReadCurrent() => File.ReadAllBytes(CurrentExe);

        public string OldFile => UpdateInstaller.OldPathFor(CurrentExe);

        public string TempDownload => UpdateInstaller.DownloadPathFor(CurrentExe);

        public void Dispose() => _folder.Dispose();
    }

    // ---- 檢查 ----

    [Fact]
    public async Task 有新版本_回報可以更新()
    {
        using var rig = new Rig();

        UpdateCheck check = await rig.Controller().CheckAsync(CancellationToken.None);

        Assert.Equal(CheckStatus.Available, check.Status);
        Assert.Equal("2.6.0", AppVersion.Display(check.Release!.Version));
        Assert.Equal(LiteName(), check.Release.Asset!.Name);
        Assert.Equal(new[] { ApiUrl }, rig.Http.Requests);
    }

    [Fact]
    public async Task 完整版會找完整版的下載檔()
    {
        using var rig = new Rig();

        UpdateCheck check = await rig.Controller(variant: AppVariant.Standalone).CheckAsync(CancellationToken.None);

        Assert.Equal(StandaloneName(), check.Release!.Asset!.Name);
    }

    [Fact]
    public async Task 已經是最新版本_不會下載()
    {
        using var rig = new Rig();

        UpdateCheck check = await rig.Controller(current: new Version(2, 6, 0)).CheckAsync(CancellationToken.None);

        Assert.Equal(CheckStatus.UpToDate, check.Status);
        Assert.Equal(new[] { ApiUrl }, rig.Http.Requests);
    }

    [Fact]
    public async Task 目前版本比GitHub上的還新_視為最新()
    {
        using var rig = new Rig();

        UpdateCheck check = await rig.Controller(current: new Version(3, 0, 0)).CheckAsync(CancellationToken.None);

        Assert.Equal(CheckStatus.UpToDate, check.Status);
    }

    [Fact]
    public async Task 開發版不檢查更新_也不連網()
    {
        using var rig = new Rig();

        UpdateCheck check = await rig.Controller(current: new Version(0, 0, 0)).CheckAsync(CancellationToken.None);

        Assert.Equal(CheckStatus.NotSupported, check.Status);
        Assert.Empty(rig.Http.Requests);
    }

    [Fact]
    public async Task 行程不是下載來的exe_不支援更新也不連網()
    {
        using var rig = new Rig { CurrentExe = @"C:\Program Files\dotnet\dotnet.exe" };

        UpdateCheck check = await rig.Controller().CheckAsync(CancellationToken.None);

        Assert.Equal(CheckStatus.NotSupported, check.Status);
        Assert.Empty(rig.Http.Requests);
    }

    [Fact]
    public async Task 新版本沒有適用的下載檔_回報失敗()
    {
        using var rig = new Rig(apiJson: """{ "tag_name": "v2.6.0", "assets": [] }""");

        UpdateCheck check = await rig.Controller().CheckAsync(CancellationToken.None);

        Assert.Equal(CheckStatus.Failed, check.Status);
        Assert.Contains("2.6.0", check.Message);
    }

    [Fact]
    public async Task 伺服器錯誤_回報失敗並附上狀態碼()
    {
        using var rig = new Rig(extra: _ => Status(HttpStatusCode.InternalServerError));

        UpdateCheck check = await rig.Controller().CheckAsync(CancellationToken.None);

        Assert.Equal(CheckStatus.Failed, check.Status);
        Assert.Contains("500", check.Message);
    }

    [Fact]
    public async Task 網路連不上_回報失敗而不是丟出例外()
    {
        using var rig = new Rig(extra: _ => throw new HttpRequestException("沒有網路"));

        UpdateCheck check = await rig.Controller().CheckAsync(CancellationToken.None);

        Assert.Equal(CheckStatus.Failed, check.Status);
        Assert.Contains("沒有網路", check.Message);
    }

    [Fact]
    public async Task 回傳的內容看不懂_回報失敗()
    {
        using var rig = new Rig(apiJson: "<html>not json</html>");

        UpdateCheck check = await rig.Controller().CheckAsync(CancellationToken.None);

        Assert.Equal(CheckStatus.Failed, check.Status);
    }

    // ---- API 被限流時改讀轉址 ----

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task API被限流_改讀最新版本頁面的轉址_仍然找得到新版本(HttpStatusCode limited)
    {
        var redirect = new HttpResponseMessage(HttpStatusCode.Found);
        redirect.Headers.Location = new Uri("https://github.com/cornming/BatteryCPUMonitor/releases/tag/v2.6.0");
        using var rig = new Rig(extra: _ => Status(limited), noRedirect: _ => redirect);

        UpdateCheck check = await rig.Controller().CheckAsync(CancellationToken.None);

        Assert.Equal(CheckStatus.Available, check.Status);
        Assert.Equal("2.6.0", AppVersion.Display(check.Release!.Version));
        Assert.Equal(DownloadUrl(LiteName()), check.Release.Asset!.Url.AbsoluteUri);
        Assert.Null(check.Release.Asset.Sha256); // 這條路沒有雜湊值可驗證
        Assert.Equal(new[] { PageUrl }, rig.NoRedirect.Requests);
    }

    [Fact]
    public async Task 轉址的標籤不是版本號_不採用()
    {
        var redirect = new HttpResponseMessage(HttpStatusCode.Found);
        redirect.Headers.Location = new Uri("https://github.com/cornming/BatteryCPUMonitor/releases/tag/evil");
        using var rig = new Rig(extra: _ => Status(HttpStatusCode.Forbidden), noRedirect: _ => redirect);

        UpdateCheck check = await rig.Controller().CheckAsync(CancellationToken.None);

        Assert.Equal(CheckStatus.Failed, check.Status);
    }

    [Fact]
    public async Task 限流且頁面也沒有轉址_回報失敗()
    {
        using var rig = new Rig(extra: _ => Status(HttpStatusCode.Forbidden), noRedirect: _ => Status(HttpStatusCode.OK));

        UpdateCheck check = await rig.Controller().CheckAsync(CancellationToken.None);

        Assert.Equal(CheckStatus.Failed, check.Status);
        Assert.Contains("限制", check.Message);
    }

    // ---- 安裝 ----

    private static async Task<LatestRelease> AvailableRelease(Rig rig)
    {
        UpdateCheck check = await rig.Controller().CheckAsync(CancellationToken.None);
        return check.Release!;
    }

    [Fact]
    public async Task 安裝成功_換成新版本並啟動它()
    {
        using var rig = new Rig();
        LatestRelease release = await AvailableRelease(rig);

        InstallResult result = await rig.Controller().InstallAsync(release, null, CancellationToken.None);

        Assert.Equal(InstallStatus.Installed, result.Status);
        Assert.True(result.Restarted);
        Assert.Equal(rig.NewContent, rig.ReadCurrent());
        Assert.Equal(Rig.OldContent, File.ReadAllBytes(rig.OldFile));
        Assert.False(File.Exists(rig.TempDownload));
        Assert.Equal(new[] { rig.CurrentExe }, rig.Relaunched);
    }

    [Fact]
    public async Task 安裝前會先試跑新版本_並指定預期的版本號()
    {
        using var rig = new Rig();
        LatestRelease release = await AvailableRelease(rig);

        await rig.Controller().InstallAsync(release, null, CancellationToken.None);

        Assert.Equal(new[] { "BatteryCPUMonitor.exe.update.exe|2.6.0" }, rig.SelfChecked);
    }

    [Fact]
    public async Task 新版本無法在這台電腦上執行_取消更新且舊版本不受影響()
    {
        using var rig = new Rig { SelfCheckResult = false };
        LatestRelease release = await AvailableRelease(rig);

        InstallResult result = await rig.Controller().InstallAsync(release, null, CancellationToken.None);

        Assert.Equal(InstallStatus.Failed, result.Status);
        Assert.Contains("無法在這台電腦上執行", result.Message);
        Assert.Equal(Rig.OldContent, rig.ReadCurrent());
        Assert.False(File.Exists(rig.OldFile));
        Assert.False(File.Exists(rig.TempDownload));
        Assert.Empty(rig.Relaunched);
    }

    [Fact]
    public async Task 下載內容被竄改_雜湊值不符_舊版本不受影響()
    {
        using var rig = new Rig(extra: request =>
            request.RequestUri!.AbsoluteUri == ApiUrl
                ? Json(ReleaseJson(liteDigest: "sha256:" + Sha256(FakeExecutable(2)), liteSize: 150_000))
                : Bytes(FakeExecutable(3)));
        LatestRelease release = await AvailableRelease(rig);

        InstallResult result = await rig.Controller().InstallAsync(release, null, CancellationToken.None);

        Assert.Equal(InstallStatus.Failed, result.Status);
        Assert.Contains("SHA-256", result.Message);
        Assert.Equal(Rig.OldContent, rig.ReadCurrent());
        Assert.Empty(rig.SelfChecked); // 驗證沒過就不會去執行它
        Assert.Empty(rig.Relaunched);
    }

    [Fact]
    public async Task 下載時網路中斷_舊版本不受影響()
    {
        using var rig = new Rig(extra: request =>
            request.RequestUri!.AbsoluteUri == ApiUrl
                ? Json(ReleaseJson())
                : throw new HttpRequestException("連線被重設"));
        LatestRelease release = await AvailableRelease(rig);

        InstallResult result = await rig.Controller().InstallAsync(release, null, CancellationToken.None);

        Assert.Equal(InstallStatus.Failed, result.Status);
        Assert.Contains("連線被重設", result.Message);
        Assert.Equal(Rig.OldContent, rig.ReadCurrent());
    }

    [Fact]
    public async Task 新版本已經換好但啟動失敗_仍算安裝成功_只是沒有重新啟動()
    {
        using var rig = new Rig { RelaunchResult = false };
        LatestRelease release = await AvailableRelease(rig);

        InstallResult result = await rig.Controller().InstallAsync(release, null, CancellationToken.None);

        Assert.Equal(InstallStatus.Installed, result.Status);
        Assert.False(result.Restarted);
        Assert.Equal(rig.NewContent, rig.ReadCurrent());
    }

    [Fact]
    public async Task 程式所在資料夾不能寫入_不下載直接回報()
    {
        using var rig = new Rig();
        LatestRelease release = await AvailableRelease(rig);
        rig.CurrentExe = Path.Combine(rig.Folder, "no-such-folder", "BatteryCPUMonitor.exe");
        rig.Http.Requests.Clear();

        InstallResult result = await rig.Controller().InstallAsync(release, null, CancellationToken.None);

        Assert.Equal(InstallStatus.Failed, result.Status);
        Assert.Contains("無法寫入", result.Message);
        Assert.Empty(rig.Http.Requests);
    }

    [Fact]
    public async Task 沒有下載檔的版本_無法安裝()
    {
        using var rig = new Rig();
        var release = new LatestRelease("v2.6.0", new Version(2, 6, 0), "", Asset: null);

        InstallResult result = await rig.Controller().InstallAsync(release, null, CancellationToken.None);

        Assert.Equal(InstallStatus.Failed, result.Status);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task 經由轉址查到的版本_沒有雜湊值也能安裝()
    {
        var redirect = new HttpResponseMessage(HttpStatusCode.Found);
        redirect.Headers.Location = new Uri("https://github.com/cornming/BatteryCPUMonitor/releases/tag/v2.6.0");
        byte[] content = FakeExecutable(2);
        using var rig = new Rig(
            extra: request => request.RequestUri!.AbsoluteUri == DownloadUrl(LiteName()) ? Bytes(content) : Status(HttpStatusCode.Forbidden),
            noRedirect: _ => redirect);

        UpdateCheck check = await rig.Controller().CheckAsync(CancellationToken.None);
        InstallResult result = await rig.Controller().InstallAsync(check.Release!, null, CancellationToken.None);

        Assert.Equal(InstallStatus.Installed, result.Status);
        Assert.Equal(content, rig.ReadCurrent());
    }
}
