using LibreHardwareMonitor.Hardware;

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

            return new SensorMessage(SensorMessage.CurrentVersion, true, null, hardware, readings);
        }
        catch (Exception ex)
        {
            // 讀取過程中硬體或驅動程式出狀況時，回報原因，服務本身不結束，下一秒再試。
            return SensorMessage.Failure(ex.Message);
        }
    }

    public void Dispose() => _computer.Close();

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
