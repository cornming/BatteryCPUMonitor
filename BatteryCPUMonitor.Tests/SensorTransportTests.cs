using System.IO.Pipes;
using System.Text;
using BatteryCPUMonitor.Sensors;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class SensorTransportTests
{
    internal sealed class FakeSource(Func<int, SensorMessage> next) : ISensorSource
    {
        private int _calls;

        public int Calls => _calls;

        public SensorMessage Read() => next(Interlocked.Increment(ref _calls));

        public void Dispose()
        {
        }
    }

    internal static SensorMessage Message(params SensorReading[] readings) =>
        new(SensorMessage.CurrentVersion, true, null, ["Cpu: 測試處理器"], [.. readings]);

    // ---- JSON ----

    [Fact]
    public void 訊息序列化後再解析_內容一致()
    {
        SensorMessage original = new(1, true, null, ["Cpu: X", "GpuNvidia: Y"],
        [
            new SensorReading("Cpu", "Temperature", "CPU Package", 61.5),
            new SensorReading("SuperIO", "Fan", "Fan #1", 1200),
        ]);

        SensorMessage? parsed = SensorJson.TryDeserialize(SensorJson.Serialize(original));

        Assert.NotNull(parsed);
        Assert.Equal(original.Version, parsed.Version);
        Assert.True(parsed.Ok);
        Assert.Equal(original.Hardware, parsed.Hardware);
        Assert.Equal(original.Readings, parsed.Readings);
    }

    [Fact]
    public void 序列化結果是單行_不含換行()
    {
        string json = SensorJson.Serialize(Message(new SensorReading("Cpu", "Temperature", "含換行\n的名稱", 50)));

        Assert.DoesNotContain("\n", json);
        Assert.DoesNotContain("\r", json);
    }

    [Fact]
    public void 失敗訊息帶著原因()
    {
        SensorMessage? parsed = SensorJson.TryDeserialize(SensorJson.Serialize(SensorMessage.Failure("驅動程式被擋下")));

        Assert.NotNull(parsed);
        Assert.False(parsed.Ok);
        Assert.Equal("驅動程式被擋下", parsed.Error);
        Assert.Empty(parsed.Readings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{ \"v\": \"x\" }")]
    [InlineData("{ \"v\": 1, \"ok\": true, \"readings\": \"壞掉\" }")]
    public void 看不懂的內容回傳空值而不是丟出例外(string line)
    {
        Assert.Null(SensorJson.TryDeserialize(line));
    }

    [Fact]
    public void 多出來的欄位會被忽略_日後新增欄位不影響舊版()
    {
        SensorMessage? parsed = SensorJson.TryDeserialize("{\"v\":1,\"ok\":true,\"error\":null,\"hw\":[],\"readings\":[],\"future\":123}");

        Assert.NotNull(parsed);
    }

    // ---- 傳送與接收 ----

    [Fact]
    public async Task 服務端每次讀一則就送出一行_直到被取消()
    {
        using var stream = new MemoryStream();
        using var cancel = new CancellationTokenSource();

        // 以「讀了幾次」而不是「過了多久」來決定何時停：第四次讀取時要求取消，所以前三則會送出、第四則不會。
        using var source = new FakeSource(n =>
        {
            if (n == 4)
            {
                cancel.Cancel();
            }

            return Message(new SensorReading("Cpu", "Temperature", "CPU Package", 50 + n));
        });

        await SensorHostLoop.RunAsync(stream, source, TimeSpan.FromMilliseconds(10), cancel.Token);

        string[] lines = Encoding.UTF8.GetString(stream.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.True(lines.All(l => SensorJson.TryDeserialize(l) is not null));
        Assert.Equal(51.0, SensorJson.TryDeserialize(lines[0])!.Readings[0].Value);
        Assert.Equal(52.0, SensorJson.TryDeserialize(lines[1])!.Readings[0].Value);
        Assert.Equal(53.0, SensorJson.TryDeserialize(lines[2])!.Readings[0].Value);
    }

    [Fact]
    public async Task 對方斷線時服務端自己結束()
    {
        using var source = new FakeSource(_ => Message());
        using var stream = new ThrowingStream();

        await SensorHostLoop.RunAsync(stream, source, TimeSpan.FromMilliseconds(10), CancellationToken.None);

        Assert.Equal(1, source.Calls); // 寫一次就失敗，不會一直讀下去
    }

    [Fact]
    public async Task 用戶端逐行讀取_略過看不懂的行()
    {
        string content = SensorJson.Serialize(Message(new SensorReading("Cpu", "Temperature", "A", 1))) + "\n"
            + "壞掉的一行\n"
            + "\n"
            + SensorJson.Serialize(Message(new SensorReading("Cpu", "Temperature", "A", 2))) + "\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var received = new List<double>();

        await SensorStreamReader.ReadAsync(stream, m => received.Add(m.Readings[0].Value), CancellationToken.None);

        Assert.Equal(new[] { 1.0, 2.0 }, received);
    }

    [Fact]
    public async Task 用戶端讀到串流結束就返回()
    {
        using var stream = new MemoryStream();

        await SensorStreamReader.ReadAsync(stream, _ => { }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task 透過真正的具名管道_服務端送出_用戶端收到()
    {
        string name = "BatteryCPUMonitor.Tests." + Guid.NewGuid().ToString("N");
        using var source = new FakeSource(n => Message(new SensorReading("Cpu", "Temperature", "CPU Package", 40 + n)));
        using var stop = new CancellationTokenSource();

        await using var server = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task serving = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(stop.Token);
            await SensorHostLoop.RunAsync(server, source, TimeSpan.FromMilliseconds(20), stop.Token);
        });

        await using var client = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.Asynchronous);
        await client.ConnectAsync(10_000);

        var got = new TaskCompletionSource<SensorMessage>();
        _ = SensorStreamReader.ReadAsync(client, m => got.TrySetResult(m), stop.Token);
        SensorMessage first = await got.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(41.0, first.Readings[0].Value);

        // 用戶端離開之後，服務端應該自己結束。
        await client.DisposeAsync();
        await serving.WaitAsync(TimeSpan.FromSeconds(10));
        stop.Cancel();
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new IOException("管道已中斷");
        public override Task FlushAsync(CancellationToken cancellationToken) => throw new IOException("管道已中斷");
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("管道已中斷");
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException("管道已中斷");
    }
}

public class SensorHostArgumentsTests
{
    private const string Sid = "S-1-5-21-1111111111-2222222222-3333333333-1001";

    [Fact]
    public void 組出來的命令列可以原樣解析回來()
    {
        var original = new SensorHostArguments("BatteryCPUMonitor.Sensors.abc123", Sid, 4242);

        SensorHostArguments? parsed = SensorHostArguments.TryParse(original.Build().Split(' '));

        Assert.Equal(original, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--other")]
    [InlineData("--sensor-host")]
    [InlineData("--sensor-host --pipe a --sid b")]
    [InlineData("--sensor-host --pipe a --sid b --parent notanumber")]
    [InlineData("--sensor-host --pipe a --sid b --parent 0")]
    [InlineData("--sensor-host --pipe a --sid b --parent -5")]
    [InlineData("--sensor-host --pipe --sid b --parent 5")]
    public void 參數不齊全或不是感測器服務_不解析(string commandLine)
    {
        string[] args = commandLine.Length == 0 ? [] : commandLine.Split(' ');

        Assert.Null(SensorHostArguments.TryParse(args));
    }

    [Theory]
    [InlineData(@"..\..\evil")]
    [InlineData(@"\\.\pipe\other")]
    [InlineData("a b")]
    [InlineData("a;b")]
    [InlineData("a&b")]
    [InlineData("a\"b")]
    public void 管道名稱含有可疑字元就拒絕(string pipeName)
    {
        Assert.Null(SensorHostArguments.TryParse(["--sensor-host", "--pipe", pipeName, "--sid", Sid, "--parent", "5"]));
    }

    [Fact]
    public void SID含有可疑字元就拒絕()
    {
        Assert.Null(SensorHostArguments.TryParse(["--sensor-host", "--pipe", "p", "--sid", "S-1-5;calc", "--parent", "5"]));
    }

    [Fact]
    public void 太長的值就拒絕()
    {
        Assert.Null(SensorHostArguments.TryParse(["--sensor-host", "--pipe", new string('a', 201), "--sid", Sid, "--parent", "5"]));
    }
}

public class PawnIoDetectorTests
{
    [Fact]
    public void 登錄檔指出的資料夾裡有函式庫_算已安裝()
    {
        string folder = Path.Combine("D:", "Tools", "PawnIO");

        bool installed = PawnIoDetector.IsInstalled(folder, Path.Combine("C:", "Program Files"), path => path == Path.Combine(folder, "PawnIOLib.dll"));

        Assert.True(installed);
    }

    [Fact]
    public void 登錄檔沒有紀錄時_退回ProgramFiles底下的預設位置()
    {
        string programFiles = Path.Combine("C:", "Program Files");

        bool installed = PawnIoDetector.IsInstalled(null, programFiles, path => path == Path.Combine(programFiles, "PawnIO", "PawnIOLib.dll"));

        Assert.True(installed);
    }

    [Fact]
    public void 兩個位置都沒有函式庫_算未安裝()
    {
        Assert.False(PawnIoDetector.IsInstalled(Path.Combine("D:", "Tools"), Path.Combine("C:", "Program Files"), _ => false));
        Assert.False(PawnIoDetector.IsInstalled(null, null, _ => true));
        Assert.False(PawnIoDetector.IsInstalled("", "", _ => true));
    }

    [Fact]
    public void 官方安裝指令與網站()
    {
        Assert.Equal("winget install --id namazso.PawnIO -e", PawnIoDetector.WingetCommand);
        Assert.Equal("https://pawnio.eu/", PawnIoDetector.OfficialSite);
    }
}
