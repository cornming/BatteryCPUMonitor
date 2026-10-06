using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatteryCPUMonitor.Sensors;

/// <summary>感測器服務回報的一筆數值。</summary>
/// <param name="Hardware">硬體類別，沿用 LibreHardwareMonitor 的名稱：Cpu、GpuNvidia、GpuAmd、GpuIntel、Motherboard、SuperIO、EmbeddedController…</param>
/// <param name="Kind">數值種類：Temperature（°C）、Power（瓦）、Clock（MHz）、Fan（RPM）。</param>
/// <param name="Name">感測器名稱，例如「CPU Package」「GPU Core」。</param>
internal sealed record SensorReading(
    [property: JsonPropertyName("h")] string Hardware,
    [property: JsonPropertyName("k")] string Kind,
    [property: JsonPropertyName("n")] string Name,
    [property: JsonPropertyName("v")] double Value);

/// <summary>感測器服務每秒送出一則的訊息（一行 JSON）。</summary>
/// <param name="Ok">服務是否順利初始化；失敗時 <paramref name="Error"/> 說明原因，服務仍會繼續回報（只是沒有數值）。</param>
/// <param name="Hardware">偵測到的硬體名稱，供診斷與顯示狀態用。</param>
internal sealed record SensorMessage(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("hw")] List<string> Hardware,
    [property: JsonPropertyName("readings")] List<SensorReading> Readings)
{
    public const int CurrentVersion = 1;

    public static SensorMessage Failure(string error) => new(CurrentVersion, false, error, [], []);
}

internal static class SensorJson
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>序列化成單行 JSON（不含換行），方便一行一則地傳送。</summary>
    public static string Serialize(SensorMessage message) => JsonSerializer.Serialize(message, Options);

    /// <summary>解析失敗（不是 JSON、欄位型別不對）回傳 null，不丟出例外。</summary>
    public static SensorMessage? TryDeserialize(string line)
    {
        try
        {
            SensorMessage? message = JsonSerializer.Deserialize<SensorMessage>(line, Options);
            return message is { Hardware: not null, Readings: not null } ? message : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>挑選出來的一個風扇。</summary>
internal readonly record struct FanReading(string Label, double Rpm);

/// <summary>從所有感測器中挑出要顯示的那幾個。沒有的項目為 null。</summary>
internal sealed record SensorValues(
    double? CpuTemperature,
    double? GpuTemperature,
    double? CpuPowerWatts,
    double? GpuPowerWatts,
    double? GpuClockMHz,
    IReadOnlyList<FanReading> Fans)
{
    public static SensorValues Empty { get; } = new(null, null, null, null, null, []);

    public bool HasAnything =>
        CpuTemperature is not null || GpuTemperature is not null || CpuPowerWatts is not null
        || GpuPowerWatts is not null || GpuClockMHz is not null || Fans.Count > 0;
}

/// <summary>
/// 決定「哪一個感測器算 CPU 溫度、CPU 功耗…」。
/// 不同廠牌、不同主機板的命名差很多，這裡依常見的命名挑選，沒有完全符合時退而求其次。純計算，不碰硬體。
/// </summary>
internal static class SensorSelector
{
    private const double MinTemperature = 1;
    private const double MaxTemperature = 150;
    private const double MaxPowerWatts = 1500;
    private const double MaxRpm = 30000;

    // 比對的優先順序：前面的先採用。
    private static readonly string[] CpuTemperatureNames = ["Package", "Tctl", "Tdie", "Core Max", "Core Average"];
    private static readonly string[] CpuPowerNames = ["Package", "CPU Cores"];
    private static readonly string[] GpuTemperatureNames = ["GPU Core", "Core"];
    private static readonly string[] GpuPowerNames = ["Package", "GPU Power", "Power", "Total"];

    public static SensorValues Select(IEnumerable<SensorReading> readings)
    {
        List<SensorReading> all = readings.Where(IsSane).ToList();

        List<SensorReading> cpu = all.Where(r => r.Hardware == "Cpu").ToList();
        List<SensorReading> gpu = PreferredGpu(all);

        return new SensorValues(
            PickByNames(cpu, "Temperature", CpuTemperatureNames, fallbackToMax: true),
            PickByNames(gpu, "Temperature", GpuTemperatureNames, fallbackToMax: false),
            PickByNames(cpu, "Power", CpuPowerNames, fallbackToMax: false),
            PickByNames(gpu, "Power", GpuPowerNames, fallbackToMax: false),
            PickByNames(gpu, "Clock", ["GPU Core", "Core"], fallbackToMax: false),
            PickFans(all));
    }

    /// <summary>
    /// 有多張顯示卡時（例如內顯加獨顯）只看一張：優先獨立顯卡（NVIDIA、AMD），內顯（Intel）最後。
    /// 同類型有多張時取名字排序最前面的，結果才不會每秒跳動。
    /// </summary>
    private static List<SensorReading> PreferredGpu(List<SensorReading> all)
    {
        foreach (string type in new[] { "GpuNvidia", "GpuAmd", "GpuIntel" })
        {
            List<SensorReading> ofType = all.Where(r => r.Hardware == type).ToList();
            if (ofType.Count > 0)
            {
                return ofType;
            }
        }

        return [];
    }

    private static double? PickByNames(List<SensorReading> candidates, string kind, string[] names, bool fallbackToMax)
    {
        List<SensorReading> ofKind = candidates.Where(r => r.Kind == kind).ToList();
        foreach (string name in names)
        {
            SensorReading? match = ofKind.FirstOrDefault(r => r.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match.Value;
            }
        }

        return fallbackToMax && ofKind.Count > 0 ? ofKind.Max(r => r.Value) : null;
    }

    /// <summary>
    /// 風扇：名字帶 CPU 的排最前面，其餘依轉速由高到低。轉速為 0 的通常是沒接風扇的插座，不列入。
    /// </summary>
    private static List<FanReading> PickFans(List<SensorReading> all)
    {
        var fans = all
            .Where(r => r.Kind == "Fan" && r.Value > 0)
            .OrderByDescending(r => r.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(r => r.Value)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();

        var result = new List<FanReading>(fans.Count);
        for (int i = 0; i < fans.Count; i++)
        {
            result.Add(new FanReading(FanLabel(fans[i].Name, i + 1), fans[i].Value));
        }

        return result;
    }

    /// <summary>橫條上的風扇標籤：「CPU Fan」→「CPU風扇」、「GPU Fan」→「GPU風扇」，其他一律「風扇＋序號」。</summary>
    public static string FanLabel(string sensorName, int position)
    {
        if (sensorName.Contains("CPU", StringComparison.OrdinalIgnoreCase))
        {
            return "CPU風扇";
        }

        return sensorName.Contains("GPU", StringComparison.OrdinalIgnoreCase) ? "GPU風扇" : $"風扇{position}";
    }

    /// <summary>過濾掉明顯不合理的數值（感測器沒接好時常回報 0、255 或極大的數字）。</summary>
    private static bool IsSane(SensorReading reading)
    {
        if (!double.IsFinite(reading.Value))
        {
            return false;
        }

        return reading.Kind switch
        {
            "Temperature" => reading.Value is >= MinTemperature and <= MaxTemperature,
            "Power" => reading.Value is >= 0 and <= MaxPowerWatts,
            "Clock" => reading.Value is > 0 and <= 20000,
            "Fan" => reading.Value is >= 0 and <= MaxRpm,
            _ => false,
        };
    }
}

/// <summary>把感測器數值轉成橫條上的文字。純計算。</summary>
internal static class SensorFormat
{
    /// <summary>例如 62.4 → 「62°C」。</summary>
    public static string Temperature(double? celsius) =>
        celsius is double value && double.IsFinite(value)
            ? ((int)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "°C"
            : "--";

    /// <summary>未滿 100 瓦顯示一位小數，例如 45.24 → 「45.2W」；100 瓦以上顯示整數。</summary>
    public static string Power(double? watts)
    {
        if (watts is not double value || !double.IsFinite(value) || value < 0)
        {
            return "--";
        }

        string number = value >= 99.95
            ? Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        return number + "W";
    }

    /// <summary>1000 MHz 以上用 GHz（3412 → 「3.4GHz」），以下用 MHz（800 → 「800MHz」）。</summary>
    public static string Clock(double? megahertz)
    {
        if (megahertz is not double value || !double.IsFinite(value) || value <= 0)
        {
            return "--";
        }

        return value >= 999.5
            ? (value / 1000).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "GHz"
            : Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "MHz";
    }

    public static string Fan(double? rpm) =>
        rpm is double value && double.IsFinite(value) && value >= 0
            ? Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "rpm"
            : "--";
}
