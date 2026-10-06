using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using BatteryCPUMonitor.Metrics;
using Xunit;
using Xunit.Abstractions;

namespace BatteryCPUMonitor.Tests;

/// <summary>
/// 實際把視窗建立起來並畫一次，確認畫面相關的程式碼在真的 Windows 上跑得動。
/// 只在 GitHub Actions 上執行：這些測試會動到桌面（系統匣圖示、工作列），不適合在開發者自己的電腦上跑。
/// 有設定環境變數 SCREENSHOT_DIR 時，會把實際畫面截圖存到那個資料夾，方便事後檢視外觀。
/// </summary>
public class WinFormsSmokeTests(ITestOutputHelper output)
{
    private static bool NotOnCi => Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true";

    [Fact]
    public void 浮動橫條_可以建立_排版並畫出文字()
    {
        if (NotOnCi) { return; }

        RunOnStaThread(() =>
        {
            using var form = new BarForm();
            form.Show();
            PumpMessages(TimeSpan.FromSeconds(2.5)); // 等背景讀到第一批真實數值並顯示出來

            Assert.True(form.Width > 100 && form.Height > 20, $"橫條大小 = {form.Size}");
            SaveScreenshot("floating-bar", Rectangle.Inflate(form.Bounds, 60, 40));

            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));

            // 底色很暗、文字很亮；數一數明亮的像素，一個都沒有代表沒畫出東西。
            int textPixels = 0;
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    if (pixel.A > 0 && pixel.R + pixel.G + pixel.B > 300)
                    {
                        textPixels++;
                    }
                }
            }

            output.WriteLine($"橫條大小 {form.Width}x{form.Height}，文字像素 {textPixels} 個");
            Assert.True(textPixels > 50, $"文字像素 = {textPixels}");
        });
    }

    [Fact]
    public void 工作列小工具_不論找不找得到工作列都不會當掉()
    {
        if (NotOnCi) { return; }

        RunOnStaThread(() =>
        {
            var columns = BarContent.Build(
                new MetricsSnapshot(default, 12, 61, 1024, 2048, new NetworkRate(100, 200)),
                VisibleItems.All);

            bool available = TaskbarForm.HasTaskbarOn(TaskbarForm.ResolveTarget(null));
            using var taskbar = new TaskbarForm(() => { });
            bool attached = taskbar.TryAttach(clickThrough: false);

            if (attached)
            {
                taskbar.UpdateContent(columns, clickThrough: false);
                PumpMessages(TimeSpan.FromSeconds(1));

                Rectangle screen = Screen.PrimaryScreen!.Bounds;
                SaveScreenshot("taskbar", new Rectangle(screen.Left, screen.Bottom - 120, screen.Width, 120));

                taskbar.UpdateContent(columns, clickThrough: true);
                PumpMessages(TimeSpan.FromSeconds(0.3));
            }

            output.WriteLine(
                $"找得到工作列 = {available}，掛載成功 = {attached}，仍掛著 = {taskbar.IsAttached}，" +
                $"小工具範圍 = {taskbar.Bounds}，系統版本 = {Environment.OSVersion.Version}");
        });
    }

    [Fact]
    public void 工作列小工具_指定的螢幕不存在時改掛在主螢幕上()
    {
        if (NotOnCi) { return; }

        RunOnStaThread(() =>
        {
            ScreenInfo primary = TaskbarForm.ResolveTarget(null);
            using var taskbar = new TaskbarForm(() => { });

            bool attached = taskbar.TryAttach(clickThrough: false, targetDevice: @"\\.\DISPLAY99");

            output.WriteLine(
                $"螢幕數 = {TaskbarForm.Screens().Count}，主螢幕 = {primary.DeviceName}，" +
                $"掛載成功 = {attached}，實際掛在 = {taskbar.TargetDevice}");
            Assert.True(primary.IsPrimary);
            if (attached)
            {
                Assert.Equal(primary.DeviceName, taskbar.TargetDevice);
            }
        });
    }

    [Fact]
    public void 螢幕清單_至少有一個主螢幕_且每個螢幕都有裝置名稱()
    {
        if (NotOnCi) { return; }

        IReadOnlyList<ScreenInfo> screens = TaskbarForm.Screens();

        output.WriteLine($"螢幕：{string.Join("；", screens.Select(s => $"{s.DeviceName} {s.Bounds} 主要={s.IsPrimary}"))}");
        Assert.True(screens.Count >= 1, $"螢幕數 = {screens.Count}");
        Assert.Single(screens.Where(s => s.IsPrimary));
        Assert.True(screens.All(s => !string.IsNullOrEmpty(s.DeviceName)));
    }

    /// <summary>處理視窗訊息一段時間，讓計時器、背景讀取的結果與繪製都有機會執行。</summary>
    private static void PumpMessages(TimeSpan duration)
    {
        DateTime until = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }

    private void SaveScreenshot(string name, Rectangle screenArea)
    {
        string? directory = Environment.GetEnvironmentVariable("SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        try
        {
            screenArea.Intersect(Screen.PrimaryScreen!.Bounds);
            Directory.CreateDirectory(directory);
            using var bitmap = new Bitmap(screenArea.Width, screenArea.Height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(screenArea.Location, Point.Empty, screenArea.Size);
            }

            bitmap.Save(Path.Combine(directory, name + ".png"), ImageFormat.Png);
            output.WriteLine($"已截圖 {name}：{screenArea}");
        }
        catch (Exception ex)
        {
            // 截圖只是輔助，建置主機不允許擷取畫面時不算測試失敗。
            output.WriteLine($"截圖 {name} 失敗：{ex.Message}");
        }
    }

    /// <summary>視窗程式要在單一執行緒 Apartment（STA）的執行緒上執行。</summary>
    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(60)))
        {
            Assert.Fail("畫面測試超過 60 秒沒有結束");
        }

        if (failure is not null)
        {
            Assert.Fail(failure.ToString());
        }
    }
}
