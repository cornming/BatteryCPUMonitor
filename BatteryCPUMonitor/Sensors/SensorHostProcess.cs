using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace BatteryCPUMonitor.Sensors;

/// <summary>
/// 特權的「感測器服務」行程：以 <c>--sensor-host</c> 啟動同一個 exe，不開任何視窗。
/// 建立一條只有指定使用者能連的具名管道，連上之後每秒送出一則感測器數值；
/// 主程式斷線或結束時，這個行程也跟著結束。
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class SensorHostProcess
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    /// <summary>等主程式連上來的最長時間，超過就放棄（使用者同意之後，主程式很快就會連）。</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMinutes(2);

    /// <summary>若命令列要求啟動感測器服務就執行並回傳 true（呼叫端應直接結束程式）。</summary>
    public static bool TryHandle(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || args[0] != SensorHostArguments.Flag)
        {
            return false;
        }

        SensorHostArguments? parsed = SensorHostArguments.TryParse(args);
        exitCode = parsed is null ? 2 : RunAsync(parsed).GetAwaiter().GetResult();
        return true;
    }

    private static async Task<int> RunAsync(SensorHostArguments arguments)
    {
        using var stop = new CancellationTokenSource();
        _ = WatchParentAsync(arguments.ParentProcessId, stop);

        try
        {
            await using NamedPipeServerStream pipe = CreatePipe(arguments);

            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            connectTimeout.CancelAfter(ConnectTimeout);
            await pipe.WaitForConnectionAsync(connectTimeout.Token);

            using ISensorSource source = CreateSource();
            await SensorHostLoop.RunAsync(pipe, source, Interval, stop.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0; // 主程式先結束了，或等不到連線
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return 1;
        }
    }

    /// <summary>
    /// 管道只允許啟動它的使用者（以 SID 指定）與系統管理員連線，其他人（包含其他使用者的行程）都連不上。
    /// 這個行程以系統管理員權限執行，而主程式是一般權限，所以一定要明確授權，否則主程式連不進來。
    /// </summary>
    private static NamedPipeServerStream CreatePipe(SensorHostArguments arguments)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(arguments.UserSid), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            arguments.PipeName,
            PipeDirection.Out,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 64 * 1024,
            security);
    }

    /// <summary>
    /// 建立感測器來源。獨立成一個不內嵌的方法，這樣只有真的以感測器服務啟動時，
    /// 才會載入 LibreHardwareMonitor，一般啟動、自我檢查都不受影響。
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ISensorSource CreateSource()
    {
        try
        {
            return new LhmSensorSource();
        }
        catch (Exception ex)
        {
            // 初始化失敗（例如驅動程式被安全軟體擋下）時仍然連線並說明原因，讓主程式顯示給使用者看。
            return new FailedSensorSource(ex.Message, ex.ToString());
        }
    }

    private static async Task WatchParentAsync(int parentProcessId, CancellationTokenSource stop)
    {
        try
        {
            using Process parent = Process.GetProcessById(parentProcessId);
            await parent.WaitForExitAsync();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // 主程式已經不在了。
        }

        stop.Cancel();
    }

    private sealed class FailedSensorSource(string error, string detail) : ISensorSource
    {
        private bool _detailSent;

        public SensorMessage Read()
        {
            SensorMessage failure = SensorMessage.Failure(error);
            if (_detailSent)
            {
                return failure;
            }

            _detailSent = true;
            return failure with { Diagnostics = SensorDiagnostics.ForInitFailure(SensorEnvironment.IsElevated(), detail) };
        }

        public void Dispose()
        {
        }
    }
}
