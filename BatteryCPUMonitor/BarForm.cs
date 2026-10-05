using System.Runtime.InteropServices;
using BatteryCPUMonitor.Metrics;

namespace BatteryCPUMonitor;

/// <summary>
/// 置頂、無邊框的資訊橫條：顯示電量、CPU 與記憶體使用率。
/// 左鍵拖曳可移動，滑鼠移入時放大，右鍵開啟選單。
/// </summary>
internal sealed class BarForm : Form
{
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;

    private const float NormalFontSize = 9f;
    private const float HoverFontSize = 29f;
    private const int FirstRefreshMs = 300;
    private const int RefreshMs = 1000;

    private readonly Label _label;
    private readonly Font _normalFont = new(FontFamily.GenericSansSerif, NormalFontSize, FontStyle.Bold);
    private readonly Font _hoverFont = new(FontFamily.GenericSansSerif, HoverFontSize, FontStyle.Bold);
    private readonly ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly CpuSampler _cpu = new();

    /// <summary>使用者拖曳後記下的錨點（橫條底邊中點）；沒拖過就是 null，跟著預設位置走。</summary>
    private Point? _userAnchor;

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

        var close = new ToolStripMenuItem("關閉");
        close.Click += (_, _) => Close();
        _menu.Items.Add(new ToolStripMenuItem($"BatteryCPUMonitor v{Application.ProductVersion}") { Enabled = false });
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(close);
        ContextMenuStrip = _menu;

        _label = new Label
        {
            AutoSize = true,
            Location = Point.Empty,
            Margin = Padding.Empty,
            Font = _normalFont,
            ForeColor = Color.Lime,
            BackColor = Color.Black,
            ContextMenuStrip = _menu,
        };
        _label.MouseDown += OnBarMouseDown;
        _label.MouseEnter += (_, _) => SetFont(_hoverFont);
        _label.MouseLeave += (_, _) => SetFont(_normalFont);
        Controls.Add(_label);
        MouseDown += OnBarMouseDown;

        _timer.Interval = FirstRefreshMs;
        _timer.Tick += (_, _) =>
        {
            _timer.Interval = RefreshMs;
            RefreshMetrics();
        };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RefreshMetrics();
        _timer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _cpu.Dispose();
            _menu.Dispose();
            _normalFont.Dispose();
            _hoverFont.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>每秒執行一次。所有讀取都是立即回傳，不會卡住畫面。</summary>
    private void RefreshMetrics()
    {
        double? cpu = _cpu.Sample();
        string text = BarText.Format(BatteryReader.Read(), cpu, MemoryReader.ReadUsedPercent());
        var (r, g, b) = BarText.ColorForCpu(cpu);

        if (_label.Text != text)
        {
            _label.Text = text;
        }

        _label.ForeColor = Color.FromArgb(r, g, b);
        ApplyLayout();
    }

    private void SetFont(Font font)
    {
        if (!ReferenceEquals(_label.Font, font))
        {
            _label.Font = font;
            ApplyLayout();
        }
    }

    /// <summary>讓視窗大小貼合文字，並依錨點重新擺放。</summary>
    private void ApplyLayout()
    {
        Size size = _label.PreferredSize;

        Point anchor;
        Rectangle workingArea;
        if (_userAnchor is Point userAnchor)
        {
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
    }

    private void OnBarMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        Point before = Location;

        // 交給 Windows 當成「拖曳標題列」處理；拖曳結束後 SendMessage 才會返回。
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);

        if (Location != before)
        {
            _userAnchor = BarPlacement.AnchorOf(Bounds);
            ApplyLayout();
        }
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();
}
