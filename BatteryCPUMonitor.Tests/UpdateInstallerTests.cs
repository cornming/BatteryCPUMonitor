using System.Diagnostics;
using System.Net;
using BatteryCPUMonitor.Updates;
using Xunit;
using static BatteryCPUMonitor.Tests.UpdateTestData;

namespace BatteryCPUMonitor.Tests;

public class UpdateInstallerTests
{
    private static bool NotWindows => !OperatingSystem.IsWindows();

    private static HttpClient ClientReturning(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new FakeHandler(respond));

    private static ReleaseAsset AssetFor(byte[] content, bool withHash = true, long? size = null) => new(
        LiteName(),
        new Uri(DownloadUrl(LiteName())),
        size ?? content.Length,
        withHash ? Sha256(content) : null);

    // ---- 下載與驗證 ----

    [Fact]
    public async Task 下載成功_檔案內容與來源一致()
    {
        using var folder = new TempFolder();
        byte[] content = FakeExecutable(1);
        using HttpClient http = ClientReturning(_ => Bytes(content));

        await UpdateInstaller.DownloadAsync(http, AssetFor(content), folder.File("new.exe"), progress: null, CancellationToken.None);

        Assert.Equal(content, File.ReadAllBytes(folder.File("new.exe")));
    }

    [Fact]
    public async Task 下載時會回報進度_最後到達百分之百()
    {
        using var folder = new TempFolder();
        byte[] content = FakeExecutable(1, length: 500_000);
        using HttpClient http = ClientReturning(_ => Bytes(content));
        var reports = new List<double>();

        await UpdateInstaller.DownloadAsync(http, AssetFor(content), folder.File("new.exe"), new SyncProgress(reports), CancellationToken.None);

        Assert.True(reports.Count > 1, $"回報次數 = {reports.Count}");
        Assert.Equal(1.0, reports[^1]);
        Assert.Equal(reports.OrderBy(r => r), reports);
    }

    [Fact]
    public async Task 來源沒有公布雜湊值與大小時_只要像執行檔就接受()
    {
        using var folder = new TempFolder();
        byte[] content = FakeExecutable(1);
        using HttpClient http = ClientReturning(_ => Bytes(content));

        await UpdateInstaller.DownloadAsync(http, AssetFor(content, withHash: false, size: 0), folder.File("new.exe"), null, CancellationToken.None);

        Assert.True(File.Exists(folder.File("new.exe")));
    }

    [Fact]
    public async Task 雜湊值不符_丟出例外並刪除檔案()
    {
        using var folder = new TempFolder();
        byte[] content = FakeExecutable(1);
        byte[] tampered = FakeExecutable(2); // 大小相同、內容不同
        using HttpClient http = ClientReturning(_ => Bytes(tampered));

        var error = await Assert.ThrowsAsync<UpdateException>(
            () => UpdateInstaller.DownloadAsync(http, AssetFor(content), folder.File("new.exe"), null, CancellationToken.None));

        Assert.Contains("SHA-256", error.Message);
        Assert.False(File.Exists(folder.File("new.exe")));
    }

    [Fact]
    public async Task 檔案大小與公布的不同_丟出例外並刪除檔案()
    {
        using var folder = new TempFolder();
        byte[] content = FakeExecutable(1);
        using HttpClient http = ClientReturning(_ => Bytes(content));

        var error = await Assert.ThrowsAsync<UpdateException>(
            () => UpdateInstaller.DownloadAsync(http, AssetFor(content, size: content.Length + 1), folder.File("new.exe"), null, CancellationToken.None));

        Assert.Contains("大小", error.Message);
        Assert.False(File.Exists(folder.File("new.exe")));
    }

    [Fact]
    public async Task 內容太小_不像程式檔案()
    {
        using var folder = new TempFolder();
        byte[] errorPage = [(byte)'M', (byte)'Z', 1, 2, 3];
        using HttpClient http = ClientReturning(_ => Bytes(errorPage));

        await Assert.ThrowsAsync<UpdateException>(
            () => UpdateInstaller.DownloadAsync(http, AssetFor(errorPage, withHash: false, size: 0), folder.File("new.exe"), null, CancellationToken.None));
        Assert.False(File.Exists(folder.File("new.exe")));
    }

    [Fact]
    public async Task 內容不是執行檔_即使很大也拒絕()
    {
        using var folder = new TempFolder();
        byte[] html = new byte[200_000];
        Array.Fill(html, (byte)'<');
        using HttpClient http = ClientReturning(_ => Bytes(html));

        var error = await Assert.ThrowsAsync<UpdateException>(
            () => UpdateInstaller.DownloadAsync(http, AssetFor(html, withHash: false, size: 0), folder.File("new.exe"), null, CancellationToken.None));

        Assert.Contains("執行檔", error.Message);
        Assert.False(File.Exists(folder.File("new.exe")));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "404")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    public async Task 伺服器回傳錯誤_丟出例外且不留下檔案(HttpStatusCode code, string expectedInMessage)
    {
        using var folder = new TempFolder();
        using HttpClient http = ClientReturning(_ => Status(code));

        var error = await Assert.ThrowsAsync<UpdateException>(
            () => UpdateInstaller.DownloadAsync(http, AssetFor(FakeExecutable(1)), folder.File("new.exe"), null, CancellationToken.None));

        Assert.Contains(expectedInMessage, error.Message);
        Assert.False(File.Exists(folder.File("new.exe")));
    }

    [Fact]
    public async Task 下載途中被取消_不留下檔案()
    {
        using var folder = new TempFolder();
        byte[] content = FakeExecutable(1);
        using HttpClient http = ClientReturning(_ => Bytes(content));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => UpdateInstaller.DownloadAsync(http, AssetFor(content), folder.File("new.exe"), null, cancelled.Token));
        Assert.False(File.Exists(folder.File("new.exe")));
    }

    // ---- 替換檔案 ----

    [Fact]
    public void 替換成功_舊檔改名為old_新檔放到原位()
    {
        using var folder = new TempFolder();
        string current = folder.File("BatteryCPUMonitor.exe");
        string update = folder.File("BatteryCPUMonitor.exe.update.exe");
        File.WriteAllBytes(current, FakeExecutable(1));
        File.WriteAllBytes(update, FakeExecutable(2));

        UpdateInstaller.Replace(current, update, TimeSpan.Zero);

        Assert.Equal(FakeExecutable(2), File.ReadAllBytes(current));
        Assert.Equal(FakeExecutable(1), File.ReadAllBytes(UpdateInstaller.OldPathFor(current)));
        Assert.False(File.Exists(update));
    }

    [Fact]
    public void 上次留下的old檔案會被蓋掉()
    {
        using var folder = new TempFolder();
        string current = folder.File("BatteryCPUMonitor.exe");
        string update = folder.File("new.exe");
        File.WriteAllBytes(current, FakeExecutable(1));
        File.WriteAllBytes(update, FakeExecutable(2));
        File.WriteAllBytes(UpdateInstaller.OldPathFor(current), FakeExecutable(9));

        UpdateInstaller.Replace(current, update, TimeSpan.Zero);

        Assert.Equal(FakeExecutable(1), File.ReadAllBytes(UpdateInstaller.OldPathFor(current)));
    }

    [Fact]
    public void 找不到目前的程式檔案_丟出例外()
    {
        using var folder = new TempFolder();
        string update = folder.File("new.exe");
        File.WriteAllBytes(update, FakeExecutable(2));

        Assert.Throws<UpdateException>(() => UpdateInstaller.Replace(folder.File("missing.exe"), update, TimeSpan.Zero));
        Assert.True(File.Exists(update));
    }

    [Fact]
    public void 新檔案被占用無法移動_會還原成舊版本()
    {
        if (NotWindows) { return; } // 檔案占用的行為是 Windows 才有的

        using var folder = new TempFolder();
        string current = folder.File("BatteryCPUMonitor.exe");
        string update = folder.File("new.exe");
        File.WriteAllBytes(current, FakeExecutable(1));
        File.WriteAllBytes(update, FakeExecutable(2));

        using (new FileStream(update, FileMode.Open, FileAccess.Read, FileShare.None)) // 模擬被防毒軟體鎖住
        {
            var error = Assert.Throws<UpdateException>(() => UpdateInstaller.Replace(current, update, TimeSpan.FromMilliseconds(5)));

            Assert.Contains("已還原", error.Message);
        }

        Assert.Equal(FakeExecutable(1), File.ReadAllBytes(current));
        Assert.False(File.Exists(UpdateInstaller.OldPathFor(current)));
        Assert.Equal(FakeExecutable(2), File.ReadAllBytes(update));
    }

    [Fact]
    public void 新檔案只是暫時被占用_稍後重試就成功()
    {
        if (NotWindows) { return; }

        using var folder = new TempFolder();
        string current = folder.File("BatteryCPUMonitor.exe");
        string update = folder.File("new.exe");
        File.WriteAllBytes(current, FakeExecutable(1));
        File.WriteAllBytes(update, FakeExecutable(2));

        var holder = new FileStream(update, FileMode.Open, FileAccess.Read, FileShare.None);
        Task.Run(() =>
        {
            Thread.Sleep(150);
            holder.Dispose();
        });

        UpdateInstaller.Replace(current, update, TimeSpan.FromMilliseconds(100));

        Assert.Equal(FakeExecutable(2), File.ReadAllBytes(current));
    }

    [Fact]
    public void 執行中的程式也能被改名換掉()
    {
        if (NotWindows) { return; }

        string ping = Path.Combine(Environment.SystemDirectory, "PING.EXE");
        if (!File.Exists(ping)) { return; }

        using var folder = new TempFolder();
        string current = folder.File("BatteryCPUMonitor.exe");
        string update = folder.File("new.exe");
        File.Copy(ping, current);
        File.WriteAllBytes(update, FakeExecutable(7));

        var startInfo = new ProcessStartInfo(current, "-n 30 127.0.0.1")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        using Process process = Process.Start(startInfo)!;
        try
        {
            Thread.Sleep(500);
            Assert.False(process.HasExited, "測試用的行程已經結束，無法驗證");

            UpdateInstaller.Replace(current, update, TimeSpan.FromMilliseconds(50));

            Assert.Equal(FakeExecutable(7), File.ReadAllBytes(current));
            Assert.True(File.Exists(UpdateInstaller.OldPathFor(current)));
            Assert.False(process.HasExited, "替換之後原本的行程應該還在執行");
        }
        finally
        {
            process.Kill();
            process.WaitForExit();
        }

        UpdateInstaller.DeleteLeftovers(current); // 行程結束後就刪得掉了
        Assert.False(File.Exists(UpdateInstaller.OldPathFor(current)));
    }

    // ---- 善後與檢查 ----

    [Fact]
    public void 善後會刪掉old與下載暫存檔_但不動正式檔案()
    {
        using var folder = new TempFolder();
        string current = folder.File("BatteryCPUMonitor.exe");
        File.WriteAllBytes(current, FakeExecutable(1));
        File.WriteAllBytes(UpdateInstaller.OldPathFor(current), FakeExecutable(2));
        File.WriteAllBytes(UpdateInstaller.DownloadPathFor(current), FakeExecutable(3));

        UpdateInstaller.DeleteLeftovers(current);

        Assert.True(File.Exists(current));
        Assert.False(File.Exists(UpdateInstaller.OldPathFor(current)));
        Assert.False(File.Exists(UpdateInstaller.DownloadPathFor(current)));
    }

    [Fact]
    public void 善後在沒有任何殘留或沒有路徑時不會出錯()
    {
        using var folder = new TempFolder();

        UpdateInstaller.DeleteLeftovers(folder.File("BatteryCPUMonitor.exe"));
        UpdateInstaller.DeleteLeftovers(null);
        UpdateInstaller.DeleteLeftovers("");
    }

    [Fact]
    public void 暫存資料夾可以寫入_不存在的資料夾不行()
    {
        using var folder = new TempFolder();

        Assert.True(UpdateInstaller.IsDirectoryWritable(folder.Path));
        Assert.False(UpdateInstaller.IsDirectoryWritable(folder.File("not-there")));
        Assert.Empty(Directory.GetFileSystemEntries(folder.Path)); // 檢查時建立的測試檔要清乾淨
    }

    [Fact]
    public void 暫存檔名稱與路徑的規則()
    {
        Assert.Equal(@"C:\T\BatteryCPUMonitor.exe.old", UpdateInstaller.OldPathFor(@"C:\T\BatteryCPUMonitor.exe"));
        Assert.Equal(@"C:\T\BatteryCPUMonitor.exe.update.exe", UpdateInstaller.DownloadPathFor(@"C:\T\BatteryCPUMonitor.exe"));
    }

    private sealed class SyncProgress(List<double> reports) : IProgress<double>
    {
        public void Report(double value) => reports.Add(value);
    }
}
