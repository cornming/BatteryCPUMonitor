using System.Drawing;
using System.Windows.Forms;
using BatteryCPUMonitor.Metrics;
using Xunit;
using Xunit.Abstractions;

namespace BatteryCPUMonitor.Tests;

/// <summary>
/// 實際把視窗建立起來並畫一次，確認畫面相關的程式碼在真的 Windows 上跑得動。
/// 只在 GitHub Actions 上執行：這些測試會動到桌面（系統匣圖示、工作列），不適合在開發者自己的電腦上跑。
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
            Application.DoEvents();

            Assert.True(form.Width > 100 && form.Height > 20, $"橫條大小 = {form.Size}");

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

            bool available = TaskbarForm.TaskbarAvailable();
            using var taskbar = new TaskbarForm(() => { });
            bool attached = taskbar.TryAttach(clickThrough: false);

            if (attached)
            {
                taskbar.UpdateContent(columns, clickThrough: false);
                Application.DoEvents();
                taskbar.UpdateContent(columns, clickThrough: true);
                Application.DoEvents();
            }

            output.WriteLine(
                $"找得到工作列 = {available}，掛載成功 = {attached}，仍掛著 = {taskbar.IsAttached}，" +
                $"小工具範圍 = {taskbar.Bounds}，系統版本 = {Environment.OSVersion.Version}");
        });
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
