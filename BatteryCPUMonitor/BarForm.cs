using System.Drawing.Drawing2D;
using BatteryCPUMonitor.Metrics;

namespace BatteryCPUMonitor;

/// <summary>
/// 置頂、無邊框的資訊橫條：以兩列格狀版面顯示電池、CPU、記憶體、GPU、磁碟與網路，數值各自依門檻變色。
/// 左鍵拖曳可移動（位置會記住），滑鼠移入時變得不透明，右鍵或系統匣圖示開啟選單。
/// </summary>
internal sealed class BarForm : Form
{
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private const float FontPoints = 9f;
    private const int FirstRefreshMs = 300;
    private const int RefreshMs = 1000;
    private const int MaxTrayTextLength = 63;
    private const int CornerRadiusAt96Dpi = 7;

    // 兩者都小於 1，視窗才會一直維持「分層視窗」樣式；滑鼠穿透需要它，切換時也不會閃爍。
    private const double RestingOpacity = 0.8;
    private const double HoverOpacity = 0.99;

    private const TextFormatFlags TextFlags =
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

    private static readonly Size Unbounded = new(int.MaxValue, int.MaxValue);
    private static readonly Color PanelColor = Color.FromArgb(28, 28, 30);
    private static readonly Color LabelColor = Color.FromArgb(235, 235, 235);
    private static readonly Color NeutralColor = Color.FromArgb(170, 170, 170);
    private static readonly Color GoodColor = Color.FromArgb(50, 215, 75);
    private static readonly Color WarnColor = Color.FromArgb(255, 214, 10);
    private static readonly Color CriticalColor = Color.FromArgb(255, 69, 58);

    // 以欄位初始設定式載入：基底類別建構時就會讀 CreateParams，那時設定必須已經就緒。
    private readonly string _settingsPath = AppSettings.DefaultPath;
    private readonly AppSettings _settings = AppSettings.Load(AppSettings.DefaultPath);

    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _autoStartItem = new("開機自動啟動");
    private readonly ToolStripMenuItem _clickThroughItem = new("滑鼠穿透");
    private readonly ToolStripMenuItem _showBatteryItem = new("電池");
    private readonly ToolStripMenuItem _showBatteryDetailItem = new("電池詳情（功耗、健康度）");
    private readonly ToolStripMenuItem _showCpuRamItem = new("CPU 與記憶體");
    private readonly ToolStripMenuItem _showGpuItem = new("GPU 與顯示記憶體");
    private readonly ToolStripMenuItem _showDiskItem = new("磁碟讀寫");
    private readonly ToolStripMenuItem _showNetworkItem = new("網路速度");
    private readonly NotifyIcon _tray = new();
    private readonly Icon _icon = LoadIcon();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly MetricsCollector _collector = new();

    private Font _font;
    private int _fontDpi;

    private MetricsSnapshot _snapshot = MetricsSnapshot.Empty;
    private GridResult _layout = new(Size.Empty, []);
    private bool _sampling;

    /// <summary>使用者拖曳後記下的錨點（橫條底邊中點）；沒拖過就是 null，跟著預設位置走。</summary>
    private Point? _userAnchor;

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
        BackColor = PanelColor;
        Opacity = RestingOpacity;
        Icon = _icon;
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);

        _userAnchor = _settings.Anchor;
        _fontDpi = DeviceDpi;
        _font = CreateFont(_fontDpi);

        BuildMenu();
        ContextMenuStrip = _menu;

        _tray.Icon = _icon;
        _tray.Text = "BatteryCPUMonitor";
        _tray.ContextMenuStrip = _menu;

        _timer.Interval = FirstRefreshMs;
        _timer.Tick += (_, _) => RefreshMetrics();
    }

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

        // 電池狀態可以立即讀到，先填上；其餘數值以「--」佔位，約 0.3 秒後補上。
        _snapshot = MetricsSnapshot.Empty with { Battery = BatteryReader.Read() };
        ApplyLayout();
        _timer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _collector.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _menu.Dispose();
            _font.Dispose();
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }

    // ---- 選單 ----

    private void BuildMenu()
    {
        var showItems = new ToolStripMenuItem("顯示項目");
        var resetPosition = new ToolStripMenuItem("重設位置");
        var close = new ToolStripMenuItem("關閉");

        showItems.DropDownItems.Add(_showBatteryItem);
        showItems.DropDownItems.Add(_showBatteryDetailItem);
        showItems.DropDownItems.Add(_showCpuRamItem);
        showItems.DropDownItems.Add(_showGpuItem);
        showItems.DropDownItems.Add(_showDiskItem);
        showItems.DropDownItems.Add(_showNetworkItem);

        _showBatteryItem.Click += (_, _) => ToggleItem(s => s.ShowBattery = !s.ShowBattery);
        _showBatteryDetailItem.Click += (_, _) => ToggleItem(s => s.ShowBatteryDetail = !s.ShowBatteryDetail);
        _showCpuRamItem.Click += (_, _) => ToggleItem(s => s.ShowCpuRam = !s.ShowCpuRam);
        _showGpuItem.Click += (_, _) => ToggleItem(s => s.ShowGpu = !s.ShowGpu);
        _showDiskItem.Click += (_, _) => ToggleItem(s => s.ShowDisk = !s.ShowDisk);
        _showNetworkItem.Click += (_, _) => ToggleItem(s => s.ShowNetwork = !s.ShowNetwork);

        _autoStartItem.Click += (_, _) => AutoStart.SetEnabled(!AutoStart.IsEnabled());
        _clickThroughItem.Click += (_, _) => SetClickThrough(!_settings.ClickThrough);
        resetPosition.Click += (_, _) => ResetPosition();
        close.Click += (_, _) => Close();

        // 每次開啟選單時才讀取實際狀態，勾選永遠反映現況。
        _menu.Opening += (_, _) =>
        {
            _showBatteryItem.Checked = _settings.ShowBattery;
            _showBatteryItem.Enabled = _snapshot.Battery.HasBattery;
            _showBatteryDetailItem.Checked = _settings.ShowBatteryDetail;
            _showBatteryDetailItem.Enabled = _snapshot.Battery.HasBattery;
            _showCpuRamItem.Checked = _settings.ShowCpuRam;
            _showGpuItem.Checked = _settings.ShowGpu;
            _showGpuItem.Enabled = _collector.HasGpuCounters;
            _showDiskItem.Checked = _settings.ShowDisk;
            _showNetworkItem.Checked = _settings.ShowNetwork;
            _autoStartItem.Checked = AutoStart.IsEnabled();
            _clickThroughItem.Checked = _settings.ClickThrough;
        };

        _menu.Items.Add(new ToolStripMenuItem($"BatteryCPUMonitor v{Application.ProductVersion}") { Enabled = false });
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(showItems);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_autoStartItem);
        _menu.Items.Add(_clickThroughItem);
        _menu.Items.Add(resetPosition);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(close);
    }

    private void ToggleItem(Action<AppSettings> toggle)
    {
        toggle(_settings);
        _settings.Save(_settingsPath);
        ApplyLayout();
    }

    private void SetClickThrough(bool enabled)
    {
        _settings.ClickThrough = enabled;
        _settings.Save(_settingsPath);

        UpdateStyles();  // 依 CreateParams 重新套用視窗樣式
        TopMost = true;  // 樣式更新後重新確認置頂

        if (enabled)
        {
            Opacity = RestingOpacity;

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

    /// <summary>
    /// 每秒執行一次。讀取數值的工作放到背景執行緒，畫面執行緒只負責顯示，
    /// 即使某次讀取比較慢，拖曳和選單也不會頓。
    /// </summary>
    private async void RefreshMetrics()
    {
        if (_sampling)
        {
            return; // 上一次還沒讀完就先跳過，不排隊
        }

        _sampling = true;
        try
        {
            VisibleItems visible = _settings.Visible;
            MetricsSnapshot snapshot = await Task.Run(() => _collector.Sample(visible));
            if (IsDisposed || Disposing)
            {
                return;
            }

            _timer.Interval = RefreshMs;
            _snapshot = snapshot;

            string trayText = BarContent.TrayText(snapshot);
            _tray.Text = trayText.Length <= MaxTrayTextLength ? trayText : trayText[..MaxTrayTextLength];

            ApplyLayout();
        }
        catch (ObjectDisposedException)
        {
            // 程式正在關閉，不需要再更新畫面。
        }
        finally
        {
            _sampling = false;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        foreach (TextRun run in _layout.Runs)
        {
            Color color = run.Level is Level level ? ColorFor(level) : LabelColor;
            TextRenderer.DrawText(e.Graphics, run.Text, _font, new Point(run.X, run.Y), color, BackColor, TextFlags);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateRoundedCorners();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRoundedCorners();
    }

    private int MeasureWidth(string text) => TextRenderer.MeasureText(text, _font, Unbounded, TextFlags).Width;

    /// <summary>重新排版，讓視窗大小貼合內容，並依錨點重新擺放。</summary>
    private void ApplyLayout()
    {
        int rowHeight = TextRenderer.MeasureText("電0", _font, Unbounded, TextFlags).Height;
        _layout = GridLayout.Compute(
            BarContent.Build(_snapshot, _settings.Visible),
            MeasureWidth,
            rowHeight,
            GridSpacing.ForDpi(_fontDpi));

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

        var bounds = new Rectangle(BarPlacement.TopLeftFor(anchor, _layout.Size, workingArea), _layout.Size);
        if (Bounds != bounds)
        {
            Bounds = bounds;
        }

        Invalidate();
    }

    private void UpdateRoundedCorners()
    {
        // 基底類別建構期間也會觸發 SizeChanged，那時視窗還沒建立，先不處理。
        if (!IsHandleCreated || Width <= 0 || Height <= 0)
        {
            return;
        }

        int diameter = Math.Min(Math.Min(Width, Height), 2 * (int)Math.Round(CornerRadiusAt96Dpi * _fontDpi / 96.0));
        if (diameter < 2)
        {
            return;
        }

        using var path = new GraphicsPath();
        path.AddArc(0, 0, diameter, diameter, 180, 90);
        path.AddArc(Width - diameter, 0, diameter, diameter, 270, 90);
        path.AddArc(Width - diameter, Height - diameter, diameter, diameter, 0, 90);
        path.AddArc(0, Height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        Region? old = Region;
        Region = new Region(path);
        old?.Dispose();
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
    private static Font CreateFont(int dpi) =>
        new(FontFamily.GenericSansSerif, FontPoints * dpi / 72f, FontStyle.Bold, GraphicsUnit.Pixel);

    /// <summary>橫條被移到不同縮放比例的螢幕時，依新的 DPI 重建字體並重新排版。</summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        e.Cancel = true; // 不採用系統建議的視窗大小，由 ApplyLayout 依新 DPI 重算
        base.OnDpiChanged(e);

        if (e.DeviceDpiNew != _fontDpi)
        {
            Font old = _font;
            _fontDpi = e.DeviceDpiNew;
            _font = CreateFont(_fontDpi);
            old.Dispose();
        }

        ApplyLayout();
    }

    // ---- 滑鼠：移入變清楚、左鍵拖曳 ----

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Opacity = HoverOpacity;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_dragging)
        {
            Opacity = RestingOpacity;
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
            Opacity = RestingOpacity;
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
