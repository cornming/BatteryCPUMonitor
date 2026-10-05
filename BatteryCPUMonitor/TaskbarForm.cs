using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BatteryCPUMonitor;

/// <summary>
/// 嵌進 Windows 工作列的小工具：把同一份兩列格狀內容直接畫在工作列上、系統匣的左邊。
/// </summary>
/// <remarks>
/// 做法參考 Diorser/LiteMonitor 與 TrafficMonitor：把自己的視窗設成工作列的子視窗。
/// 這不需要系統管理員權限，但屬於 Windows 沒有正式公開的用法，Windows 10 與 11 的工作列結構也不同：
/// Windows 11 直接疊在工作列上；Windows 10 要把「工作清單」縮短來讓出空間，離開時再還原。
/// 背景以「色鍵」挖成透明：把背景塗成一個接近工作列底色的顏色，並告訴 Windows 這個顏色不要畫出來。
/// </remarks>
internal sealed class TaskbarForm : Form
{
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const uint GA_PARENT = 1;
    private const uint LWA_COLORKEY = 0x1;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private const int Windows11FirstBuild = 22000;

    private const TextFormatFlags TextFlags =
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

    private static readonly Size Unbounded = new(int.MaxValue, int.MaxValue);
    private static readonly float[] FontCandidates = [9f, 8.5f, 8f, 7.5f, 7f];

    // 色鍵要接近工作列的底色，文字邊緣的半透明像素才不會出現明顯的色邊。
    private static readonly Color DarkKey = Color.FromArgb(40, 40, 41);
    private static readonly Color LightKey = Color.FromArgb(210, 210, 211);

    private static readonly Palette DarkPalette = new(
        Label: Color.FromArgb(255, 255, 255),
        Neutral: Color.FromArgb(190, 190, 190),
        Good: Color.FromArgb(102, 255, 153),
        Warn: Color.FromArgb(255, 214, 102),
        Critical: Color.FromArgb(255, 105, 97));

    private static readonly Palette LightPalette = new(
        Label: Color.FromArgb(20, 20, 20),
        Neutral: Color.FromArgb(95, 95, 95),
        Good: Color.FromArgb(0, 128, 64),
        Warn: Color.FromArgb(176, 112, 0),
        Critical: Color.FromArgb(196, 43, 28));

    private readonly Action _showMenu;
    private readonly bool _isWindows11 = Environment.OSVersion.Version.Build >= Windows11FirstBuild;

    private IntPtr _taskbar;
    private IntPtr _container; // 實際的父視窗：Windows 11 為工作列本身，Windows 10 為 ReBarWindow32
    private IntPtr _taskList;  // 只有 Windows 10 會用到
    private bool _taskListSqueezed;

    private Font? _font;
    private float _fontPoints;
    private int _fontDpi;
    private int _fontFitHeight;

    private GridResult _layout = new(Size.Empty, []);
    private bool? _lightTheme;
    private Palette _palette = DarkPalette;
    private bool _clickThrough;
    private bool? _widgetsInstalled;

    /// <param name="showMenu">使用者在小工具上按右鍵時呼叫。</param>
    public TaskbarForm(Action showMenu)
    {
        _showMenu = showMenu;

        Text = "BatteryCPUMonitor";
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        BackColor = DarkKey;
        Bounds = new Rectangle(-32000, -32000, 1, 1); // 掛上工作列之前先放在畫面外
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);
    }

    /// <summary>目前找不找得到工作列（Explorer 正在重新啟動時會暫時找不到）。</summary>
    public static bool TaskbarAvailable() => FindWindowW("Shell_TrayWnd", null) != IntPtr.Zero;

    /// <summary>小工具是否還好好地掛在工作列上。Explorer 重新啟動後會變成 false，需要重新建立。</summary>
    public bool IsAttached =>
        !IsDisposed
        && IsHandleCreated
        && _container != IntPtr.Zero
        && IsWindow(_container)
        && GetAncestor(Handle, GA_PARENT) == _container;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            // 分層視窗才能設定色鍵；NOACTIVATE 讓點擊小工具時不會搶走目前視窗的焦點。
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    /// <summary>把小工具掛到工作列上。找不到工作列，或工作列是直式的，回傳 false。</summary>
    public bool TryAttach(bool clickThrough)
    {
        _taskbar = FindWindowW("Shell_TrayWnd", null);
        if (_taskbar == IntPtr.Zero)
        {
            return false;
        }

        IntPtr container = _taskbar;
        if (!_isWindows11)
        {
            // Windows 10 的結構：Shell_TrayWnd → ReBarWindow32 → MSTaskSwWClass（工作清單）
            container = FindWindowExW(_taskbar, IntPtr.Zero, "ReBarWindow32", null);
            _taskList = container == IntPtr.Zero ? IntPtr.Zero : FindWindowExW(container, IntPtr.Zero, "MSTaskSwWClass", null);
            if (container == IntPtr.Zero || _taskList == IntPtr.Zero)
            {
                return false;
            }
        }

        if (!TryGetRect(container, out Rectangle containerRect) || !TaskbarPlacement.IsHorizontal(containerRect))
        {
            return false;
        }

        // 先以一般視窗的身分在畫面外顯示（不搶焦點），再改掛到工作列底下。
        Show();
        IntPtr handle = Handle;
        ApplyTheme();
        _clickThrough = !clickThrough; // 強制下面這一行套用一次
        SetClickThrough(clickThrough);

        // 先改成子視窗樣式，再指定父視窗（Windows 文件建議的順序）。
        int style = GetWindowLongW(handle, GWL_STYLE);
        SetWindowLongW(handle, GWL_STYLE, (style & ~WS_POPUP) | WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS);
        SetParent(handle, container);

        if (GetAncestor(handle, GA_PARENT) != container)
        {
            return false;
        }

        _container = container;
        return true;
    }

    /// <summary>更新內容並重新定位。工作列的配置隨時會變（通知圖示增減等），所以每次都重算位置。</summary>
    public void UpdateContent(IReadOnlyList<BarColumn> columns, bool clickThrough)
    {
        if (!IsAttached || !TryGetRect(_container, out Rectangle container))
        {
            return;
        }

        ApplyTheme();
        SetClickThrough(clickThrough);

        int dpi = GetTaskbarDpi();
        EnsureFont(dpi, container.Height);

        GridSpacing spacing = GridSpacing.ForDpi(dpi) with { PaddingY = 0, PaddingX = Scale(4, dpi) };
        _layout = GridLayout.Compute(columns, MeasureWidth, RowHeight(_font!), spacing);

        Rectangle target;
        if (_isWindows11)
        {
            TryGetRect(FindWindowExW(_taskbar, IntPtr.Zero, "TrayNotifyWnd", null), out Rectangle tray);
            target = TaskbarPlacement.Windows11(
                container,
                tray,
                _layout.Size.Width,
                gap: Scale(6, dpi),
                extraAvoid: WidgetsButtonWidth(dpi),
                fallbackTrayWidth: Scale(200, dpi));
        }
        else
        {
            (Rectangle Widget, Rectangle TaskList)? placement = TryGetRect(_taskList, out Rectangle taskList)
                ? TaskbarPlacement.Windows10(container, taskList, _layout.Size.Width, Scale(4, dpi))
                : null;
            if (placement is null)
            {
                return; // 空間不夠，這一次先不動
            }

            (Rectangle widget, Rectangle list) = placement.Value;

            // Explorer 重新排版時會把工作清單恢復原寬，所以每次都檢查，被改回去就再縮一次。
            if (list.Width != taskList.Width || list.Left != taskList.Left - container.Left)
            {
                MoveWindow(_taskList, list.Left, list.Top, list.Width, list.Height, true);
            }

            _taskListSqueezed = true;
            target = widget;
        }

        SetWindowPos(Handle, IntPtr.Zero, target.X, target.Y, target.Width, target.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_font is null)
        {
            return;
        }

        // 內容在工作列的高度內垂直置中。
        int offsetY = Math.Max(0, (ClientSize.Height - _layout.Size.Height) / 2);

        foreach (TextRun run in _layout.Runs)
        {
            Color color = run.Level is Level level ? _palette.For(level) : _palette.Label;
            TextRenderer.DrawText(e.Graphics, run.Text, _font, new Point(run.X, offsetY + run.Y), color, BackColor, TextFlags);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right)
        {
            _showMenu();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            RestoreTaskList();
            _font?.Dispose();
            _font = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>Windows 10：把先前縮短的工作清單還原。</summary>
    private void RestoreTaskList()
    {
        if (!_taskListSqueezed)
        {
            return;
        }

        _taskListSqueezed = false;
        if (IsWindow(_container) && IsWindow(_taskList)
            && TryGetRect(_container, out Rectangle container) && TryGetRect(_taskList, out Rectangle taskList))
        {
            Rectangle restored = TaskbarPlacement.Windows10Restore(container, taskList);
            MoveWindow(_taskList, restored.Left, restored.Top, restored.Width, restored.Height, true);
        }
    }

    // ---- 外觀 ----

    /// <summary>依工作列是深色或淺色，切換文字配色與色鍵。</summary>
    private void ApplyTheme()
    {
        bool light = IsTaskbarLight();
        if (_lightTheme == light)
        {
            return;
        }

        _lightTheme = light;
        _palette = light ? LightPalette : DarkPalette;
        Color key = light ? LightKey : DarkKey;
        BackColor = key;

        if (IsHandleCreated)
        {
            uint colorRef = (uint)(key.R | (key.G << 8) | (key.B << 16));
            SetLayeredWindowAttributes(Handle, colorRef, 0, LWA_COLORKEY);
        }
    }

    private void SetClickThrough(bool enabled)
    {
        if (_clickThrough == enabled || !IsHandleCreated)
        {
            return;
        }

        _clickThrough = enabled;
        int exStyle = GetWindowLongW(Handle, GWL_EXSTYLE);
        SetWindowLongW(Handle, GWL_EXSTYLE, enabled ? exStyle | WS_EX_TRANSPARENT : exStyle & ~WS_EX_TRANSPARENT);
    }

    private void EnsureFont(int dpi, int availableHeight)
    {
        if (_font is not null && dpi == _fontDpi && availableHeight == _fontFitHeight)
        {
            return;
        }

        int rowGap = GridSpacing.ForDpi(dpi).RowGap;
        float points = TaskbarPlacement.FitFontPoints(
            FontCandidates,
            candidate =>
            {
                using Font probe = CreateFont(candidate, dpi);
                return RowHeight(probe);
            },
            rowGap,
            availableHeight - Scale(2, dpi));

        if (_font is null || points != _fontPoints || dpi != _fontDpi)
        {
            _font?.Dispose();
            _font = CreateFont(points, dpi);
        }

        (_fontPoints, _fontDpi, _fontFitHeight) = (points, dpi, availableHeight);
    }

    private static Font CreateFont(float points, int dpi) =>
        new(FontFamily.GenericSansSerif, points * dpi / 72f, FontStyle.Bold, GraphicsUnit.Pixel);

    private static int RowHeight(Font font) => TextRenderer.MeasureText("電0", font, Unbounded, TextFlags).Height;

    private int MeasureWidth(string text) => TextRenderer.MeasureText(text, _font!, Unbounded, TextFlags).Width;

    private static int Scale(int pixelsAt96, int dpi) => (int)Math.Round(pixelsAt96 * dpi / 96.0, MidpointRounding.AwayFromZero);

    private int GetTaskbarDpi()
    {
        try
        {
            uint dpi = GetDpiForWindow(_taskbar);
            return dpi >= 96 ? (int)dpi : DeviceDpi;
        }
        catch (EntryPointNotFoundException)
        {
            return DeviceDpi; // Windows 10 1607 之前沒有這個函式
        }
    }

    // ---- 讀取 Windows 的設定 ----

    /// <summary>工作列跟著「Windows 模式」的深淺色走。</summary>
    private static bool IsTaskbarLight() =>
        ReadDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme") == 1;

    /// <summary>
    /// Windows 11 的工作列靠左對齊時，「小工具」（天氣）按鈕會跑到系統匣的左邊，需要避開。
    /// 置中對齊時它在最左邊，不影響。
    /// </summary>
    private int WidgetsButtonWidth(int dpi)
    {
        const string advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

        bool leftAligned = ReadDword(advanced, "TaskbarAl") == 0;
        bool widgetsShown = ReadDword(advanced, "TaskbarDa") is int value && value != 0;
        if (!leftAligned || !widgetsShown)
        {
            return 0;
        }

        _widgetsInstalled ??= IsWidgetsPackageInstalled();
        return _widgetsInstalled == true ? Scale(150, dpi) : 0;
    }

    private static bool IsWidgetsPackageInstalled()
    {
        try
        {
            string packages = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            return Directory.Exists(packages)
                && Directory.EnumerateDirectories(packages, "MicrosoftWindows.Client.WebExperience*").Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static int? ReadDword(string subKey, string name)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKey);
            return key?.GetValue(name) as int?;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static bool TryGetRect(IntPtr window, out Rectangle rectangle)
    {
        if (window != IntPtr.Zero && GetWindowRect(window, out RECT rect))
        {
            rectangle = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            return true;
        }

        rectangle = Rectangle.Empty;
        return false;
    }

    private sealed record Palette(Color Label, Color Neutral, Color Good, Color Warn, Color Critical)
    {
        public Color For(Level level) => level switch
        {
            Level.Good => Good,
            Level.Warn => Warn,
            Level.Critical => Critical,
            _ => Neutral,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr FindWindowW(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr FindWindowExW(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out RECT rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetWindowLongW(IntPtr window, int index);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int SetWindowLongW(IntPtr window, int index, int newValue);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr window, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(IntPtr window);
}
