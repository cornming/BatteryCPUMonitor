using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatteryCPUMonitor;

/// <summary>
/// 程式自動記住的狀態（橫條位置、滑鼠穿透、顯示模式與項目），存在使用者自己的 AppData 資料夾。
/// 使用者不需要也不必手動編輯這個檔案。
/// </summary>
internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public int? AnchorX { get; set; }

    public int? AnchorY { get; set; }

    public bool ClickThrough { get; set; }

    /// <summary>嵌入工作列（true）或浮動橫條（false）。</summary>
    public bool TaskbarMode { get; set; }

    /// <summary>自動檢查更新，有新版本時自動下載並安裝（預設開啟）。</summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>
    /// 嵌入哪個螢幕的工作列（螢幕的裝置名稱）；null 表示主螢幕。
    /// 指定的螢幕目前沒接上時暫時改用主螢幕，這個設定不會被清掉。
    /// </summary>
    public string? TaskbarMonitor { get; set; }

    // 要顯示的項目，預設全部顯示。舊版的設定檔沒有這些欄位，讀取後維持預設值。
    public bool ShowBattery { get; set; } = true;

    public bool ShowBatteryDetail { get; set; } = true;

    public bool ShowCpuRam { get; set; } = true;

    public bool ShowGpu { get; set; } = true;

    public bool ShowDisk { get; set; } = true;

    public bool ShowNetwork { get; set; } = true;

    // 後來加入的項目預設關閉：自動更新會悄悄換成新版本，不該讓橫條自己多出幾欄。
    public bool ShowTemperature { get; set; }

    public bool ShowClock { get; set; }

    public bool ShowPower { get; set; }

    public bool ShowFans { get; set; }

    /// <summary>啟用硬體感測器（CPU、GPU 溫度、功耗、風扇）。需要系統管理員權限，每次啟動程式都要重新同意。</summary>
    public bool SensorsEnabled { get; set; }

    [JsonIgnore]
    public VisibleItems Visible => new(
        ShowBattery, ShowBatteryDetail, ShowCpuRam, ShowGpu, ShowDisk, ShowNetwork,
        ShowTemperature, ShowClock, ShowPower, ShowFans);

    /// <summary>使用者拖曳後的錨點（橫條底邊中點）；沒拖過為 null。</summary>
    [JsonIgnore]
    public Point? Anchor
    {
        get => AnchorX is int x && AnchorY is int y ? new Point(x, y) : null;
        set => (AnchorX, AnchorY) = (value?.X, value?.Y);
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BatteryCPUMonitor",
        "settings.json");

    /// <summary>讀取設定；檔案不存在或內容損毀時回傳預設值。</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return new AppSettings();
        }
    }

    /// <summary>寫入設定；失敗時回傳 false，不影響程式繼續執行。</summary>
    public bool Save(string path)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
            return true;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return false;
        }
    }

    private static bool IsExpected(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException;
}
