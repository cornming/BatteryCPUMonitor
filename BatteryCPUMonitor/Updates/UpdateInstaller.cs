using System.Security.Cryptography;

namespace BatteryCPUMonitor.Updates;

/// <summary>更新過程中可以直接顯示給使用者看的錯誤。</summary>
internal sealed class UpdateException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>下載新版本、驗證內容，並把執行中的程式換成新版本。</summary>
internal static class UpdateInstaller
{
    /// <summary>低於這個大小不可能是正常的程式（精簡版約 0.3 MB），多半是錯誤頁面。</summary>
    private const long MinimumExecutableBytes = 100_000;

    /// <summary>新版本先下載到這裡，確認沒問題才換到正式位置。與正式檔案在同一個資料夾，改名才是瞬間完成的。</summary>
    public static string DownloadPathFor(string currentExe) => currentExe + ".update.exe";

    /// <summary>舊版本被換下來之後暫放的名稱，下次啟動時刪除。</summary>
    public static string OldPathFor(string currentExe) => currentExe + ".old";

    /// <summary>
    /// 下載檔案並驗證：HTTP 狀態、檔案大小、SHA-256（有公布時）、是否為 Windows 執行檔。
    /// 任何一項不符就刪掉下載到一半的檔案並丟出 <see cref="UpdateException"/>。
    /// </summary>
    public static async Task DownloadAsync(
        HttpClient http,
        ReleaseAsset asset,
        string destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            string actualHash;
            long written;

            using (HttpResponseMessage response = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new UpdateException($"下載失敗（HTTP {(int)response.StatusCode}）。");
                }

                long total = response.Content.Headers.ContentLength ?? asset.Size;
                await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                byte[] buffer = new byte[81920];
                written = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                    written += read;
                    if (total > 0)
                    {
                        progress?.Report(Math.Min(1.0, (double)written / total));
                    }
                }

                await target.FlushAsync(cancellationToken);
                actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }

            Verify(destination, asset, written, actualHash);
        }
        catch
        {
            TryDelete(destination);
            throw;
        }
    }

    private static void Verify(string file, ReleaseAsset asset, long written, string actualHash)
    {
        if (asset.Size > 0 && written != asset.Size)
        {
            throw new UpdateException($"下載的檔案大小不對（應為 {asset.Size:N0}，實際 {written:N0} 位元組），可能是下載中斷了。");
        }

        if (written < MinimumExecutableBytes)
        {
            throw new UpdateException("下載到的內容太小，不像是程式檔案。");
        }

        if (asset.Sha256 is not null && !string.Equals(asset.Sha256, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateException("下載的檔案與 GitHub 公布的 SHA-256 不符，已捨棄。");
        }

        if (!LooksLikeExecutable(file))
        {
            throw new UpdateException("下載到的不是 Windows 執行檔。");
        }
    }

    /// <summary>Windows 執行檔一定以「MZ」開頭。</summary>
    internal static bool LooksLikeExecutable(string file)
    {
        using FileStream stream = File.OpenRead(file);
        return stream.ReadByte() == 'M' && stream.ReadByte() == 'Z';
    }

    /// <summary>
    /// 把執行中的 <paramref name="currentExe"/> 換成 <paramref name="newExe"/>。
    /// Windows 不讓人刪除或覆蓋執行中的程式，但允許改名，所以先把舊的改名為 .old、再把新的移到原位。
    /// 第二步失敗時會把舊的改回來，不會留下沒有程式可用的狀態。
    /// </summary>
    /// <param name="retryDelay">新檔案暫時被防毒軟體或其他程式占用時，重試之間等待的時間。</param>
    public static void Replace(string currentExe, string newExe, TimeSpan? retryDelay = null)
    {
        string old = OldPathFor(currentExe);

        try
        {
            if (File.Exists(old))
            {
                File.Delete(old);
            }

            File.Move(currentExe, old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException("無法替換目前的程式檔案（可能被占用或沒有寫入權限）。", ex);
        }

        try
        {
            MoveWithRetry(newExe, currentExe, retryDelay ?? TimeSpan.FromMilliseconds(300));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Move(old, currentExe);
            }
            catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException)
            {
                throw new UpdateException($"更新失敗，而且無法還原舊版本。舊版本檔案現在叫「{old}」，請手動改回原名。", restoreError);
            }

            throw new UpdateException("無法放入新版本，已還原成舊版本。", ex);
        }
    }

    private static void MoveWithRetry(string source, string destination, TimeSpan delay)
    {
        const int attempts = 5;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, destination);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(delay);
            }
        }
    }

    /// <summary>刪除上次更新留下的暫存檔，失敗就算了（下次啟動再試）。</summary>
    public static void DeleteLeftovers(string? currentExe)
    {
        if (string.IsNullOrEmpty(currentExe))
        {
            return;
        }

        TryDelete(OldPathFor(currentExe));
        TryDelete(DownloadPathFor(currentExe));
    }

    /// <summary>程式所在的資料夾能不能寫入（放在需要管理員權限的位置就不能自己更新）。</summary>
    public static bool IsDirectoryWritable(string directory)
    {
        try
        {
            string probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>刪除還沒換上去的下載檔。</summary>
    public static void DeleteDownload(string currentExe) => TryDelete(DownloadPathFor(currentExe));

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 暫存檔刪不掉不影響使用。
        }
    }
}
