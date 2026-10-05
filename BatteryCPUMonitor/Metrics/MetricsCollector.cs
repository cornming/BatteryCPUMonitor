namespace BatteryCPUMonitor.Metrics;

/// <summary>某一刻的所有監控數值。還沒有資料或這台電腦不支援的項目為 null。</summary>
internal readonly record struct MetricsSnapshot(
    BatterySnapshot Battery,
    double? CpuPercent,
    double? RamPercent,
    double? DiskReadBytesPerSecond,
    double? DiskWriteBytesPerSecond,
    NetworkRate? Network,
    double? GpuPercent = null,
    double? GpuMemoryBytes = null,
    BatteryDetail? BatteryDetail = null)
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

    // 與工作管理員「效能 → GPU」相同的資料來源。每個程式在每個 GPU 引擎上各有一筆，所以用萬用字元。
    private const string GpuEngineCounter = @"\GPU Engine(*)\Utilization Percentage";
    private const string GpuMemoryCounter = @"\GPU Adapter Memory(*)\Dedicated Usage";

    private readonly object _gate = new();
    private readonly PdhQuery? _pdh;
    private readonly IntPtr? _cpu;
    private readonly IntPtr? _diskRead;
    private readonly IntPtr? _diskWrite;
    private readonly SystemTimesCpu _cpuFallback = new();
    private readonly NetworkSampler _network = new();

    // GPU 計數器的資料量大、收集成本較高，放在獨立的查詢裡，沒有要顯示 GPU 時就不收集。
    private readonly PdhQuery? _gpuPdh;
    private readonly IntPtr? _gpuEngine;
    private readonly IntPtr? _gpuMemory;
    private bool _gpuPrimed;

    private readonly BatteryDetailReader _batteryDetail = new();
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

        _gpuPdh = PdhQuery.TryOpen();
        if (_gpuPdh is not null)
        {
            _gpuEngine = _gpuPdh.TryAdd(GpuEngineCounter);
            _gpuMemory = _gpuPdh.TryAdd(GpuMemoryCounter);
        }
    }

    /// <summary>這台電腦有沒有提供 GPU 計數器（需要 Windows 10 1709 以上與支援的顯示驅動程式）。</summary>
    public bool HasGpuCounters => _gpuEngine is not null || _gpuMemory is not null;

    /// <param name="visible">目前要顯示的項目；沒有要顯示的項目會略過較花時間的讀取。</param>
    public MetricsSnapshot Sample(VisibleItems visible)
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

            double? gpu = null;
            double? gpuMemory = null;
            if (visible.Gpu && HasGpuCounters && _gpuPdh!.Collect())
            {
                // 使用率是速率類數值，剛開始收集的第一筆沒有意義，從第二筆起才採用。
                if (_gpuPrimed)
                {
                    gpu = GpuMath.Utilization(_gpuPdh.ReadArray(_gpuEngine));
                }

                gpuMemory = GpuMath.DedicatedMemoryBytes(_gpuPdh.ReadArray(_gpuMemory));
                _gpuPrimed = true;
            }
            else
            {
                _gpuPrimed = false;
            }

            BatterySnapshot battery = BatteryReader.Read();
            BatteryDetail? batteryDetail = battery.HasBattery && (visible.Battery || visible.BatteryDetail)
                ? _batteryDetail.Read(battery.IsPluggedIn, battery.IsCharging)
                : null;

            return new MetricsSnapshot(
                battery,
                cpu ?? cpuFallback,
                MemoryReader.ReadUsedPercent(),
                diskRead,
                diskWrite,
                _network.Sample(),
                gpu,
                gpuMemory,
                batteryDetail);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _pdh?.Dispose();
            _gpuPdh?.Dispose();
            _batteryDetail.Dispose();
        }
    }

    private static double? NonNegative(double? value) => value is double v ? Math.Max(0, v) : null;
}
