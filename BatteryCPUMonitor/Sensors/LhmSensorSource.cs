using System.Reflection;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Cpu;

namespace BatteryCPUMonitor.Sensors;

/// <summary>
/// 以 LibreHardwareMonitor 讀取 CPU、GPU 與主機板的感測器。只在特權的感測器服務行程裡使用。
/// 這個檔案是整個專案裡唯一用到 LibreHardwareMonitor 的地方。
/// </summary>
/// <remarks>
/// 只啟用 CPU、GPU、主機板（含 Super I/O 與嵌入式控制器上的風扇）。
/// 磁碟（SMART）每秒查詢一次會讓硬碟無法休眠，記憶體、網路、電池程式本身已經有更輕量的讀法，所以都不啟用。
/// </remarks>
internal sealed class LhmSensorSource : ISensorSource
{
    private readonly Computer _computer;
    private bool _diagnosticsSent;

    public LhmSensorSource()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
        };

        _computer.Open();
    }

    public SensorMessage Read()
    {
        try
        {
            var readings = new List<SensorReading>();
            var hardware = new List<string>();

            foreach (IHardware item in _computer.Hardware)
            {
                Collect(item, readings, hardware);
            }

            // 診斷資料只在第一則帶上：它不會變，而且函式庫報告很長，不需要每秒送。
            SensorDiagnostics? diagnostics = null;
            if (!_diagnosticsSent)
            {
                _diagnosticsSent = true;
                diagnostics = TryCollectDiagnostics();
            }

            return new SensorMessage(SensorMessage.CurrentVersion, true, null, hardware, readings, diagnostics);
        }
        catch (Exception ex)
        {
            // 讀取過程中硬體或驅動程式出狀況時，回報原因，服務本身不結束，下一秒再試。
            return SensorMessage.Failure(ex.Message);
        }
    }

    public void Dispose() => _computer.Close();

    /// <summary>診斷只是附帶的：蒐集時出任何問題都改成回報例外內容，不能讓它害得數值也送不出去。</summary>
    private SensorDiagnostics TryCollectDiagnostics()
    {
        try
        {
            return CollectDiagnostics();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return SensorDiagnostics.ForInitFailure(SensorEnvironment.IsElevated(), ex.ToString());
        }
    }

    /// <summary>
    /// 蒐集「CPU 溫度為什麼沒出來」需要的資料。只看 CPU：函式庫完整報告（含主機板）會有序號等識別資訊，不適合讓人複製貼上。
    /// </summary>
    private SensorDiagnostics CollectDiagnostics()
    {
        var cpus = new List<CpuDiagnostics>();
        var sensors = new List<SensorDump>();

        foreach (IHardware hardware in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Cpu))
        {
            cpus.Add(DescribeCpu(hardware));
            foreach (ISensor sensor in hardware.Sensors)
            {
                double? value = sensor.Value is float number && float.IsFinite(number) ? number : null;
                sensors.Add(new SensorDump(hardware.HardwareType.ToString(), sensor.SensorType.ToString(), sensor.Name, value));
            }
        }

        return new SensorDiagnostics(
            typeof(Computer).Assembly.GetName().Version?.ToString() ?? "?",
            SensorEnvironment.IsElevated(),
            LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled,
            LibreHardwareMonitor.PawnIo.PawnIo.Version?.ToString(),
            cpus,
            sensors,
            Detail: null);
    }

    private static CpuDiagnostics DescribeCpu(IHardware hardware)
    {
        CpuId? id = (hardware as GenericCpu)?.CpuId.FirstOrDefault()?.FirstOrDefault();

        return new CpuDiagnostics(
            hardware.Name,
            id?.Vendor.ToString(),
            id?.Family ?? 0,
            id?.Model ?? 0,
            id?.Stepping ?? 0,
            hardware.GetType().Name,
            ReadPrivate(hardware, "_microArchitecture")?.ToString(),
            IsDriverModuleLoaded(hardware),
            ReadReport(hardware));
    }

    private static string? ReadReport(IHardware hardware)
    {
        try
        {
            return hardware.GetReport();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return $"（函式庫報告產生失敗：{ex.Message}）";
        }
    }

    /// <summary>
    /// 這顆 CPU 的 PawnIO 模組有沒有載入成功。函式庫沒有公開這項資訊，只能讀它的私有欄位：
    /// CPU 物件的 _pawnModule 裡面存著 PawnIo 控制代碼。函式庫改了欄位名稱就會讀不到，這時回傳 null（未知），判斷改看 PawnIO 服務狀態。
    /// </summary>
    private static bool? IsDriverModuleLoaded(IHardware hardware)
    {
        object? module = ReadPrivate(hardware, "_pawnModule");
        if (module is null)
        {
            return null;
        }

        foreach (FieldInfo field in module.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (field.FieldType == typeof(LibreHardwareMonitor.PawnIo.PawnIo) && ReadField(field, module) is LibreHardwareMonitor.PawnIo.PawnIo handle)
            {
                return handle.IsLoaded;
            }
        }

        return null;
    }

    private static object? ReadPrivate(object target, string fieldName) =>
        target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance) is FieldInfo field
            ? ReadField(field, target)
            : null;

    private static object? ReadField(FieldInfo field, object target)
    {
        try
        {
            return field.GetValue(target);
        }
        catch (Exception ex) when (ex is MemberAccessException or TargetException or NotSupportedException)
        {
            return null; // 內部結構變了，只是看不到這一項
        }
    }

    private static void Collect(IHardware hardware, List<SensorReading> readings, List<string> names)
    {
        hardware.Update();
        names.Add($"{hardware.HardwareType}: {hardware.Name}");

        foreach (ISensor sensor in hardware.Sensors)
        {
            if (sensor.Value is not float value || float.IsNaN(value))
            {
                continue;
            }

            string? kind = sensor.SensorType switch
            {
                SensorType.Temperature => "Temperature",
                SensorType.Power => "Power",
                SensorType.Clock => "Clock",
                SensorType.Fan => "Fan",
                _ => null,
            };

            if (kind is not null)
            {
                readings.Add(new SensorReading(hardware.HardwareType.ToString(), kind, sensor.Name, value));
            }
        }

        foreach (IHardware sub in hardware.SubHardware)
        {
            Collect(sub, readings, names);
        }
    }
}
