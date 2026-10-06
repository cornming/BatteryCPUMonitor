using System.ComponentModel;
using System.Diagnostics;
using System.Net;

namespace BatteryCPUMonitor.Updates;

/// <summary>
/// 「自我檢查」：以 <c>--self-check 檔案路徑</c> 啟動程式時，只把版本與種類寫進那個檔案就結束，不開任何視窗。
/// 更新時拿來試跑剛下載的新版本，確認它在這台電腦上真的跑得起來（例如精簡版需要的 .NET 版本有沒有裝）。
/// </summary>
internal static class SelfCheckRunner
{
    public const string Argument = "--self-check";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public static string BuildReport() =>
        $"version={AppVersion.CurrentText}\nvariant={AppVariantInfo.Name(AppVariantInfo.Current)}\n";

    /// <summary>若命令列要求自我檢查就執行並回傳 true（呼叫端應直接結束程式）。</summary>
    public static bool TryHandle(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || args[0] != Argument)
        {
            return false;
        }

        if (args.Length >= 2)
        {
            try
            {
                File.WriteAllText(args[1], BuildReport());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                exitCode = 2;
            }
        }

        return true;
    }

    /// <summary>試跑 <paramref name="exe"/>，版本必須等於 <paramref name="expected"/>。逾時、錯誤或版本不符都回傳 false。</summary>
    public static async Task<bool> RunAsync(string exe, Version expected, CancellationToken cancellationToken)
    {
        string report = Path.Combine(Path.GetTempPath(), $"BatteryCPUMonitor-selfcheck-{Guid.NewGuid():N}.txt");
        try
        {
            var startInfo = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
            };
            startInfo.ArgumentList.Add(Argument);
            startInfo.ArgumentList.Add(report);

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                return false;
            }

            return process.ExitCode == 0 && ReportMatches(report, expected);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
        finally
        {
            try
            {
                File.Delete(report);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 暫存檔刪不掉不影響結果。
            }
        }
    }

    /// <summary>報告檔裡的版本是否等於預期。</summary>
    internal static bool ReportMatches(string reportFile, Version expected)
    {
        if (!File.Exists(reportFile))
        {
            return false;
        }

        foreach (string line in File.ReadAllLines(reportFile))
        {
            if (line.StartsWith("version=", StringComparison.Ordinal))
            {
                return AppVersion.TryParse(line["version=".Length..], out Version actual) && actual == AppVersion.Normalize(expected);
            }
        }

        return false;
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // 行程已經結束。
        }
    }
}

/// <summary>換好新版本之後，啟動它。</summary>
internal static class UpdateRelauncher
{
    /// <summary>新版本啟動時帶這個參數：等舊版本結束再接手，並顯示「已更新」的提示。</summary>
    public const string AfterUpdateArgument = "--after-update";

    public static bool Relaunch(string exe)
    {
        try
        {
            var startInfo = new ProcessStartInfo(exe, AfterUpdateArgument)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
            };

            using Process? process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>更新用的網路連線。使用系統的 Proxy 設定，公司網路需要驗證時沿用目前登入的帳號。</summary>
internal static class UpdateHttp
{
    private static readonly Lazy<HttpClient> Follow = new(() => Create(followRedirects: true));
    private static readonly Lazy<HttpClient> NoRedirect = new(() => Create(followRedirects: false));

    public static HttpClient Client => Follow.Value;

    public static HttpClient NoRedirectClient => NoRedirect.Value;

    private static HttpClient Create(bool followRedirects)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = followRedirects,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        };

        // 各個要求自己設逾時（查詢 20 秒、下載 15 分鐘），這裡不設總逾時。
        var client = new HttpClient(handler, disposeHandler: true) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"BatteryCPUMonitor/{AppVersion.CurrentText}");
        return client;
    }
}
