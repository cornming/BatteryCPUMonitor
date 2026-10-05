using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatteryCPUMonitor;

/// <summary>
/// 程式自動記住的狀態（橫條位置、滑鼠穿透），存在使用者自己的 AppData 資料夾。
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
