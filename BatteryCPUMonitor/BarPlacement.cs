using System.Drawing;

namespace BatteryCPUMonitor;

/// <summary>計算橫條的擺放位置。純計算，不碰畫面。</summary>
internal static class BarPlacement
{
    /// <summary>預設錨點：工作區（扣掉工作列後的範圍）底邊的正中央。</summary>
    public static Point DefaultAnchor(Rectangle workingArea) =>
        new(workingArea.Left + workingArea.Width / 2, workingArea.Bottom);

    /// <summary>由橫條目前的範圍反推錨點（底邊中點）。</summary>
    public static Point AnchorOf(Rectangle barBounds) =>
        new(barBounds.Left + barBounds.Width / 2, barBounds.Bottom);

    /// <summary>
    /// 讓橫條的底邊中點對齊錨點，並確保整條留在工作區內。
    /// 這樣橫條變大變小時是以同一個點為中心伸縮，不會跑出螢幕。
    /// </summary>
    public static Point TopLeftFor(Point anchor, Size barSize, Rectangle workingArea)
    {
        int x = anchor.X - barSize.Width / 2;
        int y = anchor.Y - barSize.Height;

        x = Math.Clamp(x, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - barSize.Width));
        y = Math.Clamp(y, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - barSize.Height));

        return new Point(x, y);
    }
}
