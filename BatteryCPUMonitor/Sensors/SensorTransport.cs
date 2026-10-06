using System.Text;

namespace BatteryCPUMonitor.Sensors;

/// <summary>感測器的資料來源。實際的實作在特權行程裡讀硬體；測試時換成假的。</summary>
internal interface ISensorSource : IDisposable
{
    /// <summary>讀一次目前的所有數值。可能花上一點時間，不會丟出例外（出錯時回傳 <see cref="SensorMessage.Failure"/>）。</summary>
    SensorMessage Read();
}

/// <summary>特權行程這一端：每隔一段時間讀一次，寫成一行 JSON 送出去。</summary>
internal static class SensorHostLoop
{
    /// <summary>
    /// 一直送到取消，或對方斷線（寫入失敗）為止。
    /// 這個行程沒有任何輸入要處理：它只送出數值，不接受任何指令。
    /// </summary>
    public static async Task RunAsync(Stream output, ISensorSource source, TimeSpan interval, CancellationToken cancellationToken)
    {
        var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\n", AutoFlush = false };
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                DateTime started = DateTime.UtcNow;
                SensorMessage message = await Task.Run(source.Read, CancellationToken.None);

                try
                {
                    await writer.WriteLineAsync(SensorJson.Serialize(message).AsMemory(), cancellationToken);
                    await writer.FlushAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
                {
                    return; // 對方（主程式）已經離開，或被要求停止
                }

                TimeSpan wait = interval - (DateTime.UtcNow - started);
                if (wait > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(wait, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }
        finally
        {
            // 釋放 StreamWriter 時會再沖一次緩衝區；這時對方多半已經斷線，寫不進去是正常的，不能因此丟出例外。
            try
            {
                await writer.DisposeAsync();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
            }
        }
    }
}

/// <summary>主程式這一端：一行一行讀，解析成訊息。</summary>
internal static class SensorStreamReader
{
    /// <summary>讀到串流結束（對方斷線）或取消為止。看不懂的行直接略過。</summary>
    public static async Task ReadAsync(Stream input, Action<SensorMessage> onMessage, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(input, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    return; // 串流結束
                }

                if (SensorJson.TryDeserialize(line) is SensorMessage message)
                {
                    onMessage(message);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // 斷線或被取消，都是結束的正常方式。
        }
    }
}

/// <summary>啟動特權行程時的命令列參數。</summary>
internal sealed record SensorHostArguments(string PipeName, string UserSid, int ParentProcessId)
{
    public const string Flag = "--sensor-host";

    /// <summary>組成命令列字串。管道名稱與 SID 只含英數字、點、減號，不需要引號。</summary>
    public string Build() => $"{Flag} --pipe {PipeName} --sid {UserSid} --parent {ParentProcessId}";

    /// <summary>不是感測器服務的啟動方式，或參數不齊全、含有可疑字元時回傳 null。</summary>
    public static SensorHostArguments? TryParse(string[] args)
    {
        if (args.Length == 0 || args[0] != Flag)
        {
            return null;
        }

        string? pipe = ValueOf(args, "--pipe");
        string? sid = ValueOf(args, "--sid");
        string? parent = ValueOf(args, "--parent");

        if (!IsSafeToken(pipe) || !IsSafeToken(sid) || !int.TryParse(parent, out int parentId) || parentId <= 0)
        {
            return null;
        }

        return new SensorHostArguments(pipe!, sid!, parentId);
    }

    private static string? ValueOf(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>只允許英數字、點、減號、底線：管道名稱與 SID 本來就只會有這些，其他一律拒絕，避免奇怪的路徑或指令。</summary>
    private static bool IsSafeToken(string? token) =>
        !string.IsNullOrEmpty(token)
        && token.Length <= 200
        && !token.StartsWith("--", StringComparison.Ordinal) // 少了值時，下一個參數會被誤當成值
        && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
}

/// <summary>PawnIO（讀取 CPU、主機板感測器需要的核心驅動程式）有沒有安裝。</summary>
internal static class PawnIoDetector
{
    public const string OfficialSite = "https://pawnio.eu/";
    public const string WingetCommand = "winget install --id namazso.PawnIO -e";

    /// <summary>
    /// 官方文件的做法：先讀登錄檔的安裝位置，沒有的話退回「Program Files\PawnIO」，再確認資料夾裡有函式庫。
    /// </summary>
    /// <param name="registryInstallLocation">登錄檔裡的 InstallLocation；讀不到為 null。</param>
    /// <param name="programFiles">Program Files 資料夾的路徑。</param>
    /// <param name="fileExists">檢查檔案是否存在的函式（測試時替換）。</param>
    public static bool IsInstalled(string? registryInstallLocation, string? programFiles, Func<string, bool> fileExists)
    {
        foreach (string? folder in new[] { registryInstallLocation, string.IsNullOrWhiteSpace(programFiles) ? null : Path.Combine(programFiles, "PawnIO") })
        {
            if (!string.IsNullOrWhiteSpace(folder) && fileExists(Path.Combine(folder, "PawnIOLib.dll")))
            {
                return true;
            }
        }

        return false;
    }
}
