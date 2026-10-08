using System.IO.Pipes;
using BatteryCPUMonitor.Sensors;
using Xunit;
using static BatteryCPUMonitor.Tests.SensorTransportTests;

namespace BatteryCPUMonitor.Tests;

public class SensorServiceTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(15);

    /// <summary>假的啟動器：記錄被要求啟動幾次，並在背景扮演感測器服務（建立管道、送出數值）。</summary>
    private sealed class FakeLauncher(Action<string, CancellationToken>? host = null, Exception? launchError = null) : ISensorHostLauncher, IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private Task _hostTask = Task.CompletedTask;

        public int Launches { get; private set; }

        public void Launch(string pipeName)
        {
            Launches++;
            if (launchError is not null)
            {
                throw launchError;
            }

            if (host is not null)
            {
                CancellationToken token = _stop.Token;
                _hostTask = Task.Run(() => host(pipeName, token));
            }
        }

        public bool HostFinishedWithin(TimeSpan timeout) => _hostTask.Wait(timeout);

        public void Dispose() => _stop.Cancel();
    }

    /// <summary>扮演正常運作的服務：連線後持續送出同一組數值。</summary>
    private static Action<string, CancellationToken> NormalHost(Func<int, SensorMessage> next, int? stopAfterMessages = null) => (pipeName, token) =>
    {
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        try
        {
            server.WaitForConnectionAsync(token).GetAwaiter().GetResult();
            using var source = new FakeSource(next);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task loop = SensorHostLoop.RunAsync(server, source, TimeSpan.FromMilliseconds(30), limit.Token);
            if (stopAfterMessages is int count)
            {
                while (source.Calls < count && !loop.IsCompleted)
                {
                    Thread.Sleep(10);
                }

                limit.Cancel();
            }

            loop.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
    };

    private static SensorService Service(ISensorHostLauncher launcher, int connectMs = 10_000, int firstMessageMs = 10_000, int staleMs = 10_000) =>
        new(launcher, TimeSpan.FromMilliseconds(connectMs), TimeSpan.FromMilliseconds(firstMessageMs), TimeSpan.FromMilliseconds(staleMs));

    private static async Task WaitUntil(Func<bool> condition, string what, TimeSpan? timeout = null)
    {
        DateTime until = DateTime.UtcNow + (timeout ?? Long);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException($"等不到：{what}");
            }

            await Task.Delay(15);
        }
    }

    private static SensorMessage CpuTemp(double value) =>
        Message(new SensorReading("Cpu", "Temperature", "CPU Package", value), new SensorReading("SuperIO", "Fan", "CPU Fan", 1300));

    [Fact]
    public async Task 啟動後連上服務_收到數值_狀態變成已連線()
    {
        using var launcher = new FakeLauncher(NormalHost(_ => CpuTemp(57)));
        using SensorService service = Service(launcher);

        service.Start();
        await WaitUntil(() => service.State == SensorState.Connected, "已連線");
        await WaitUntil(() => service.Latest is not null, "有數值");

        Assert.Equal(57.0, service.Latest!.CpuTemperature);
        Assert.Equal("CPU風扇", Assert.Single(service.Latest.Fans).Label);
        Assert.Equal(new[] { "Cpu: 測試處理器" }, service.HardwareNames);
    }

    [Fact]
    public async Task 停止後狀態是未啟用_數值清空_服務端自己結束()
    {
        using var launcher = new FakeLauncher(NormalHost(_ => CpuTemp(57)));
        using SensorService service = Service(launcher);
        service.Start();
        await WaitUntil(() => service.Latest is not null, "有數值");

        service.Stop();

        Assert.Equal(SensorState.Off, service.State);
        Assert.Null(service.Latest);
        Assert.True(launcher.HostFinishedWithin(TimeSpan.FromSeconds(10)), "服務端沒有跟著結束");
    }

    [Fact]
    public async Task 重複要求啟動_只會啟動一次()
    {
        using var launcher = new FakeLauncher(NormalHost(_ => CpuTemp(57)));
        using SensorService service = Service(launcher);

        service.Start();
        service.Start();
        await WaitUntil(() => service.State == SensorState.Connected, "已連線");
        service.Start();

        Assert.Equal(1, launcher.Launches);
    }

    [Fact]
    public async Task 使用者拒絕系統管理員權限_狀態是已拒絕()
    {
        using var launcher = new FakeLauncher(launchError: new SensorLaunchDeclinedException());
        using SensorService service = Service(launcher);

        service.Start();
        await WaitUntil(() => service.State == SensorState.Declined, "已拒絕");

        Assert.Contains("同意", service.Status);
        Assert.Null(service.Latest);
    }

    [Fact]
    public async Task 啟動時發生其他錯誤_狀態是失敗並帶著原因()
    {
        using var launcher = new FakeLauncher(launchError: new InvalidOperationException("找不到程式路徑"));
        using SensorService service = Service(launcher);

        service.Start();
        await WaitUntil(() => service.State == SensorState.Failed, "失敗");

        Assert.Contains("找不到程式路徑", service.Status);
    }

    [Fact]
    public async Task 服務一直沒有出現_連線逾時()
    {
        using var launcher = new FakeLauncher(); // 啟動了，但沒有任何人建立管道
        using SensorService service = Service(launcher, connectMs: 300);

        service.Start();
        await WaitUntil(() => service.State == SensorState.Failed, "連線逾時");

        Assert.Contains("逾時", service.Status);
    }

    [Fact]
    public async Task 連上了但一直沒有數值_判定沒有回應()
    {
        using var launcher = new FakeLauncher((name, token) =>
        {
            using var server = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            server.WaitForConnectionAsync(token).GetAwaiter().GetResult();
            token.WaitHandle.WaitOne(); // 連上之後什麼都不送
        });
        using SensorService service = Service(launcher, firstMessageMs: 300);

        service.Start();
        await WaitUntil(() => service.State == SensorState.Failed, "沒有回應");

        Assert.Contains("沒有回應", service.Status);
    }

    [Fact]
    public async Task 服務中途結束_狀態變成失敗()
    {
        using var launcher = new FakeLauncher(NormalHost(_ => CpuTemp(57), stopAfterMessages: 3));
        using SensorService service = Service(launcher);

        service.Start();
        await WaitUntil(() => service.State == SensorState.Failed, "服務結束後變成失敗");

        Assert.Contains("結束", service.Status);
        Assert.Null(service.Latest);
    }

    [Fact]
    public async Task 服務的版本不同_判定失敗而不是亂讀()
    {
        using var launcher = new FakeLauncher(NormalHost(_ => new SensorMessage(99, true, null, [], [])));
        using SensorService service = Service(launcher);

        service.Start();
        await WaitUntil(() => service.State == SensorState.Failed, "版本不符");

        Assert.Contains("版本", service.Status);
    }

    [Fact]
    public async Task 服務初始化失敗_仍然連線_並把原因告訴使用者()
    {
        using var launcher = new FakeLauncher(NormalHost(_ => SensorMessage.Failure("驅動程式被安全軟體擋下")));
        using SensorService service = Service(launcher);

        service.Start();
        await WaitUntil(() => service.State == SensorState.Connected, "已連線");

        Assert.Contains("驅動程式被安全軟體擋下", service.Status);
        Assert.False(service.Latest!.HasAnything);
    }

    [Fact]
    public async Task 太久沒有新數值_不再顯示舊的()
    {
        using var launcher = new FakeLauncher((name, token) =>
        {
            using var server = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            server.WaitForConnectionAsync(token).GetAwaiter().GetResult();
            using var writer = new StreamWriter(server) { AutoFlush = true, NewLine = "\n" };
            writer.WriteLine(SensorJson.Serialize(CpuTemp(57))); // 只送一則，之後服務卡住
            token.WaitHandle.WaitOne();
        });
        using SensorService service = Service(launcher, staleMs: 300);

        service.Start();
        await WaitUntil(() => service.Latest is not null, "第一則數值");
        await WaitUntil(() => service.Latest is null, "舊數值過期");

        Assert.Equal(SensorState.Connected, service.State);
    }

    [Fact]
    public async Task 失敗之後可以重新啟動()
    {
        using var first = new FakeLauncher(); // 第一次沒有服務
        using SensorService failing = Service(first, connectMs: 200);
        failing.Start();
        await WaitUntil(() => failing.State == SensorState.Failed, "第一次失敗");

        failing.Stop();
        failing.Start();
        await WaitUntil(() => first.Launches == 2, "再次要求啟動");

        Assert.Equal(2, first.Launches);
    }

    [Fact]
    public async Task 狀態改變時會通知()
    {
        using var launcher = new FakeLauncher(NormalHost(_ => CpuTemp(57)));
        using SensorService service = Service(launcher);
        var states = new List<SensorState>();
        service.Changed += () =>
        {
            lock (states)
            {
                states.Add(service.State);
            }
        };

        service.Start();
        await WaitUntil(() => service.State == SensorState.Connected, "已連線");
        service.Stop();

        lock (states)
        {
            Assert.Equal(new[] { SensorState.Starting, SensorState.Connected, SensorState.Off }, states.Distinct().ToArray());
        }
    }

    private static SensorDiagnostics SampleDiagnostics() =>
        new("0.9.6.0", true, true, "2.2.0.0", [], [], null);

    [Fact]
    public async Task 第一則訊息帶的診斷資料_之後的訊息沒帶也會保留()
    {
        using var launcher = new FakeLauncher(NormalHost(n => n == 1 ? CpuTemp(57) with { Diagnostics = SampleDiagnostics() } : CpuTemp(57)));
        using SensorService service = Service(launcher);

        service.Start();
        await WaitUntil(() => service.HostDiagnostics is not null, "收到診斷資料");
        await Task.Delay(200); // 讓後面不帶診斷資料的訊息也進來

        Assert.Equal("0.9.6.0", service.HostDiagnostics!.LibraryVersion);
    }

    [Fact]
    public async Task 停止之後診斷資料清空()
    {
        using var launcher = new FakeLauncher(NormalHost(_ => CpuTemp(57) with { Diagnostics = SampleDiagnostics() }));
        using SensorService service = Service(launcher);
        service.Start();
        await WaitUntil(() => service.HostDiagnostics is not null, "收到診斷資料");

        service.Stop();

        Assert.Null(service.HostDiagnostics);
    }

    [Fact]
    public async Task 連線中斷後仍保留診斷資料_重新啟動時清掉_不拿舊的解釋新的()
    {
        // 只有第一次啟動會有服務端；第二次沒有任何人建立管道，狀態會停在「啟動中」。
        var normal = NormalHost(_ => CpuTemp(57) with { Diagnostics = SampleDiagnostics() }, stopAfterMessages: 2);
        int launches = 0;
        using var launcher = new FakeLauncher((pipe, token) =>
        {
            if (Interlocked.Increment(ref launches) == 1)
            {
                normal(pipe, token);
            }
        });
        using SensorService service = Service(launcher);
        service.Start();
        await WaitUntil(() => service.State == SensorState.Failed, "服務結束");

        Assert.NotNull(service.HostDiagnostics); // 失敗之後還看得到上一次的內容，方便查原因

        service.Start();
        await WaitUntil(() => service.State == SensorState.Starting, "重新啟動");

        Assert.Null(service.HostDiagnostics);
    }

    [Fact]
    public void 還沒啟動時什麼都沒有()
    {
        using var launcher = new FakeLauncher();
        using SensorService service = Service(launcher);

        Assert.Equal(SensorState.Off, service.State);
        Assert.Null(service.Latest);
        Assert.Empty(service.HardwareNames);
        Assert.Equal(0, launcher.Launches);
    }
}
