using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using BatteryCPUMonitor.Sensors;
using Xunit;
using Xunit.Abstractions;

namespace BatteryCPUMonitor.Tests;

/// <summary>
/// 在真的 Windows 上啟動「感測器服務」行程（同一個 exe 加上 --sensor-host），
/// 確認它能載入 LibreHardwareMonitor、建立管道、送出數值，而且主程式一斷線就自己結束。
/// 只在 GitHub Actions 上執行。建置主機是虛擬機器，沒有溫度或風扇的感測器，也沒有安裝 PawnIO，
/// 所以這裡驗證的是「整條路通不通」，而不是「讀得到哪些數值」。
/// </summary>
public class LiveSensorHostTests(ITestOutputHelper output)
{
    private static bool NotOnCi => Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true";

    private static string? HostExecutable()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "BatteryCPUMonitor.exe");
        return File.Exists(exe) ? exe : null;
    }

    private static Process StartHost(string exe, string arguments) => Process.Start(new ProcessStartInfo(exe, arguments)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = AppContext.BaseDirectory,
    })!;

    [Fact]
    public async Task 感測器服務_真的啟動_持續送出數值_主程式斷線後自己結束()
    {
        if (NotOnCi) { return; }

        string? exe = HostExecutable();
        if (exe is null)
        {
            output.WriteLine("輸出資料夾裡沒有 BatteryCPUMonitor.exe，略過。");
            return;
        }

        string pipe = "BatteryCPUMonitor.Sensors." + Guid.NewGuid().ToString("N");
        string sid = WindowsIdentity.GetCurrent().User!.Value;
        var arguments = new SensorHostArguments(pipe, sid, Environment.ProcessId);

        using Process host = StartHost(exe, arguments.Build());
        try
        {
            var messages = new List<SensorMessage>();
            var twoMessages = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            await using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.In, PipeOptions.Asynchronous))
            {
                await client.ConnectAsync(60_000);

                using var stop = new CancellationTokenSource();
                Task reader = SensorStreamReader.ReadAsync(
                    client,
                    message =>
                    {
                        lock (messages)
                        {
                            messages.Add(message);
                            if (messages.Count >= 2)
                            {
                                twoMessages.TrySetResult();
                            }
                        }
                    },
                    stop.Token);

                Task finished = await Task.WhenAny(twoMessages.Task, host.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(120)));
                Assert.True(
                    finished == twoMessages.Task,
                    $"沒有收到兩則訊息。服務已結束 = {host.HasExited}，結束碼 = {(host.HasExited ? host.ExitCode : null)}，已收到 {messages.Count} 則");

                stop.Cancel();
                await reader;
            }

            SensorMessage first;
            lock (messages)
            {
                first = messages[0];
            }

            SensorValues values = SensorSelector.Select(first.Readings);
            output.WriteLine($"服務回報：ok={first.Ok}，error={first.Error}");
            output.WriteLine($"偵測到的硬體：{string.Join("；", first.Hardware)}");
            output.WriteLine($"感測器 {first.Readings.Count} 筆：{string.Join("，", first.Readings.Take(12).Select(r => $"{r.Hardware}/{r.Kind}/{r.Name}={r.Value:0.#}"))}");
            output.WriteLine($"挑選結果：CPU溫={values.CpuTemperature}，GPU溫={values.GpuTemperature}，CPU功耗={values.CpuPowerWatts}，風扇數={values.Fans.Count}");

            Assert.Equal(SensorMessage.CurrentVersion, first.Version);
            Assert.True(first.Ok, $"感測器服務初始化失敗：{first.Error}");

            // 用戶端離開之後，服務要自己結束，而且是正常結束。
            Assert.True(host.WaitForExit(30_000), "主程式斷線後，感測器服務沒有結束");
            Assert.Equal(0, host.ExitCode);
        }
        finally
        {
            if (!host.HasExited)
            {
                host.Kill(entireProcessTree: true);
            }
        }
    }

    [Theory]
    [InlineData("--sensor-host")]
    [InlineData("--sensor-host --pipe a --sid b")]
    [InlineData("--sensor-host --pipe a;b --sid S-1-5-18 --parent 5")]
    public void 感測器服務_參數不對就直接結束_不建立任何管道(string arguments)
    {
        if (NotOnCi) { return; }

        string? exe = HostExecutable();
        if (exe is null) { return; }

        using Process host = StartHost(exe, arguments);

        Assert.True(host.WaitForExit(30_000), "參數不對時應該立刻結束");
        Assert.Equal(2, host.ExitCode);
    }

    [Fact]
    public async Task 感測器服務_主程式不存在_自己結束()
    {
        if (NotOnCi) { return; }

        string? exe = HostExecutable();
        if (exe is null) { return; }

        // 找一個肯定不存在的行程編號：先啟動再等它結束的行程。
        using Process shortLived = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false, CreateNoWindow = true })!;
        await shortLived.WaitForExitAsync();

        string sid = WindowsIdentity.GetCurrent().User!.Value;
        var arguments = new SensorHostArguments("BatteryCPUMonitor.Sensors." + Guid.NewGuid().ToString("N"), sid, shortLived.Id);

        using Process host = StartHost(exe, arguments.Build());

        Assert.True(host.WaitForExit(60_000), "主程式已經不在了，感測器服務卻沒有結束");
        Assert.Equal(0, host.ExitCode);
    }
}
