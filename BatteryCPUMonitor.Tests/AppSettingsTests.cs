using System.Drawing;
using Xunit;

namespace BatteryCPUMonitor.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BatteryCPUMonitor.Tests." + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "nested", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 檔案不存在_回傳預設值()
    {
        AppSettings settings = AppSettings.Load(SettingsPath);

        Assert.Null(settings.Anchor);
        Assert.False(settings.ClickThrough);
    }

    [Fact]
    public void 存檔後再讀取_內容一致_資料夾會自動建立()
    {
        var saved = new AppSettings { Anchor = new Point(-1200, 1040), ClickThrough = true };

        Assert.True(saved.Save(SettingsPath));
        AppSettings loaded = AppSettings.Load(SettingsPath);

        Assert.Equal(new Point(-1200, 1040), loaded.Anchor);
        Assert.True(loaded.ClickThrough);
    }

    [Fact]
    public void 重設位置後_錨點清空()
    {
        var settings = new AppSettings { Anchor = new Point(10, 20) };
        settings.Anchor = null;
        settings.Save(SettingsPath);

        Assert.Null(AppSettings.Load(SettingsPath).Anchor);
    }

    [Fact]
    public void 檔案內容損毀_回傳預設值而不是當掉()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ 這不是合法的 JSON");

        AppSettings settings = AppSettings.Load(SettingsPath);

        Assert.Null(settings.Anchor);
        Assert.False(settings.ClickThrough);
    }

    [Fact]
    public void 顯示項目預設全部開啟()
    {
        Assert.Equal(VisibleItems.All, AppSettings.Load(SettingsPath).Visible);
    }

    [Fact]
    public void 舊版設定檔沒有顯示項目欄位_讀取後維持全部開啟()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ \"anchorX\": 100, \"anchorY\": 200, \"clickThrough\": true }");

        AppSettings settings = AppSettings.Load(SettingsPath);

        Assert.Equal(new Point(100, 200), settings.Anchor);
        Assert.Equal(VisibleItems.All, settings.Visible);
    }

    [Fact]
    public void 關掉的顯示項目_存檔後再讀取仍是關的()
    {
        new AppSettings { ShowDisk = false, ShowBattery = false, ShowGpu = false }.Save(SettingsPath);

        Assert.Equal(
            new VisibleItems(Battery: false, BatteryDetail: true, CpuRam: true, Gpu: false, Disk: false, Network: true),
            AppSettings.Load(SettingsPath).Visible);
    }

    [Fact]
    public void 只有一個座標_不視為有效位置()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ \"anchorX\": 100 }");

        Assert.Null(AppSettings.Load(SettingsPath).Anchor);
    }
}
