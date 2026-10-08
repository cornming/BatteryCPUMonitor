using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using BatteryCPUMonitor.Metrics;
using BatteryCPUMonitor.Sensors;
using BatteryCPUMonitor.Updates;
using Microsoft.Win32;

namespace BatteryCPUMonitor;

/// <summary>
/// 置頂、無邊框的資訊橫條：以兩列格狀版面顯示電池、CPU、記憶體、GPU、磁碟與網路，數值各自依門檻變色。
/// 左鍵拖曳可移動（位置會記住），滑鼠移入時變得不透明，右鍵或系統匣圖示開啟選單。
/// 也可以切換成「嵌入工作列」，這時浮動橫條會隱藏，內容改由 <see cref="TaskbarForm"/> 畫在工作列上。
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
    private const int TaskbarRetryMs = 5000;

    // 啟動後稍等一下再檢查更新，不要跟開機時一堆程式搶資源；之後每 12 小時檢查一次。
    private const int FirstUpdateCheckMs = 15_000;
    private const int UpdateIntervalMs = 12 * 60 * 60 * 1000;
    private const string DialogCaption = "BatteryCPUMonitor";

    // 啟動後等多久再詢問是否授權硬體感測器（讓其他通知先顯示完）。
    private const int SensorPromptDelayMs = 9000;

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
    private readonly ToolStripMenuItem _showBarItem = new("顯示橫條");
    private readonly ToolStripMenuItem _clickThroughItem = new("滑鼠穿透");
    private readonly ToolStripMenuItem _taskbarModeItem = new("嵌入工作列");
    private readonly ToolStripMenuItem _checkUpdateItem = new("檢查更新…");
    private readonly ToolStripMenuItem _autoUpdateItem = new("自動更新");
    private readonly ToolStripMenuItem _showBatteryItem = new("電池");
    private readonly ToolStripMenuItem _showBatteryDetailItem = new("電池詳情（功耗、健康度）");
    private readonly ToolStripMenuItem _showCpuRamItem = new("CPU 與記憶體");
    private readonly ToolStripMenuItem _showGpuItem = new("GPU 與顯示記憶體");
    private readonly ToolStripMenuItem _showDiskItem = new("磁碟讀寫");
    private readonly ToolStripMenuItem _showNetworkItem = new("網路速度");
    private readonly ToolStripMenuItem _cpuModeMenu = new("CPU 使用率算法");
    private readonly ToolStripMenuItem _cpuTimeItem = new("依忙碌時間（與新版工作管理員一致）")
    {
        ToolTipText = "忙碌時間 ÷ 總時間。Windows 11 24H2 之後的工作管理員用這種算法。",
    };
    private readonly ToolStripMenuItem _cpuUtilityItem = new("依處理器效能（考慮加速與降頻）")
    {
        ToolTipText = "Processor Utility：CPU 跑在標稱頻率以上時數字會比較高。舊版 Windows 10 的工作管理員用這種算法。",
    };
    private readonly ToolStripMenuItem _showTemperatureItem = new("溫度（需要硬體感測器）");
    private readonly ToolStripMenuItem _showClockItem = new("頻率（CPU、GPU）");
    private readonly ToolStripMenuItem _showPowerItem = new("功耗（需要硬體感測器）");
    private readonly ToolStripMenuItem _showFansItem = new("風扇（需要硬體感測器）");
    private readonly ToolStripMenuItem _sensorsMenu = new("硬體感測器");
    private readonly ToolStripMenuItem _sensorsEnabledItem = new("啟用硬體感測器（需要系統管理員權限）");
    private readonly ToolStripMenuItem _sensorsReconnectItem = new("重新連線（需要系統管理員權限）");
    private readonly ToolStripMenuItem _sensorsStatusItem = new("狀態") { Enabled = false };
    private readonly ToolStripMenuItem _pawnIoItem = new("PawnIO…");
    private readonly ToolStripMenuItem _sensorsDiagnoseItem = new("診斷資訊…");
    private readonly ToolStripMenuItem _sensorsCopyDiagnosticsItem = new("複製診斷資訊");
    private SensorDiagnosticsForm? _diagnosticsForm;
    private readonly SensorService _sensors = new(
        new ElevatedHostLauncher(),
        connectTimeout: TimeSpan.FromMinutes(3),
        firstMessageTimeout: TimeSpan.FromSeconds(90),
        staleAfter: TimeSpan.FromSeconds(8));
    private readonly System.Windows.Forms.Timer _sensorPromptTimer = new();
    private readonly MetricsCollector _collector;

    // 硬體感測器每次啟動程式都要重新取得系統管理員權限。已啟用但還沒連線時，先用通知詢問，使用者點了才開始。
    private bool _sensorPromptActive;
    private readonly NotifyIcon _tray = new();
    private readonly Icon _icon = LoadIcon();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly System.Windows.Forms.Timer _updateTimer = new();

    private Font _font;
    private int _fontDpi;

    private MetricsSnapshot _snapshot = MetricsSnapshot.Empty;
    private GridResult _layout = new(Size.Empty, []);
    private bool _sampling;

    private UpdateController? _updater;
    private bool _updating;

    // 嵌入工作列用的小工具；只有在「嵌入工作列」開啟而且成功掛上時才存在。
    private TaskbarForm? _taskbar;
    private bool _taskbarActive;
    private long _taskbarRetryAt;

    // 浮動橫條現在該不該顯示：隱藏整個橫條，或已經嵌入工作列時都是 false。
    private bool _barVisible = true;

    /// <summary>使用者拖曳後記下的錨點（橫條底邊中點）；沒拖過就是 null，跟著預設位置走。</summary>
    private Point? _userAnchor;

    private bool _dragging;
    private bool _dragMoved;
    private Point _dragStartCursor;
    private Point _dragStartAnchor;

    /// <param name="afterUpdate">這個程式是剛更新完、由舊版本啟動的；會顯示「已更新」的提示。</param>
    public BarForm(bool afterUpdate = false)
    {
        _collector = new MetricsCollector(() => _sensors.Latest) { CpuMode = _settings.CpuMode };

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

        // 先把視窗建立起來（還不顯示）。嵌入工作列時這個視窗會一直隱藏，
        // 但選單與程式結束的流程都需要它存在。
        _ = Handle;

        _tray.Visible = true;

        if (afterUpdate)
        {
            _tray.ShowBalloonTip(5000, "已更新", $"BatteryCPUMonitor 已更新到 v{AppVersion.CurrentText}。", ToolTipIcon.Info);
        }

        _sensors.Changed += OnSensorsChanged;
        _tray.BalloonTipClicked += (_, _) => StartSensorsAfterConsent();
        _tray.BalloonTipClosed += (_, _) => _sensorPromptActive = false;
        ScheduleSensorPrompt();

        _updateTimer.Interval = FirstUpdateCheckMs;
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Interval = UpdateIntervalMs;
            if (_settings.AutoUpdate)
            {
                _ = RunUpdateAsync(interactive: false);
            }
        };
        _updateTimer.Start();

        // 電池狀態可以立即讀到，先填上；其餘數值以「--」佔位，約 0.3 秒後補上。
        _snapshot = MetricsSnapshot.Empty with { Battery = BatteryReader.Read() };
        ApplyLayout();
        _timer.Start();
    }

    /// <summary>嵌入工作列或隱藏橫條期間，浮動橫條保持隱藏。</summary>
    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(value && _barVisible);

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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _updateTimer.Dispose();
            _sensorPromptTimer.Dispose();
            _diagnosticsForm?.Dispose();
            _sensors.Dispose(); // 特權的感測器服務偵測到斷線會自己結束
            ReleaseTaskbar(); // Windows 10 會在這裡把工作清單還原
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
        showItems.DropDownItems.Add(new ToolStripSeparator());
        showItems.DropDownItems.Add(_showTemperatureItem);
        showItems.DropDownItems.Add(_showClockItem);
        showItems.DropDownItems.Add(_showPowerItem);
        showItems.DropDownItems.Add(_showFansItem);

        _cpuModeMenu.DropDownItems.Add(_cpuTimeItem);
        _cpuModeMenu.DropDownItems.Add(_cpuUtilityItem);
        _cpuTimeItem.Click += (_, _) => SetCpuMode(CpuUsageMode.Time);
        _cpuUtilityItem.Click += (_, _) => SetCpuMode(CpuUsageMode.Utility);

        _sensorsMenu.DropDownItems.Add(_sensorsEnabledItem);
        _sensorsMenu.DropDownItems.Add(_sensorsReconnectItem);
        _sensorsMenu.DropDownItems.Add(new ToolStripSeparator());
        _sensorsMenu.DropDownItems.Add(_sensorsStatusItem);
        _sensorsMenu.DropDownItems.Add(_sensorsDiagnoseItem);
        _sensorsMenu.DropDownItems.Add(_sensorsCopyDiagnosticsItem);
        _sensorsMenu.DropDownItems.Add(new ToolStripSeparator());
        _sensorsMenu.DropDownItems.Add(_pawnIoItem);

        _showBatteryItem.Click += (_, _) => ToggleItem(s => s.ShowBattery = !s.ShowBattery);
        _showBatteryDetailItem.Click += (_, _) => ToggleItem(s => s.ShowBatteryDetail = !s.ShowBatteryDetail);
        _showCpuRamItem.Click += (_, _) => ToggleItem(s => s.ShowCpuRam = !s.ShowCpuRam);
        _showGpuItem.Click += (_, _) => ToggleItem(s => s.ShowGpu = !s.ShowGpu);
        _showDiskItem.Click += (_, _) => ToggleItem(s => s.ShowDisk = !s.ShowDisk);
        _showNetworkItem.Click += (_, _) => ToggleItem(s => s.ShowNetwork = !s.ShowNetwork);
        _showTemperatureItem.Click += (_, _) => ToggleItem(s => s.ShowTemperature = !s.ShowTemperature);
        _showClockItem.Click += (_, _) => ToggleItem(s => s.ShowClock = !s.ShowClock);
        _showPowerItem.Click += (_, _) => ToggleItem(s => s.ShowPower = !s.ShowPower);
        _showFansItem.Click += (_, _) => ToggleItem(s => s.ShowFans = !s.ShowFans);
        _sensorsEnabledItem.Click += (_, _) =>
        {
            if (_settings.SensorsEnabled)
            {
                DisableSensors();
            }
            else
            {
                EnableSensors();
            }
        };
        _sensorsReconnectItem.Click += (_, _) => ReconnectSensors();
        _pawnIoItem.Click += (_, _) => ShowPawnIoHelp();
        _sensorsDiagnoseItem.Click += (_, _) => ShowSensorDiagnostics();
        _sensorsCopyDiagnosticsItem.Click += (_, _) => CopySensorDiagnostics();

        _autoStartItem.Click += (_, _) => AutoStart.SetEnabled(!AutoStart.IsEnabled());
        _showBarItem.Click += (_, _) => SetBarHidden(!_settings.BarHidden);
        _clickThroughItem.Click += (_, _) => SetClickThrough(!_settings.ClickThrough);
        _taskbarModeItem.Click += (_, _) =>
        {
            // 有多個螢幕時這一項是子選單的標題，點它只是展開，不要切換。
            if (!_taskbarModeItem.HasDropDownItems)
            {
                ChangeTaskbarMode(!_settings.TaskbarMode, screen: null);
            }
        };
        resetPosition.Click += (_, _) => ResetPosition();
        _checkUpdateItem.Click += (_, _) => _ = RunUpdateAsync(interactive: true);
        _autoUpdateItem.Click += (_, _) =>
        {
            _settings.AutoUpdate = !_settings.AutoUpdate;
            _settings.Save(_settingsPath);
        };
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
            _showTemperatureItem.Checked = _settings.ShowTemperature;
            _showClockItem.Checked = _settings.ShowClock;
            _showPowerItem.Checked = _settings.ShowPower;
            _showFansItem.Checked = _settings.ShowFans;
            _cpuTimeItem.Checked = _settings.CpuMode == CpuUsageMode.Time;
            _cpuUtilityItem.Checked = _settings.CpuMode == CpuUsageMode.Utility;
            _sensorsEnabledItem.Checked = _settings.SensorsEnabled;
            _sensorsReconnectItem.Enabled = _settings.SensorsEnabled && _sensors.State != SensorState.Starting; // 已連線時也能重來：PawnIO 是啟動後才裝的話，要重新啟動感測器服務才讀得到
            _sensorsStatusItem.Text = "狀態：" + SensorStatusText();
            _pawnIoItem.Text = SensorEnvironment.IsPawnIoInstalled() ? "PawnIO：已安裝（CPU 與主機板感測器可用）" : "安裝 PawnIO（CPU 與主機板感測器需要）…";
            _autoStartItem.Checked = AutoStart.IsEnabled();
            _showBarItem.Checked = !_settings.BarHidden;
            _clickThroughItem.Checked = _settings.ClickThrough;
            RefreshTaskbarMenu();
            resetPosition.Enabled = !_taskbarActive && !_settings.BarHidden;

            // 開發版，或不是用下載來的 exe 執行（例如用 dotnet 執行）時，不支援自動更新。
            bool canUpdate = UpdatePolicy.CanSelfUpdate(Environment.ProcessPath) && !UpdatePolicy.IsDevelopment(AppVersion.Current);
            _checkUpdateItem.Enabled = canUpdate && !_updating;
            _autoUpdateItem.Enabled = canUpdate;
            _autoUpdateItem.Checked = _settings.AutoUpdate;
        };

        _menu.Items.Add(new ToolStripMenuItem($"BatteryCPUMonitor v{Application.ProductVersion}") { Enabled = false });
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_showBarItem);
        _menu.Items.Add(showItems);
        _menu.Items.Add(_cpuModeMenu);
        _menu.Items.Add(_taskbarModeItem);
        _menu.Items.Add(_sensorsMenu);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_autoStartItem);
        _menu.Items.Add(_clickThroughItem);
        _menu.Items.Add(resetPosition);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_checkUpdateItem);
        _menu.Items.Add(_autoUpdateItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(close);
    }

    private void SetCpuMode(CpuUsageMode mode)
    {
        _settings.CpuMode = mode;
        _settings.Save(_settingsPath);
        _collector.CpuMode = mode; // 下一秒的取樣起生效
    }

    private void ToggleItem(Action<AppSettings> toggle)
    {
        toggle(_settings);
        _settings.Save(_settingsPath);
        ApplyLayout();
    }

    /// <summary>
    /// 顯示或隱藏整個橫條。隱藏時浮動橫條與工作列上的小工具都撤掉，但程式、感測器與系統匣圖示照常執行。
    /// </summary>
    private void SetBarHidden(bool hidden)
    {
        _settings.BarHidden = hidden;
        _settings.Save(_settingsPath);
        ApplyLayout();

        if (hidden)
        {
            // 橫條不見了，提醒使用者要從哪裡叫回來。
            _tray.ShowBalloonTip(
                5000,
                "橫條已隱藏",
                "程式仍在背景執行。要再顯示，請在系統匣的電池圖示上按右鍵，勾選「顯示橫條」。",
                ToolTipIcon.Info);
        }
    }

    private void SetClickThrough(bool enabled)
    {
        _settings.ClickThrough = enabled;
        _settings.Save(_settingsPath);

        UpdateStyles();  // 依 CreateParams 重新套用視窗樣式
        TopMost = true;  // 樣式更新後重新確認置頂
        ApplyLayout();   // 嵌入工作列時，由這裡把設定帶給工作列上的小工具

        if (enabled)
        {
            Opacity = RestingOpacity;

            // 開啟後橫條本身點不到了，提醒使用者之後要從系統匣操作。
            _tray.ShowBalloonTip(
                5000,
                "已開啟滑鼠穿透",
                "橫條不會再擋住滑鼠。要關閉滑鼠穿透或做其他設定，請在系統匣的電池圖示上按右鍵。",
                ToolTipIcon.Info);
        }
    }

    /// <summary>
    /// 每次開啟選單時重建「嵌入工作列」這一項：只有一個螢幕時是單純的勾選項；
    /// 有多個螢幕時變成子選單，可以選擇嵌入哪個螢幕的工作列，或維持浮動橫條。
    /// </summary>
    private void RefreshTaskbarMenu()
    {
        foreach (ToolStripItem old in _taskbarModeItem.DropDownItems.Cast<ToolStripItem>().ToList())
        {
            old.Dispose();
        }

        _taskbarModeItem.DropDownItems.Clear();

        IReadOnlyList<ScreenInfo> screens = TaskbarForm.Screens();
        if (screens.Count < 2)
        {
            _taskbarModeItem.Checked = _settings.TaskbarMode;
            return;
        }

        _taskbarModeItem.Checked = false;

        var floating = new ToolStripMenuItem("浮動橫條（不嵌入）") { Checked = !_settings.TaskbarMode };
        floating.Click += (_, _) => ChangeTaskbarMode(embed: false, screen: null);
        _taskbarModeItem.DropDownItems.Add(floating);
        _taskbarModeItem.DropDownItems.Add(new ToolStripSeparator());

        foreach (MonitorChoice choice in TaskbarTargets.Choices(screens, TaskbarForm.HasTaskbarOn, _settings.TaskbarMode, _settings.TaskbarMonitor))
        {
            var item = new ToolStripMenuItem(choice.Label) { Enabled = choice.Enabled, Checked = choice.Checked };
            ScreenInfo screen = choice.Screen;
            item.Click += (_, _) => ChangeTaskbarMode(embed: true, screen);
            _taskbarModeItem.DropDownItems.Add(item);
        }
    }

    /// <summary>切換浮動橫條與嵌入工作列；<paramref name="screen"/> 是要嵌入哪個螢幕，null 表示維持原本選的螢幕。</summary>
    private void ChangeTaskbarMode(bool embed, ScreenInfo? screen)
    {
        _settings.TaskbarMode = embed;
        if (embed && screen is ScreenInfo chosen)
        {
            _settings.TaskbarMonitor = TaskbarTargets.SettingFor(chosen);
        }

        _settings.Save(_settingsPath);
        _taskbarRetryAt = 0;
        ApplyLayout();

        if (embed && !_taskbarActive)
        {
            _tray.ShowBalloonTip(
                5000,
                "目前無法嵌入工作列",
                "找不到可用的工作列（工作列在螢幕左右兩側，或所選螢幕沒有顯示工作列時不支援）。先以浮動橫條顯示，之後會自動再試。",
                ToolTipIcon.Warning);
        }
    }

    /// <summary>確保工作列上的小工具存在、還掛著，而且掛在使用者選的那個螢幕上；做不到時回傳 false，由浮動橫條頂替。</summary>
    private bool EnsureTaskbar()
    {
        // 實際要用的螢幕：選的那個螢幕被拔掉時暫時改用主螢幕，接回來之後會自動回去。
        ScreenInfo target = TaskbarForm.ResolveTarget(_settings.TaskbarMonitor);

        if (_taskbar is { IsAttached: true } attached
            && string.Equals(attached.TargetDevice, target.DeviceName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Explorer 重新啟動後舊的小工具會失效，選的螢幕改變了也要換地方，兩種情況都丟掉重建。
        // 失敗的話隔幾秒再試，不必每秒都試。
        ReleaseTaskbar();
        if (Environment.TickCount64 < _taskbarRetryAt || !TaskbarForm.HasTaskbarOn(target))
        {
            return false;
        }

        var taskbar = new TaskbarForm(ShowMenuAtCursor);
        if (!taskbar.TryAttach(_settings.ClickThrough, _settings.TaskbarMonitor))
        {
            taskbar.Dispose();
            _taskbarRetryAt = Environment.TickCount64 + TaskbarRetryMs;
            return false;
        }

        _taskbar = taskbar;
        return true;
    }

    private void ReleaseTaskbar()
    {
        _taskbar?.Dispose();
        _taskbar = null;
    }

    // ---- 硬體感測器 ----

    /// <summary>啟動時，之前已經啟用硬體感測器的話，用通知詢問要不要現在授權；不自己跳出系統管理員權限的視窗。</summary>
    private void ScheduleSensorPrompt()
    {
        if (!_settings.SensorsEnabled)
        {
            return;
        }

        _sensorPromptTimer.Interval = SensorPromptDelayMs; // 讓「已更新」之類的通知先顯示完
        _sensorPromptTimer.Tick += (_, _) =>
        {
            _sensorPromptTimer.Stop();
            if (_settings.SensorsEnabled && _sensors.State == SensorState.Off)
            {
                _sensorPromptActive = true;
                _tray.ShowBalloonTip(
                    15000,
                    "硬體感測器需要你同意",
                    "點一下這則通知，開始授權系統管理員權限。不需要的話，到選單的「硬體感測器」取消勾選。",
                    ToolTipIcon.Info);
            }
        };
        _sensorPromptTimer.Start();
    }

    private void StartSensorsAfterConsent()
    {
        if (!_sensorPromptActive)
        {
            return;
        }

        _sensorPromptActive = false;
        _sensors.Start();
    }

    private void EnableSensors()
    {
        DialogResult answer = MessageBox.Show(
            "硬體感測器要讀取 CPU、主機板與顯示卡的感測器，這需要系統管理員權限。\n\n" +
            "程式本身仍然以一般權限執行；只有另外啟動的一個「感測器服務」會以系統管理員權限執行。" +
            "它只負責讀取數值並傳回來，不接受任何指令，程式結束時它也會跟著結束。\n\n" +
            "接下來 Windows 會詢問你是否同意。要繼續嗎？",
            DialogCaption,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        _settings.SensorsEnabled = true;
        if (!_settings.ShowTemperature && !_settings.ShowClock && !_settings.ShowPower && !_settings.ShowFans)
        {
            _settings.ShowTemperature = true; // 剛啟用就什麼都看不到會以為壞了，先打開溫度
        }

        _settings.Save(_settingsPath);
        _sensorPromptActive = false;
        _sensors.Start();
        ApplyLayout();
    }

    private void DisableSensors()
    {
        _settings.SensorsEnabled = false;
        _settings.Save(_settingsPath);
        _sensorPromptActive = false;
        _sensors.Stop();
        ApplyLayout();
    }

    private void ReconnectSensors()
    {
        _sensorPromptActive = false;
        _sensors.Stop(); // 已連線時先放掉舊的服務；沒連線時等於什麼都沒做
        _sensors.Start();
    }

    /// <summary>感測器服務的狀態在背景執行緒上改變，回到畫面執行緒處理。</summary>
    private void OnSensorsChanged()
    {
        try
        {
            BeginInvoke(HandleSensorsChanged);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // 程式正在關閉。
        }
    }

    private void HandleSensorsChanged()
    {
        if (IsDisposed)
        {
            return;
        }

        switch (_sensors.State)
        {
            case SensorState.Declined:
                _settings.SensorsEnabled = false;
                _settings.Save(_settingsPath);
                _tray.ShowBalloonTip(6000, "硬體感測器沒有啟用", _sensors.Status, ToolTipIcon.Info);
                break;

            case SensorState.Failed:
                _tray.ShowBalloonTip(8000, "硬體感測器無法使用", _sensors.Status, ToolTipIcon.Warning);
                break;

            case SensorState.Connected when _sensors.Status.Length > 0:
                _tray.ShowBalloonTip(8000, "硬體感測器", _sensors.Status, ToolTipIcon.Warning); // 連上了，但感測器服務初始化失敗
                break;
        }

        ApplyLayout();
    }

    private string SensorStatusText() => _sensors.State switch
    {
        SensorState.Off => _settings.SensorsEnabled ? "尚未連線（按「重新連線」）" : "未啟用",
        SensorState.Starting => "等待授權與初始化…",
        SensorState.Connected => ConnectedStatusText(),
        _ => _sensors.Status,
    };

    private string ConnectedStatusText()
    {
        if (_sensors.Status.Length > 0)
        {
            return _sensors.Status;
        }

        Diagnosis diagnosis = SensorDiagnosis.Evaluate(CurrentDiagnosisInput());
        return diagnosis.Kind switch
        {
            DiagnosisKind.Working => "已連線",
            DiagnosisKind.Waiting => "已連線，等待數值…",
            _ => $"已連線，但沒有取得 CPU 溫度：{diagnosis.Headline}（見「診斷資訊」）",
        };
    }

    private DiagnosisInput CurrentDiagnosisInput() =>
        new(_sensors.State, _sensors.Status, _sensors.Latest, _sensors.HostDiagnostics, SensorEnvironment.QueryPawnIoService(), SensorEnvironment.IsPawnIoInstalled());

    private SensorDiagnosticsView BuildSensorDiagnostics() =>
        SensorDiagnosticsReport.Build(
            CurrentDiagnosisInput(),
            new ReportEnvironment(
                DateTimeOffset.Now,
                AppVersion.CurrentText,
                SensorEnvironment.DescribeOperatingSystem(),
                SensorEnvironment.ReadProcessor()));

    private void ShowSensorDiagnostics()
    {
        if (_diagnosticsForm is { IsDisposed: false } open)
        {
            open.Activate();
            return;
        }

        _diagnosticsForm = new SensorDiagnosticsForm(BuildSensorDiagnostics, _icon);
        _diagnosticsForm.FormClosed += (_, _) => _diagnosticsForm = null;
        _diagnosticsForm.Show();
    }

    /// <summary>不開視窗，直接把診斷資訊放進剪貼簿。</summary>
    private void CopySensorDiagnostics()
    {
        SensorDiagnosticsView view = BuildSensorDiagnostics();
        if (SensorDiagnosticsForm.TryCopy(view.Text))
        {
            _tray.ShowBalloonTip(5000, "已複製診斷資訊", $"{view.Diagnosis.Headline}。可以直接貼上。", ToolTipIcon.Info);
        }
        else
        {
            _tray.ShowBalloonTip(6000, "複製失敗", "剪貼簿被其他程式占用，請稍後再試，或從「診斷資訊…」視窗複製。", ToolTipIcon.Warning);
        }
    }

    /// <summary>說明 CPU 與主機板的感測器為什麼需要 PawnIO，並詢問要不要開啟官方網站。程式不會替使用者安裝驅動程式。</summary>
    private void ShowPawnIoHelp()
    {
        string state = SensorEnvironment.IsPawnIoInstalled()
            ? "這台電腦已經偵測到 PawnIO。\n\n"
            : "這台電腦還沒有偵測到 PawnIO。\n\n";

        DialogResult answer = MessageBox.Show(
            state +
            "CPU 與主機板的感測器（CPU 溫度、CPU 功耗、風扇）必須透過核心驅動程式才讀得到，" +
            "這裡使用的是 PawnIO（由 namazso 開發的簽章驅動程式）。顯示卡的溫度、功耗與頻率不需要它。\n\n" +
            "本程式不會替你安裝驅動程式，要不要裝由你決定：\n" +
            "• 網站：" + PawnIoDetector.OfficialSite + "\n" +
            "• 或在系統管理員的命令提示字元執行：" + PawnIoDetector.WingetCommand + "\n\n" +
            "要現在開啟 PawnIO 的官方網站嗎？",
            DialogCaption,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        if (answer == DialogResult.Yes)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PawnIoDetector.OfficialSite) { UseShellExecute = true })?.Dispose();
        }
    }

    // ---- 更新 ----

    private UpdateController Updater => _updater ??= new UpdateController(
        UpdateHttp.Client,
        UpdateHttp.NoRedirectClient,
        Environment.ProcessPath ?? string.Empty,
        AppVersion.Current,
        AppVariantInfo.Current,
        SelfCheckRunner.RunAsync,
        UpdateRelauncher.Relaunch);

    /// <summary>
    /// 檢查更新，有新版本就下載、安裝，並重新啟動。
    /// 手動檢查（<paramref name="interactive"/>）會顯示結果並在安裝前徵求同意；自動檢查只在真的要更新時用通知告知，其餘靜悄悄。
    /// </summary>
    private async Task RunUpdateAsync(bool interactive)
    {
        if (_updating || IsDisposed)
        {
            return;
        }

        _updating = true;
        try
        {
            UpdateCheck check = await Updater.CheckAsync(CancellationToken.None);
            if (check.Status != CheckStatus.Available)
            {
                if (interactive)
                {
                    string message = check.Status == CheckStatus.UpToDate
                        ? $"目前的 v{AppVersion.CurrentText} 已經是最新版本。"
                        : check.Message;
                    MessageBox.Show(message, DialogCaption, MessageBoxButtons.OK, check.Status == CheckStatus.Failed ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                }

                return;
            }

            LatestRelease release = check.Release!;
            string latest = AppVersion.Display(release.Version);

            if (interactive)
            {
                string notes = release.Notes.Length > 0 ? "\n\n" + release.Notes : string.Empty;
                DialogResult answer = MessageBox.Show(
                    $"發現新版本 v{latest}（目前 v{AppVersion.CurrentText}）。{notes}\n\n要現在下載並安裝嗎？安裝完成後程式會自動重新啟動。",
                    DialogCaption,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                {
                    return;
                }
            }

            _tray.ShowBalloonTip(4000, "正在更新", $"正在下載並安裝 v{latest}，完成後會自動重新啟動。", ToolTipIcon.Info);
            InstallResult result = await Updater.InstallAsync(release, progress: null, CancellationToken.None);

            if (result.Status == InstallStatus.Installed && result.Restarted)
            {
                Close(); // 新版本已經啟動，會等這個程式結束後接手
                return;
            }

            string text = result.Status == InstallStatus.Installed
                ? $"已更新到 v{latest}，但無法自動重新啟動，請手動重新開啟程式。"
                : $"更新失敗：{result.Message}";
            if (interactive)
            {
                MessageBox.Show(text, DialogCaption, MessageBoxButtons.OK, result.Status == InstallStatus.Installed ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            else
            {
                _tray.ShowBalloonTip(8000, "自動更新沒有成功", text, ToolTipIcon.Warning);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 更新是附加功能，無論哪裡出了意料之外的狀況，都不能讓監控程式本身跟著當掉。
            if (interactive)
            {
                MessageBox.Show($"檢查更新時發生未預期的錯誤：{ex.Message}", DialogCaption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>在工作列的小工具上按右鍵時顯示選單。</summary>
    private void ShowMenuAtCursor()
    {
        // 先把自己設為前景，選單才會在使用者點別處時自動關閉（系統匣圖示的選單也是這樣做）。
        SetForegroundWindow(Handle);
        _menu.Show(Cursor.Position);
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

    /// <summary>
    /// 依目前的數值與設定更新畫面：嵌入工作列時交給工作列上的小工具，否則重新排版浮動橫條。
    /// </summary>
    private void ApplyLayout()
    {
        IReadOnlyList<BarColumn> columns = BarContent.Build(_snapshot, _settings.Visible);

        // 隱藏時連工作列上的小工具也撤掉；再顯示時會重新嵌入。
        bool useTaskbar = !_settings.BarHidden && _settings.TaskbarMode && EnsureTaskbar();
        if (useTaskbar)
        {
            _taskbar!.UpdateContent(columns, _settings.ClickThrough);
        }
        else
        {
            ReleaseTaskbar();
        }

        _taskbarActive = useTaskbar;

        bool showFloating = !_settings.BarHidden && !useTaskbar;
        if (_barVisible != showFloating)
        {
            _barVisible = showFloating;
            Visible = showFloating;
        }

        if (!showFloating)
        {
            return;
        }

        int rowHeight = TextRenderer.MeasureText("電0", _font, Unbounded, TextFlags).Height;
        _layout = GridLayout.Compute(columns, MeasureWidth, rowHeight, GridSpacing.ForDpi(_fontDpi));

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

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    private static Icon LoadIcon()
    {
        using Stream? stream = typeof(BarForm).Assembly.GetManifestResourceStream("app.ico");
        return stream is null
            ? (Icon)SystemIcons.Application.Clone()
            : new Icon(stream, SystemInformation.SmallIconSize);
    }
}
