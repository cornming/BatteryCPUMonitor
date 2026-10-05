using System.Drawing;

namespace BatteryCPUMonitor;

/// <summary>嵌入工作列時的位置計算。所有矩形都是螢幕座標。純計算，不碰畫面。</summary>
internal static class TaskbarPlacement
{
    /// <summary>工作列是否為橫式（在螢幕上方或下方）。直式工作列目前不支援嵌入。</summary>
    public static bool IsHorizontal(Rectangle taskbar) => taskbar.Width > 0 && taskbar.Width > taskbar.Height;

    /// <summary>
    /// Windows 11：小工具直接疊在工作列上，緊貼在系統匣（時鐘與通知圖示）的左邊，佔滿工作列的高度。
    /// </summary>
    /// <param name="taskbar">整條工作列的範圍。</param>
    /// <param name="tray">系統匣的範圍；找不到時傳入 <see cref="Rectangle.Empty"/>。</param>
    /// <param name="width">小工具的寬度。</param>
    /// <param name="gap">與系統匣之間的間距。</param>
    /// <param name="extraAvoid">系統匣左邊還要再避開的寬度（例如靠右的「小工具」天氣按鈕）。</param>
    /// <param name="fallbackTrayWidth">找不到系統匣時，假設它佔用的寬度。</param>
    /// <returns>小工具相對於工作列左上角的範圍。</returns>
    public static Rectangle Windows11(Rectangle taskbar, Rectangle tray, int width, int gap, int extraAvoid, int fallbackTrayWidth)
    {
        int trayLeft = tray.Width > 0 && tray.Left > taskbar.Left && tray.Left <= taskbar.Right
            ? tray.Left
            : taskbar.Right - fallbackTrayWidth;

        int x = trayLeft - gap - extraAvoid - width - taskbar.Left;
        return new Rectangle(Math.Max(0, x), 0, width, taskbar.Height);
    }

    /// <summary>
    /// Windows 10：小工具放在「工作清單所在的容器」最右邊，並把工作清單縮短，讓出這塊空間。
    /// </summary>
    /// <param name="container">工作清單的容器（ReBarWindow32）的範圍。</param>
    /// <param name="taskList">工作清單（執行中程式的按鈕列）目前的範圍。</param>
    /// <param name="width">小工具的寬度。</param>
    /// <param name="gap">工作清單與小工具之間的間距。</param>
    /// <returns>
    /// 兩個相對於容器左上角的範圍：小工具的位置，以及工作清單應該調整成的範圍。
    /// 空間不夠（讓出後工作清單會沒有寬度）時回傳 null。
    /// </returns>
    public static (Rectangle Widget, Rectangle TaskList)? Windows10(Rectangle container, Rectangle taskList, int width, int gap)
    {
        int widgetX = container.Width - width;
        int listLeft = taskList.Left - container.Left;
        int listTop = taskList.Top - container.Top;
        int listWidth = widgetX - gap - listLeft;

        if (widgetX < 0 || listWidth <= 0)
        {
            return null;
        }

        return (
            new Rectangle(widgetX, 0, width, container.Height),
            new Rectangle(listLeft, listTop, listWidth, taskList.Height));
    }

    /// <summary>Windows 10：離開工作列模式時，把工作清單還原成佔滿容器右側的寬度。</summary>
    public static Rectangle Windows10Restore(Rectangle container, Rectangle taskList)
    {
        int listLeft = taskList.Left - container.Left;
        int listTop = taskList.Top - container.Top;
        return new Rectangle(listLeft, listTop, Math.Max(0, container.Width - listLeft), taskList.Height);
    }

    /// <summary>
    /// 從大到小挑出第一個「兩列文字放得進工作列高度」的字體大小；都放不進去時回傳最小的那個。
    /// </summary>
    /// <param name="candidates">候選的字體大小（點），由大到小。</param>
    /// <param name="rowHeightFor">給定字體大小，回傳一列文字的高度（像素）。</param>
    /// <param name="rowGap">兩列之間的間距（像素）。</param>
    /// <param name="availableHeight">工作列可用的高度（像素）。</param>
    public static float FitFontPoints(IReadOnlyList<float> candidates, Func<float, int> rowHeightFor, int rowGap, int availableHeight)
    {
        foreach (float points in candidates)
        {
            if (rowHeightFor(points) * 2 + rowGap <= availableHeight)
            {
                return points;
            }
        }

        return candidates[^1];
    }
}
