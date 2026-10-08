using System.Runtime.InteropServices;
using BatteryCPUMonitor.Sensors;

namespace BatteryCPUMonitor;

/// <summary>
/// 硬體感測器的診斷視窗：最上面一條是結論，下面是完整的診斷資訊，可以一鍵複製。
/// 內容由呼叫端提供，按「重新整理」會重新取一次。
/// </summary>
internal sealed class SensorDiagnosticsForm : Form
{
    private const int CopiedLabelMs = 2500;

    private static readonly Color WorkingColor = Color.FromArgb(50, 215, 75);
    private static readonly Color WaitingColor = Color.FromArgb(200, 200, 200);
    private static readonly Color ProblemColor = Color.FromArgb(255, 214, 10);

    private readonly Func<SensorDiagnosticsView> _build;
    private readonly Label _headline = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        ForeColor = Color.Black,
        Padding = new Padding(10, 0, 10, 0),
    };

    private readonly TextBox _text = new()
    {
        Multiline = true,
        ReadOnly = true,
        WordWrap = false,
        ScrollBars = ScrollBars.Both,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 10f),
        BackColor = SystemColors.Window,
    };

    private readonly Button _copy = new() { Text = "複製診斷資訊", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    private readonly Button _refresh = new() { Text = "重新整理", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    private readonly Button _close = new() { Text = "關閉", AutoSize = true, Padding = new Padding(8, 2, 8, 2), DialogResult = DialogResult.Cancel };
    private readonly System.Windows.Forms.Timer _copiedTimer = new() { Interval = CopiedLabelMs };

    private SensorDiagnosticsView _current;

    public SensorDiagnosticsForm(Func<SensorDiagnosticsView> build, Icon icon)
    {
        _build = build;
        _current = build();

        Text = "硬體感測器診斷";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(780, 560);
        MinimumSize = new Size(520, 360);
        CancelButton = _close;
        ShowInTaskbar = true;

        var banner = new Panel { Dock = DockStyle.Top, Height = 40 };
        banner.Controls.Add(_headline);

        var note = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(10, 0, 10, 0),
            Text = "內容只有 CPU 與驅動程式的資訊，不含主機板序號；貼到公開的地方之前，還是請自己看過一遍。",
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(_close);
        buttons.Controls.Add(_refresh);
        buttons.Controls.Add(_copy);

        // 加入順序決定停靠順序：最後加入的最先佔位，所以文字框要最先加。
        Controls.Add(_text);
        Controls.Add(note);
        Controls.Add(buttons);
        Controls.Add(banner);

        _copy.Click += (_, _) => CopyCurrent();
        _refresh.Click += (_, _) => Reload();
        _copiedTimer.Tick += (_, _) =>
        {
            _copiedTimer.Stop();
            _copy.Text = "複製診斷資訊";
        };

        Render(_current);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _copiedTimer.Dispose();
            _text.Font.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// 把文字放進剪貼簿。剪貼簿偶爾會被別的程式占著，所以重試幾次；還是失敗就回傳 false，不丟出例外。
    /// </summary>
    public static bool TryCopy(string text)
    {
        try
        {
            Clipboard.SetDataObject(text, copy: true, retryTimes: 5, retryDelay: 100);
            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    private void Reload()
    {
        _current = _build();
        Render(_current);
    }

    private void CopyCurrent()
    {
        // 複製的是當下最新的內容，不是視窗上次顯示的。
        Reload();

        _copy.Text = TryCopy(_current.Text) ? "已複製 ✓" : "複製失敗，請全選後自行複製";
        _copiedTimer.Stop();
        _copiedTimer.Start();
    }

    private void Render(SensorDiagnosticsView view)
    {
        _headline.Text = view.Diagnosis.Headline;
        _headline.BackColor = view.Diagnosis.Kind switch
        {
            DiagnosisKind.Working => WorkingColor,
            DiagnosisKind.Waiting => WaitingColor,
            _ => ProblemColor,
        };

        _text.Text = view.Text;
        _text.SelectionStart = 0;
        _text.SelectionLength = 0;
    }
}
