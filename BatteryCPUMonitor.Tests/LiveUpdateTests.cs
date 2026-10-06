using System.Net;
using BatteryCPUMonitor.Updates;
using Xunit;
using Xunit.Abstractions;

namespace BatteryCPUMonitor.Tests;

/// <summary>
/// 對真正的 GitHub 做一次完整的查詢與下載，確認我們對 GitHub 回傳格式的假設沒有錯：
/// 版本標籤與檔名的格式、SHA-256 欄位、最新版本頁面的轉址。
/// 只在 GitHub Actions 上執行（需要網路）；GitHub 暫時限制查詢次數時只記錄、不算失敗。
/// </summary>
public class LiveUpdateTests(ITestOutputHelper output)
{
    private static bool NotOnCi => Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true";

    private static HttpClient CreateClient()
    {
        HttpClient client = UpdateHttp.Client;
        return client;
    }

    private static async Task<HttpResponseMessage> GetLatestApiAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseSource.ApiLatest);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        // CI 上用內建的 token 提高查詢次數上限，免得與同一台主機上的其他工作搶額度。
        string? token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return await CreateClient().SendAsync(request);
    }

    [Fact]
    public async Task GitHub最新版本_格式符合我們的解析假設()
    {
        if (NotOnCi) { return; }

        using HttpResponseMessage response = await GetLatestApiAsync();
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            output.WriteLine($"GitHub 暫時限制查詢次數（HTTP {(int)response.StatusCode}），略過。");
            return;
        }

        Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}");
        string json = await response.Content.ReadAsStringAsync();

        foreach (AppVariant variant in Enum.GetValues<AppVariant>())
        {
            LatestRelease? release = ReleaseParser.Parse(json, variant);

            Assert.NotNull(release);
            Assert.True(release.Version >= new Version(2, 5, 0), $"版本 = {release.Version}");
            Assert.NotNull(release.Asset);
            Assert.True(release.Asset.Size > 100_000, $"大小 = {release.Asset.Size}");
            Assert.NotNull(release.Asset.Sha256);
            Assert.Equal(ReleaseSource.AssetName(release.Tag, variant), release.Asset.Name);
            Assert.Equal(ReleaseSource.AssetUrl(release.Tag, variant), release.Asset.Url);
            output.WriteLine($"{variant}：{release.Tag} {release.Asset.Name} {release.Asset.Size:N0} 位元組 sha256={release.Asset.Sha256![..12]}…");
        }
    }

    [Fact]
    public async Task 真的從GitHub下載精簡版_內容與公布的SHA256相符()
    {
        if (NotOnCi) { return; }

        using HttpResponseMessage response = await GetLatestApiAsync();
        if (!response.IsSuccessStatusCode)
        {
            output.WriteLine($"查詢最新版本沒成功（HTTP {(int)response.StatusCode}），略過。");
            return;
        }

        LatestRelease? release = ReleaseParser.Parse(await response.Content.ReadAsStringAsync(), AppVariant.Lite);
        Assert.NotNull(release?.Asset);

        using var folder = new TempFolder();
        string destination = folder.File("downloaded.exe");
        var reports = new List<double>();

        await UpdateInstaller.DownloadAsync(CreateClient(), release.Asset, destination, new Progress<double>(reports.Add), CancellationToken.None);

        Assert.Equal(release.Asset.Size, new FileInfo(destination).Length);
        output.WriteLine($"下載 {release.Asset.Name} 成功，{new FileInfo(destination).Length:N0} 位元組，雜湊值已驗證。");
    }

    [Fact]
    public async Task 試跑真正的程式_版本相符才算通過_改名成update_exe也跑得起來()
    {
        if (NotOnCi) { return; }

        // 建置時主程式的 exe 與它的執行設定會一起放進測試的輸出資料夾。
        string[] files = ["BatteryCPUMonitor.exe", "BatteryCPUMonitor.dll", "BatteryCPUMonitor.runtimeconfig.json", "BatteryCPUMonitor.deps.json"];
        string missing = string.Join("、", files.Where(f => !File.Exists(Path.Combine(AppContext.BaseDirectory, f))));
        if (missing.Length > 0)
        {
            output.WriteLine($"輸出資料夾裡缺少 {missing}，略過。");
            return;
        }

        using var folder = new TempFolder();
        foreach (string file in files)
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, file), folder.File(file));
        }

        // 實際更新時，被試跑的是下載下來的 BatteryCPUMonitor.exe.update.exe，所以就用這個名字試。
        string renamed = UpdateInstaller.DownloadPathFor(folder.File("BatteryCPUMonitor.exe"));
        File.Copy(folder.File("BatteryCPUMonitor.exe"), renamed);

        Assert.True(await SelfCheckRunner.RunAsync(folder.File("BatteryCPUMonitor.exe"), AppVersion.Current, CancellationToken.None));
        Assert.True(await SelfCheckRunner.RunAsync(renamed, AppVersion.Current, CancellationToken.None), "改名後的執行檔試跑失敗");
        Assert.False(await SelfCheckRunner.RunAsync(renamed, new Version(9, 9, 9), CancellationToken.None), "版本不符卻通過了");
        Assert.False(await SelfCheckRunner.RunAsync(folder.File("no-such.exe"), AppVersion.Current, CancellationToken.None), "不存在的檔案卻通過了");
        output.WriteLine($"試跑成功，版本 {AppVersion.CurrentText}。");
    }

    [Fact]
    public async Task 最新版本頁面會轉址到版本標籤_這是API被限流時的備用路線()
    {
        if (NotOnCi) { return; }

        using HttpResponseMessage response = await UpdateHttp.NoRedirectClient.GetAsync(
            ReleaseSource.PageLatest, HttpCompletionOption.ResponseHeadersRead);

        Assert.True((int)response.StatusCode is >= 300 and < 400, $"HTTP {(int)response.StatusCode}");
        Uri? location = response.Headers.Location;
        Assert.NotNull(location);
        string tag = location.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
        output.WriteLine($"轉址到 {location}");

        Assert.Contains("/releases/tag/", location.AbsolutePath);
        Assert.True(ReleaseSource.IsValidTag(tag), $"標籤 = {tag}");
        Assert.True(ReleaseSource.IsTrustedDownload(ReleaseSource.AssetUrl(tag, AppVariant.Lite)));
    }
}
