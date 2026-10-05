namespace BatteryCPUMonitor.Metrics;

/// <summary>某一刻的所有監控數值。還沒有資料或這台電腦不支援的項目為 null。</summary>
internal readonly record struct MetricsSnapshot(
    BatterySnapshot Battery,
    double? CpuPercent,
    double? RamPercent,
    double? DiskReadBytesPerSecond,
    double? DiskWriteBytesPerSecond,
    NetworkRate? Network)
{
    public static MetricsSnapshot Empty => default;
}

/// <summary>
/// 收集所有監控數值。<see cref="Sample"/> 可以在背景執行緒呼叫，
/// 每次回傳「上一次呼叫到這一次呼叫」之間的平均值。全程不需要系統管理員權限。
/// </summary>
internal sealed class MetricsCollector : IDisposable
{
    // 「Processor Utility」比較接近工作管理員顯示的 CPU%。
    private const string CpuCounter = @"\Processor Information(_Total)\% Processor Utility";
    private const string DiskReadCounter = @"\PhysicalDisk(_Total)\Disk Read Bytes/sec";
    private const string DiskWriteCounter = @"\PhysicalDisk(_Total)\Disk Write Bytes/sec";

    private readonly object _gate = new();
    private readonly PdhQuery? _pdh;
    private readonly IntPtr? _cpu;
    private readonly IntPtr? _diskRead;
    private readonly IntPtr? _diskWrite;
    private readonly SystemTimesCpu _cpuFallback = new();
    private readonly NetworkSampler _network = new();
    private bool _disposed;

    public MetricsCollector()
    {
        _pdh = PdhQuery.TryOpen();
        if (_pdh is not null)
        {
            _cpu = _pdh.TryAdd(CpuCounter);
            _diskRead = _pdh.TryAdd(DiskReadCounter);
            _diskWrite = _pdh.TryAdd(DiskWriteCounter);
            _pdh.Collect(); // 速率要兩筆樣本相減才算得出來，先收第一筆當基準
        }
    }

    public MetricsSnapshot Sample()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return MetricsSnapshot.Empty;
            }

            // 備援也每次取樣，基準點才會持續更新，隨時可以接手。
            double? cpuFallback = _cpuFallback.Sample();

            double? cpu = null;
            double? diskRead = null;
            double? diskWrite = null;
            if (_pdh is not null && _pdh.Collect())
            {
                cpu = _pdh.Read(_cpu) is double value ? Math.Clamp(value, 0, 100) : null;
                diskRead = NonNegative(_pdh.Read(_diskRead));
                diskWrite = NonNegative(_pdh.Read(_diskWrite));
            }

            return new MetricsSnapshot(
                BatteryReader.Read(),
                cpu ?? cpuFallback,
                MemoryReader.ReadUsedPercent(),
                diskRead,
                diskWrite,
                _network.Sample());
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _pdh?.Dispose();
        }
    }

    private static double? NonNegative(double? value) => value is double v ? Math.Max(0, v) : null;
}
