using System.Net;

namespace BatteryCPUMonitor.Updates;

internal enum CheckStatus
{
    /// <summary>已經是最新版本。</summary>
    UpToDate,

    /// <summary>有新版本可以更新。</summary>
    Available,

    /// <summary>這種執行方式不支援自動更新（開發版、用 dotnet 執行等）。</summary>
    NotSupported,

    /// <summary>查詢失敗（沒有網路、被 GitHub 擋下等）。</summary>
    Failed,
}

internal sealed record UpdateCheck(CheckStatus Status, LatestRelease? Release = null, string Message = "");

internal enum InstallStatus
{
    /// <summary>已換成新版本。</summary>
    Installed,

    /// <summary>沒有更新成功，原本的版本不受影響。</summary>
    Failed,
}

/// <param name="Restarted">新版本是否已經啟動。true 時，目前這個程式應該立刻結束。</param>
internal sealed record InstallResult(InstallStatus Status, string Message = "", bool Restarted = false);

/// <summary>
/// 檢查更新、下載、驗證、替換、重新啟動的完整流程。畫面上的提示由呼叫端負責，這裡只做事、回報結果。
/// </summary>
internal sealed class UpdateController
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);

    private readonly HttpClient _http;
    private readonly HttpClient _noRedirectHttp;
    private readonly string _currentExe;
    private readonly Version _currentVersion;
    private readonly AppVariant _variant;
    private readonly Func<string, Version, CancellationToken, Task<bool>> _selfCheck;
    private readonly Func<string, bool> _relaunch;

    /// <param name="http">一般的 HTTP 用戶端（會自動跟隨轉址）。</param>
    /// <param name="noRedirectHttp">不跟隨轉址的用戶端，只用在「GitHub API 被限流時，改讀最新版本頁面的轉址」。</param>
    /// <param name="currentExe">目前執行中的程式路徑；更新就是把這個檔案換掉。</param>
    /// <param name="selfCheck">拿新下載的檔案試跑一次，確認能在這台電腦上執行，而且版本與預期相同。</param>
    /// <param name="relaunch">啟動換好的新版本；成功回傳 true。</param>
    public UpdateController(
        HttpClient http,
        HttpClient noRedirectHttp,
        string currentExe,
        Version currentVersion,
        AppVariant variant,
        Func<string, Version, CancellationToken, Task<bool>> selfCheck,
        Func<string, bool> relaunch)
    {
        _http = http;
        _noRedirectHttp = noRedirectHttp;
        _currentExe = currentExe;
        _currentVersion = currentVersion;
        _variant = variant;
        _selfCheck = selfCheck;
        _relaunch = relaunch;
    }

    /// <summary>查詢有沒有新版本。不會下載或改動任何檔案。</summary>
    public async Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken)
    {
        if (!UpdatePolicy.CanSelfUpdate(_currentExe))
        {
            return new UpdateCheck(CheckStatus.NotSupported, Message: "目前的執行方式不支援自動更新。");
        }

        if (UpdatePolicy.IsDevelopment(_currentVersion))
        {
            return new UpdateCheck(CheckStatus.NotSupported, Message: "目前是從原始碼建置的開發版，不檢查更新。");
        }

        LatestRelease latest;
        try
        {
            latest = await FetchLatestAsync(cancellationToken);
        }
        catch (UpdateException ex)
        {
            return new UpdateCheck(CheckStatus.Failed, Message: ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return new UpdateCheck(CheckStatus.Failed, Message: $"無法連到 GitHub：{ex.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheck(CheckStatus.Failed, Message: "連線 GitHub 逾時。");
        }

        if (!UpdatePolicy.IsNewer(latest.Version, _currentVersion))
        {
            return new UpdateCheck(CheckStatus.UpToDate, latest);
        }

        return latest.Asset is null
            ? new UpdateCheck(CheckStatus.Failed, latest, $"新版本 v{AppVersion.Display(latest.Version)} 沒有適用的下載檔。")
            : new UpdateCheck(CheckStatus.Available, latest);
    }

    /// <summary>下載並安裝 <paramref name="release"/>，成功後啟動新版本。失敗時原本的版本完全不受影響。</summary>
    public async Task<InstallResult> InstallAsync(LatestRelease release, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (release.Asset is not ReleaseAsset asset)
        {
            return new InstallResult(InstallStatus.Failed, "沒有可下載的檔案。");
        }

        string directory = Path.GetDirectoryName(_currentExe) ?? string.Empty;
        if (!UpdateInstaller.IsDirectoryWritable(directory))
        {
            return new InstallResult(
                InstallStatus.Failed,
                $"程式所在的資料夾無法寫入（{directory}）。請把程式放到可以寫入的位置，或到 {ReleaseSource.ReleasesPage} 手動下載。");
        }

        string download = UpdateInstaller.DownloadPathFor(_currentExe);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(DownloadTimeout);

            await UpdateInstaller.DownloadAsync(_http, asset, download, progress, timeout.Token);

            if (!await _selfCheck(download, release.Version, timeout.Token))
            {
                return new InstallResult(InstallStatus.Failed, "下載的新版本無法在這台電腦上執行，已取消更新，目前的版本不受影響。");
            }

            UpdateInstaller.Replace(_currentExe, download);
            return new InstallResult(InstallStatus.Installed, Restarted: _relaunch(_currentExe));
        }
        catch (UpdateException ex)
        {
            return new InstallResult(InstallStatus.Failed, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return new InstallResult(InstallStatus.Failed, $"下載時網路連線失敗：{ex.Message}");
        }
        catch (OperationCanceledException)
        {
            return new InstallResult(InstallStatus.Failed, "下載逾時或已取消。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new InstallResult(InstallStatus.Failed, $"寫入檔案失敗：{ex.Message}");
        }
        finally
        {
            // 成功時檔案已經被移走；失敗時清掉殘留。
            UpdateInstaller.DeleteDownload(_currentExe);
        }
    }

    /// <summary>
    /// 先問 GitHub API（有檔案大小與 SHA-256 可以驗證）。
    /// 不登入的 API 每個 IP 每小時只能問 60 次，同一個辦公室網路的很多人一起用時可能被擋；
    /// 被擋時改讀「最新版本」頁面的轉址，這條路沒有次數限制，只是少了雜湊值可驗證。
    /// </summary>
    private async Task<LatestRelease> FetchLatestAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseSource.ApiLatest);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using HttpResponseMessage response = await _http.SendAsync(request, timeout.Token);

        if (response.IsSuccessStatusCode)
        {
            string json = await response.Content.ReadAsStringAsync(timeout.Token);
            return ReleaseParser.Parse(json, _variant) ?? throw new UpdateException("看不懂 GitHub 回傳的版本資訊。");
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            return await FetchLatestFromRedirectAsync(timeout.Token);
        }

        throw new UpdateException($"查詢最新版本失敗（HTTP {(int)response.StatusCode}）。");
    }

    private async Task<LatestRelease> FetchLatestFromRedirectAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _noRedirectHttp.GetAsync(
            ReleaseSource.PageLatest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        Uri? location = response.Headers.Location;
        bool redirected = (int)response.StatusCode is >= 300 and < 400;
        string? tag = location?.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();

        if (!redirected
            || location is null
            || !location.AbsolutePath.Contains("/releases/tag/", StringComparison.Ordinal)
            || !ReleaseSource.IsValidTag(tag)
            || !AppVersion.TryParse(tag, out Version version))
        {
            throw new UpdateException("GitHub 暫時限制了查詢次數，稍後再試。");
        }

        var asset = new ReleaseAsset(ReleaseSource.AssetName(tag!, _variant), ReleaseSource.AssetUrl(tag!, _variant), Size: 0, Sha256: null);
        return new LatestRelease(tag!, version, Notes: string.Empty, asset);
    }
}
