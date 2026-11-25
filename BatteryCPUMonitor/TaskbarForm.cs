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
/// 有多個螢幕時，可以選擇掛在哪一個螢幕的工作列上：主螢幕的工作列是 Shell_TrayWnd，
/// 其他螢幕的是各自獨立的 Shell_SecondaryTrayWnd。
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

    private const TextFormatFlags TextFlags = BarRenderer.TextFlags;

    private static readonly Size Unbounded = new(int.MaxValue, int.MaxValue);
    private static readonly float[] FontCandidates = [8.75f, 8.25f, 7.75f, 7.25f, 6.75f];

    private readonly Action _showMenu;
    private readonly bool _isWindows11 = WindowChrome.IsWindows11;

    private ScreenInfo _target;
    private IntPtr _taskbar;
    private IntPtr _container; // 實際的父視窗：Windows 11 為工作列本身，Windows 10 為 ReBarWindow32
    private IntPtr _taskList;  // 只有 Windows 10 會用到
    private bool _taskListSqueezed;

    private ThemeFontSet? _fonts;
    private float _fontPoints;
    private int _fontDpi;
    private int _fontFitHeight;
    private BarVisualStyle _fontFitStyle;
    private bool _fontFitCapsule;
    private bool _metersVisible = true;

    private StyledGridResult _layout = new(Size.Empty, Rectangle.Empty, [], []);
    private BarVisualStyle _style = BarVisualStyle.Obsidian;
    private ThemeMode _themeMode = ThemeMode.System;
    private Level _overallLevel = Level.Neutral;
    private IReadOnlyList<double> _cpuHistory = [];
    private bool _pulseBright = true;
    private TaskbarThemeState? _themeState;
    private ThemePalette _palette = BarTheme.TaskbarDark;
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
        BackColor = BarTheme.TaskbarDarkKey;
        Bounds = new Rectangle(-32000, -32000, 1, 1); // 掛上工作列之前先放在畫面外
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);
    }

    /// <summary>目前掛在哪個螢幕（裝置名稱）。還沒掛上時為空字串。</summary>
    public string TargetDevice => _target.DeviceName ?? string.Empty;

    /// <summary>目前接上的所有螢幕。</summary>
    public static IReadOnlyList<ScreenInfo> Screens() =>
        Screen.AllScreens.Select(screen => new ScreenInfo(screen.DeviceName, screen.Bounds, screen.Primary)).ToList();

    /// <summary>依設定值（螢幕的裝置名稱，null 為主螢幕）找出實際要用的螢幕。</summary>
    public static ScreenInfo ResolveTarget(string? preferredDevice) => TaskbarTargets.Resolve(preferredDevice, Screens());

    /// <summary>
    /// 找出指定螢幕上的工作列視窗；這個螢幕沒有工作列（例如 Windows 設定裡沒有開啟「在所有顯示器上顯示工作列」），
    /// 或 Explorer 正在重新啟動時，回傳 <see cref="IntPtr.Zero"/>。
    /// </summary>
    public static IntPtr FindTaskbarOn(ScreenInfo screen)
    {
        if (screen.IsPrimary)
        {
            return FindWindowW("Shell_TrayWnd", null);
        }

        var handles = new List<IntPtr>();
        var rectangles = new List<Rectangle>();
        IntPtr window = IntPtr.Zero;
        while ((window = FindWindowExW(IntPtr.Zero, window, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            if (TryGetRect(window, out Rectangle rectangle))
            {
                handles.Add(window);
                rectangles.Add(rectangle);
            }
        }

        return TaskbarTargets.IndexOfTaskbarOn(screen.Bounds, rectangles) is int index ? handles[index] : IntPtr.Zero;
    }

    /// <summary>指定的螢幕上有沒有工作列可以掛。</summary>
    public static bool HasTaskbarOn(ScreenInfo screen) => FindTaskbarOn(screen) != IntPtr.Zero;

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

    /// <summary>
    /// 把小工具掛到工作列上。找不到工作列，或工作列是直式的，回傳 false。
    /// </summary>
    /// <param name="clickThrough">是否讓滑鼠穿透。</param>
    /// <param name="targetDevice">要掛在哪個螢幕的工作列（裝置名稱）；null 為主螢幕，指定的螢幕沒接上時也改用主螢幕。</param>
    public bool TryAttach(bool clickThrough, string? targetDevice = null)
    {
        _target = ResolveTarget(targetDevice);
        _taskbar = FindTaskbarOn(_target);
        if (_taskbar == IntPtr.Zero)
        {
            return false;
        }

        IntPtr container = _taskbar;
        if (!_isWindows11)
        {
            // Windows 10 的結構：Shell_TrayWnd → ReBarWindow32 → MSTaskSwWClass（工作清單）。
            // 副螢幕的工作列內部名稱不太一樣，容器可能叫 WorkerW、工作清單可能叫 MSTaskListWClass。
            container = FindWindowExW(_taskbar, IntPtr.Zero, "ReBarWindow32", null);
            if (container == IntPtr.Zero)
            {
                container = FindWindowExW(_taskbar, IntPtr.Zero, "WorkerW", null);
            }

            if (container != IntPtr.Zero)
            {
                _taskList = FindWindowExW(container, IntPtr.Zero, "MSTaskSwWClass", null);
                if (_taskList == IntPtr.Zero)
                {
                    _taskList = FindWindowExW(container, IntPtr.Zero, "MSTaskListWClass", null);
                }
            }

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
    public void UpdateContent(
        IReadOnlyList<BarColumn> columns,
        bool clickThrough,
        BarVisualStyle style = BarVisualStyle.Obsidian,
        ThemeMode themeMode = ThemeMode.System,
        IReadOnlyList<double>? cpuHistory = null,
        bool pulseBright = true)
    {
        if (!IsAttached || !TryGetRect(_container, out Rectangle container))
        {
            return;
        }

        _style = style;
        _themeMode = themeMode;
        _overallLevel = BarTheme.OverallLevel(columns);
        _cpuHistory = cpuHistory ?? [];
        _pulseBright = pulseBright;

        ApplyTheme();
        SetClickThrough(clickThrough);

        bool useCapsule = _themeState?.UseCapsule == true;
        int dpi = GetTaskbarDpi();
        EnsureFonts(dpi, container.Height, style, useCapsule);

        StyledGridMetrics metrics = StyledGridMetrics.For(style, dpi, taskbar: true);
        if (useCapsule)
        {
            metrics = metrics.WithCapsulePadding(dpi);
        }

        if (!_metersVisible)
        {
            metrics = metrics.WithoutMeters();
        }

        if (useCapsule)
        {
            using Graphics measureGraphics = CreateGraphics();
            int labelHeight = BarRenderer.MeasureFloatingText(measureGraphics, "電A", _fonts!.Label).Height;
            int valueHeight = BarRenderer.MeasureFloatingText(measureGraphics, "00", _fonts.Value).Height;
            int unitHeight = BarRenderer.MeasureFloatingText(measureGraphics, "%W", _fonts.Unit).Height;

            _layout = GridLayout.ComputeStyled(
                columns,
                s => BarRenderer.MeasureFloatingText(measureGraphics, s, _fonts.Label).Width,
                s => BarRenderer.MeasureFloatingText(measureGraphics, s, _fonts.Value).Width,
                s => BarRenderer.MeasureFloatingText(measureGraphics, s, _fonts.Unit).Width,
                labelHeight,
                valueHeight,
                unitHeight,
                metrics);
        }
        else
        {
            int labelHeight = TextRenderer.MeasureText("電A", _fonts!.Label, Unbounded, TextFlags).Height;
            int valueHeight = TextRenderer.MeasureText("00", _fonts.Value, Unbounded, TextFlags).Height;
            int unitHeight = TextRenderer.MeasureText("%W", _fonts.Unit, Unbounded, TextFlags).Height;

            _layout = GridLayout.ComputeStyled(columns, MeasureLabel, MeasureValue, MeasureUnit, labelHeight, valueHeight, unitHeight, metrics);
        }

        Rectangle target;
        if (_isWindows11)
        {
            // 副螢幕的工作列不一定有系統匣，只有時鐘；找不到時改保留一段寬度避開時鐘。
            // 「小工具」（天氣）按鈕只出現在主螢幕。
            TryGetRect(FindWindowExW(_taskbar, IntPtr.Zero, "TrayNotifyWnd", null), out Rectangle tray);
            target = TaskbarPlacement.Windows11(
                container,
                tray,
                _layout.Size.Width,
                gap: Scale(6, dpi),
                extraAvoid: _target.IsPrimary ? WidgetsButtonWidth(dpi) : 0,
                fallbackTrayWidth: Scale(_target.IsPrimary ? 200 : 130, dpi));
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
        if (_fonts is null)
        {
            return;
        }

        if (_themeState?.UseCapsule == true)
        {
            BarRenderer.PaintTaskbarCapsule(
                e.Graphics,
                ClientSize,
                _layout,
                _fonts,
                _palette,
                _style,
                _overallLevel,
                _cpuHistory,
                _pulseBright,
                BackColor);
            return;
        }

        // 內容在工作列的高度內垂直置中。
        int offsetY = Math.Max(0, (ClientSize.Height - _layout.Size.Height) / 2);

        BarRenderer.PaintTaskbar(
            e.Graphics,
            offsetY,
            _layout,
            _fonts,
            _palette,
            _style,
            _overallLevel,
            _cpuHistory,
            _pulseBright,
            BackColor);
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
            _fonts?.Dispose();
            _fonts = null;
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

    /// <summary>
    /// 依使用者選擇的深淺主題與 Windows 工作列底色，切換色票、色鍵與是否啟用實心膠囊底框。
    /// 色鍵永遠跟隨 Windows 工作列本身底色，避免邊緣產生色邊；內容色票則依使用者選擇的主題決定。
    /// </summary>
    private void ApplyTheme()
    {
        TaskbarThemeState next = ThemeModes.ResolveTaskbar(_themeMode, WindowChrome.IsSystemTaskbarLight());
        if (_themeState == next)
        {
            return;
        }

        bool keyChanged = _themeState is null || _themeState.Value.TaskbarLight != next.TaskbarLight;
        _themeState = next;
        _palette = next.UseCapsule
            ? BarTheme.ForFloating(next.ContentLight)
            : BarTheme.ForTaskbar(next.ContentLight);

        if (keyChanged)
        {
            Color key = next.TaskbarLight ? BarTheme.TaskbarLightKey : BarTheme.TaskbarDarkKey;
            BackColor = key;

            if (IsHandleCreated)
            {
                uint colorRef = (uint)(key.R | (key.G << 8) | (key.B << 16));
                SetLayeredWindowAttributes(Handle, colorRef, 0, LWA_COLORKEY);
            }
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

    private void EnsureFonts(int dpi, int availableHeight, BarVisualStyle style, bool useCapsule)
    {
        if (_fonts is not null
            && dpi == _fontDpi
            && availableHeight == _fontFitHeight
            && style == _fontFitStyle
            && useCapsule == _fontFitCapsule)
        {
            return;
        }

        StyledGridMetrics metrics = StyledGridMetrics.For(style, dpi, taskbar: true);
        int extraPerRow = metrics.MeterHeight > 0 ? metrics.MeterGap + metrics.MeterHeight : 0;
        int reservedVertical = useCapsule ? Scale(9, dpi) : Scale(2, dpi);
        int maxAllowed = Math.Max(16, availableHeight - reservedVertical);

        // 先試著在保留微進度條的情況下挑最大字級；真的塞不下（例如極矮工作列）再收起微進度條。
        bool showMeters = metrics.MeterHeight > 0;
        float points = TaskbarPlacement.FitFontPoints(
            FontCandidates,
            candidate => SlotHeightFor(candidate, dpi, extraPerRow),
            metrics.RowGap,
            maxAllowed);

        if (showMeters && SlotHeightFor(points, dpi, extraPerRow) * 2 + metrics.RowGap > maxAllowed)
        {
            showMeters = false;
            points = TaskbarPlacement.FitFontPoints(
                FontCandidates,
                candidate => SlotHeightFor(candidate, dpi, 0),
                metrics.RowGap,
                maxAllowed);
        }

        if (_fonts is null || points != _fontPoints || dpi != _fontDpi)
        {
            _fonts?.Dispose();
            _fonts = ThemeFontSet.Create(points, dpi);
        }

        _metersVisible = showMeters;
        (_fontPoints, _fontDpi, _fontFitHeight, _fontFitStyle, _fontFitCapsule) = (points, dpi, availableHeight, style, useCapsule);
    }

    private static int SlotHeightFor(float points, int dpi, int extraPerRow)
    {
        using ThemeFontSet probe = ThemeFontSet.Create(points, dpi);
        int labelH = TextRenderer.MeasureText("電A", probe.Label, Unbounded, TextFlags).Height;
        int valueH = TextRenderer.MeasureText("00", probe.Value, Unbounded, TextFlags).Height;
        return Math.Max(labelH, valueH) + extraPerRow;
    }

    private int MeasureLabel(string text) => TextRenderer.MeasureText(text, _fonts!.Label, Unbounded, TextFlags).Width;

    private int MeasureValue(string text) => TextRenderer.MeasureText(text, _fonts!.Value, Unbounded, TextFlags).Width;

    private int MeasureUnit(string text) => TextRenderer.MeasureText(text, _fonts!.Unit, Unbounded, TextFlags).Width;

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
