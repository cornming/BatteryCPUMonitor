using BatteryCPUMonitor.Metrics;

namespace BatteryCPUMonitor;

/// <summary>
/// 置頂、無邊框的資訊橫條：顯示電量、CPU 與記憶體使用率，各自依門檻變色。
/// 左鍵拖曳可移動（位置會記住），滑鼠移入時放大，右鍵或系統匣圖示開啟選單。
/// </summary>
internal sealed class BarForm : Form
{
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private const float NormalFontPoints = 9f;
    private const float HoverFontPoints = 29f;
    private const int FirstRefreshMs = 300;
    private const int RefreshMs = 1000;
    private const int MaxTrayTextLength = 63;

    private const TextFormatFlags TextFlags =
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

    private static readonly Size Unbounded = new(int.MaxValue, int.MaxValue);
    private static readonly Color NeutralColor = Color.FromArgb(180, 180, 180);
    private static readonly Color GoodColor = Color.FromArgb(50, 215, 75);
    private static readonly Color WarnColor = Color.FromArgb(255, 214, 10);
    private static readonly Color CriticalColor = Color.FromArgb(255, 69, 58);

    // 以欄位初始設定式載入：基底類別建構時就會讀 CreateParams，那時設定必須已經就緒。
    private readonly string _settingsPath = AppSettings.DefaultPath;
    private readonly AppSettings _settings = AppSettings.Load(AppSettings.DefaultPath);

    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _autoStartItem = new("開機自動啟動");
    private readonly ToolStripMenuItem _clickThroughItem = new("滑鼠穿透");
    private readonly NotifyIcon _tray = new();
    private readonly Icon _icon = LoadIcon();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly CpuSampler _cpu = new();

    private Font _normalFont;
    private Font _hoverFont;
    private int _fontDpi;

    private IReadOnlyList<BarSegment> _segments = [];

    /// <summary>使用者拖曳後記下的錨點（橫條底邊中點）；沒拖過就是 null，跟著預設位置走。</summary>
    private Point? _userAnchor;

    private bool _hover;
    private bool _dragging;
    private bool _dragMoved;
    private Point _dragStartCursor;
    private Point _dragStartAnchor;

    public BarForm()
    {
        Text = "BatteryCPUMonitor";
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
        Opacity = 0.6;
        Icon = _icon;
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);

        _userAnchor = _settings.Anchor;
        _fontDpi = DeviceDpi;
        _normalFont = CreateFont(NormalFontPoints, _fontDpi);
        _hoverFont = CreateFont(HoverFontPoints, _fontDpi);

        BuildMenu();
        ContextMenuStrip = _menu;

        _tray.Icon = _icon;
        _tray.Text = "BatteryCPUMonitor";
        _tray.ContextMenuStrip = _menu;

        _timer.Interval = FirstRefreshMs;
        _timer.Tick += (_, _) =>
        {
            _timer.Interval = RefreshMs;
            RefreshMetrics();
        };
    }

    private Font CurrentFont => _hover ? _hoverFont : _normalFont;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW; // 不出現在 Alt+Tab 清單

            if (_settings.ClickThrough)
            {
                cp.ExStyle |= WS_EX_TRANSPARENT; // 滑鼠事件直接穿過橫條，落到後面的視窗
            }

            return cp;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _tray.Visible = true;
        RefreshMetrics();
        _timer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _cpu.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _menu.Dispose();
            _normalFont.Dispose();
            _hoverFont.Dispose();
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }

    // ---- 選單 ----

    private void BuildMenu()
    {
        var resetPosition = new ToolStripMenuItem("重設位置");
        var close = new ToolStripMenuItem("關閉");

        _autoStartItem.Click += (_, _) => AutoStart.SetEnabled(!AutoStart.IsEnabled());
        _clickThroughItem.Click += (_, _) => SetClickThrough(!_settings.ClickThrough);
        resetPosition.Click += (_, _) => ResetPosition();
        close.Click += (_, _) => Close();

        // 每次開啟選單時才讀取實際狀態，勾選永遠反映現況。
        _menu.Opening += (_, _) =>
        {
            _autoStartItem.Checked = AutoStart.IsEnabled();
            _clickThroughItem.Checked = _settings.ClickThrough;
        };

        _menu.Items.Add(new ToolStripMenuItem($"BatteryCPUMonitor v{Application.ProductVersion}") { Enabled = false });
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_autoStartItem);
        _menu.Items.Add(_clickThroughItem);
        _menu.Items.Add(resetPosition);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(close);
    }

    private void SetClickThrough(bool enabled)
    {
        _settings.ClickThrough = enabled;
        _settings.Save(_settingsPath);

        UpdateStyles();  // 依 CreateParams 重新套用視窗樣式
        TopMost = true;  // 樣式更新後重新確認置頂

        if (enabled)
        {
            SetHover(false);

            // 開啟後橫條本身點不到了，提醒使用者之後要從系統匣操作。
            _tray.ShowBalloonTip(
                5000,
                "已開啟滑鼠穿透",
                "橫條不會再擋住滑鼠。要關閉或移動橫條，請在系統匣的電池圖示上按右鍵。",
                ToolTipIcon.Info);
        }
    }

    private void ResetPosition()
    {
        _userAnchor = null;
        _settings.Anchor = null;
        _settings.Save(_settingsPath);
        ApplyLayout();
    }

    // ---- 更新與繪製 ----

    /// <summary>每秒執行一次。所有讀取都是立即回傳，不會卡住畫面。</summary>
    private void RefreshMetrics()
    {
        _segments = BarText.Build(BatteryReader.Read(), _cpu.Sample(), MemoryReader.ReadUsedPercent());

        string plain = BarText.Plain(_segments);
        _tray.Text = plain.Length <= MaxTrayTextLength ? plain : plain[..MaxTrayTextLength];

        ApplyLayout();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Font font = CurrentFont;
        Size padding = PaddingFor(_fontDpi);
        int x = padding.Width;

        foreach (BarSegment segment in _segments)
        {
            TextRenderer.DrawText(e.Graphics, segment.Text, font, new Point(x, padding.Height), ColorFor(segment.Level), BackColor, TextFlags);
            x += TextRenderer.MeasureText(segment.Text, font, Unbounded, TextFlags).Width;
        }
    }

    private Size MeasureBar()
    {
        Font font = CurrentFont;
        Size padding = PaddingFor(_fontDpi);
        int width = 0;
        int height = TextRenderer.MeasureText("0", font, Unbounded, TextFlags).Height;

        foreach (BarSegment segment in _segments)
        {
            width += TextRenderer.MeasureText(segment.Text, font, Unbounded, TextFlags).Width;
        }

        return new Size(width + padding.Width * 2, height + padding.Height * 2);
    }

    /// <summary>讓視窗大小貼合文字，並依錨點重新擺放。</summary>
    private void ApplyLayout()
    {
        Size size = MeasureBar();

        Point anchor;
        Rectangle workingArea;
        if (_userAnchor is Point userAnchor)
        {
            // 以錨點所在（或最接近）的螢幕為準；螢幕被拔掉時會自動落到還在的螢幕上。
            anchor = userAnchor;
            workingArea = Screen.FromPoint(userAnchor).WorkingArea;
        }
        else
        {
            workingArea = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
            anchor = BarPlacement.DefaultAnchor(workingArea);
        }

        var bounds = new Rectangle(BarPlacement.TopLeftFor(anchor, size, workingArea), size);
        if (Bounds != bounds)
        {
            Bounds = bounds;
        }

        Invalidate();
    }

    private static Color ColorFor(Level level) => level switch
    {
        Level.Good => GoodColor,
        Level.Warn => WarnColor,
        Level.Critical => CriticalColor,
        _ => NeutralColor,
    };

    // ---- 高 DPI ----

    /// <summary>字體以像素為單位建立，大小依橫條所在螢幕的 DPI 換算。</summary>
    private static Font CreateFont(float points, int dpi) =>
        new(FontFamily.GenericSansSerif, points * dpi / 72f, FontStyle.Bold, GraphicsUnit.Pixel);

    private static Size PaddingFor(int dpi) =>
        new((int)Math.Round(4 * dpi / 96.0), (int)Math.Round(1 * dpi / 96.0));

    /// <summary>橫條被移到不同縮放比例的螢幕時，依新的 DPI 重建字體並重新排版。</summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        e.Cancel = true; // 不採用系統建議的視窗大小，由 ApplyLayout 依新 DPI 重算
        base.OnDpiChanged(e);

        if (e.DeviceDpiNew != _fontDpi)
        {
            Font oldNormal = _normalFont;
            Font oldHover = _hoverFont;
            _fontDpi = e.DeviceDpiNew;
            _normalFont = CreateFont(NormalFontPoints, _fontDpi);
            _hoverFont = CreateFont(HoverFontPoints, _fontDpi);
            oldNormal.Dispose();
            oldHover.Dispose();
        }

        ApplyLayout();
    }

    // ---- 滑鼠：移入放大、左鍵拖曳 ----

    private void SetHover(bool hover)
    {
        if (_hover != hover)
        {
            _hover = hover;
            ApplyLayout();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        SetHover(true);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_dragging)
        {
            SetHover(false);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = true;
        _dragMoved = false;
        _dragStartCursor = Cursor.Position;
        _dragStartAnchor = BarPlacement.AnchorOf(Bounds);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
        {
            return;
        }

        Point cursor = Cursor.Position;
        int dx = cursor.X - _dragStartCursor.X;
        int dy = cursor.Y - _dragStartCursor.Y;

        if (!_dragMoved)
        {
            // 移動超過系統的拖曳門檻才算拖曳，單純點一下不會改變位置。
            Size threshold = SystemInformation.DragSize;
            if (Math.Abs(dx) < threshold.Width && Math.Abs(dy) < threshold.Height)
            {
                return;
            }

            _dragMoved = true;
        }

        _userAnchor = new Point(_dragStartAnchor.X + dx, _dragStartAnchor.Y + dy);
        ApplyLayout();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
        {
            EndDrag();
        }
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        EndDrag(); // 拖曳途中被其他視窗搶走滑鼠時，同樣要收尾
    }

    private void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;

        if (_dragMoved)
        {
            // 記下實際落點（已被限制在螢幕內），下次啟動回到同一個位置。
            _userAnchor = BarPlacement.AnchorOf(Bounds);
            _settings.Anchor = _userAnchor;
            _settings.Save(_settingsPath);
        }

        if (!ClientRectangle.Contains(PointToClient(Cursor.Position)))
        {
            SetHover(false);
        }
    }

    private static Icon LoadIcon()
    {
        using Stream? stream = typeof(BarForm).Assembly.GetManifestResourceStream("app.ico");
        return stream is null
            ? (Icon)SystemIcons.Application.Clone()
            : new Icon(stream, SystemInformation.SmallIconSize);
    }
}
