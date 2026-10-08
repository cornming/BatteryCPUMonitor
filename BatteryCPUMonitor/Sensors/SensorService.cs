using System.IO.Pipes;

namespace BatteryCPUMonitor.Sensors;

internal enum SensorState
{
    /// <summary>沒有啟用。</summary>
    Off,

    /// <summary>已要求啟動，正在等使用者同意、感測器服務初始化。</summary>
    Starting,

    /// <summary>已連線，正在收數值。</summary>
    Connected,

    /// <summary>使用者在系統管理員權限的確認視窗按了「否」。</summary>
    Declined,

    /// <summary>連不上，或服務結束了。</summary>
    Failed,
}

/// <summary>啟動特權的感測器服務行程。</summary>
internal interface ISensorHostLauncher
{
    /// <exception cref="SensorLaunchDeclinedException">使用者拒絕了系統管理員權限。</exception>
    void Launch(string pipeName);
}

internal sealed class SensorLaunchDeclinedException : Exception;

/// <summary>
/// 主程式這一端的感測器服務：要求啟動特權行程、連上管道、持續收數值。
/// 任何狀態變化都不會丟出例外，結果透過 <see cref="State"/>、<see cref="Status"/>、<see cref="Latest"/> 讀取。
/// </summary>
internal sealed class SensorService : IDisposable
{
    private readonly ISensorHostLauncher _launcher;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _firstMessageTimeout;
    private readonly TimeSpan _staleAfter;
    private readonly object _gate = new();

    private CancellationTokenSource? _cts;
    private SensorState _state = SensorState.Off;
    private string _status = string.Empty;
    private SensorValues? _latest;
    private SensorMessage? _lastMessage;
    private SensorDiagnostics? _diagnostics;
    private long _lastMessageTick;

    /// <param name="connectTimeout">啟動後最多等多久連上管道（要包含使用者按下同意的時間）。</param>
    /// <param name="firstMessageTimeout">連上之後最多等多久收到第一則數值（感測器服務初始化要一點時間）。</param>
    /// <param name="staleAfter">超過這麼久沒收到新數值，就不再顯示舊的。</param>
    public SensorService(ISensorHostLauncher launcher, TimeSpan connectTimeout, TimeSpan firstMessageTimeout, TimeSpan staleAfter)
    {
        _launcher = launcher;
        _connectTimeout = connectTimeout;
        _firstMessageTimeout = firstMessageTimeout;
        _staleAfter = staleAfter;
    }

    /// <summary>狀態改變時引發（在背景執行緒上）。</summary>
    public event Action? Changed;

    public SensorState State
    {
        get { lock (_gate) { return _state; } }
    }

    /// <summary>給人看的狀態說明，例如失敗的原因。</summary>
    public string Status
    {
        get { lock (_gate) { return _status; } }
    }

    /// <summary>最近一次收到的數值；太久沒更新（服務卡住了）或還沒連線時為 null。</summary>
    public SensorValues? Latest
    {
        get
        {
            lock (_gate)
            {
                bool fresh = _state == SensorState.Connected
                    && Environment.TickCount64 - _lastMessageTick <= _staleAfter.TotalMilliseconds;
                return fresh ? _latest : null;
            }
        }
    }

    /// <summary>偵測到的硬體名稱（最近一則訊息裡的）。</summary>
    public IReadOnlyList<string> HardwareNames
    {
        get { lock (_gate) { return _lastMessage?.Hardware ?? []; } }
    }

    /// <summary>
    /// 感測器服務啟動後送來的診斷資料（第一則訊息帶的）。服務沒起來、還在初始化，或重新啟動後還沒收到時為 null。
    /// 連線中斷後仍然保留，方便查原因；重新啟動或停止時清掉。
    /// </summary>
    public SensorDiagnostics? HostDiagnostics
    {
        get { lock (_gate) { return _diagnostics; } }
    }

    /// <summary>要求啟動。已經在啟動中或已連線時不會重複啟動。立即返回，後續在背景進行。</summary>
    public void Start()
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_state is SensorState.Starting or SensorState.Connected)
            {
                return;
            }

            _cts?.Dispose();
            _cts = cts = new CancellationTokenSource();
            _diagnostics = null; // 上一次的診斷資料不能拿來解釋這一次
            Set(SensorState.Starting, "等待系統管理員權限與感測器初始化…");
        }

        _ = Task.Run(() => RunAsync(cts.Token));
    }

    /// <summary>停止並放開連線；特權行程偵測到斷線會自己結束。</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _latest = null;
            _lastMessage = null;
            _diagnostics = null;
        }

        Set(SensorState.Off, string.Empty);
    }

    public void Dispose() => Stop();

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        string pipeName = "BatteryCPUMonitor.Sensors." + Guid.NewGuid().ToString("N");
        try
        {
            try
            {
                _launcher.Launch(pipeName);
            }
            catch (SensorLaunchDeclinedException)
            {
                SetIfActive(cancellationToken, SensorState.Declined, "你沒有同意系統管理員權限，硬體感測器沒有啟用。");
                return;
            }

            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync((int)_connectTimeout.TotalMilliseconds, cancellationToken);
            }
            catch (TimeoutException)
            {
                SetIfActive(cancellationToken, SensorState.Failed, "連不上感測器服務（逾時）。");
                return;
            }

            var firstMessage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task reader = SensorStreamReader.ReadAsync(pipe, message => OnMessage(message, firstMessage, cancellationToken), cancellationToken);

            Task finished = await Task.WhenAny(firstMessage.Task, reader, Task.Delay(_firstMessageTimeout, cancellationToken));
            if (finished != firstMessage.Task && !cancellationToken.IsCancellationRequested)
            {
                SetIfActive(cancellationToken, SensorState.Failed, finished == reader ? "感測器服務提前結束了。" : "感測器服務一直沒有回應。");
                return;
            }

            await reader;
            SetIfActive(cancellationToken, SensorState.Failed, "感測器服務已經結束。");
        }
        catch (OperationCanceledException)
        {
            // 呼叫了 Stop()。
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetIfActive(cancellationToken, SensorState.Failed, $"無法啟動感測器服務：{ex.Message}");
        }
    }

    private void OnMessage(SensorMessage message, TaskCompletionSource firstMessage, CancellationToken cancellationToken)
    {
        // Stop() 之後可能還有一則訊息在路上，不能因此又把狀態改回已連線。
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (message.Version != SensorMessage.CurrentVersion)
        {
            Set(SensorState.Failed, "感測器服務的版本與主程式不一致（更新程式後請重新啟用）。");
            firstMessage.TrySetResult();
            return;
        }

        lock (_gate)
        {
            _lastMessage = message;
            _diagnostics = message.Diagnostics ?? _diagnostics;
            _latest = SensorSelector.Select(message.Readings);
            _lastMessageTick = Environment.TickCount64;
        }

        Set(
            SensorState.Connected,
            message.Ok ? string.Empty : $"感測器服務初始化失敗：{message.Error}");
        firstMessage.TrySetResult();
    }

    private void SetIfActive(CancellationToken token, SensorState state, string status)
    {
        if (!token.IsCancellationRequested)
        {
            Set(state, status);
        }
    }

    private void Set(SensorState state, string status)
    {
        bool changed;
        lock (_gate)
        {
            changed = _state != state || _status != status;
            _state = state;
            _status = status;
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }
}
