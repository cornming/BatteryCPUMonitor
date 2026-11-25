using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using BatteryCPUMonitor.Sensors;

namespace BatteryCPUMonitor;

/// <summary>
/// 硬體感測器的診斷視窗：套用統一的 Claude 暖曜黑／暖象牙白主題，
/// 頂部以狀態膠囊顯示結論，下方以等寬字體呈現完整診斷內容，並支援一鍵複製。
/// </summary>
internal sealed class SensorDiagnosticsForm : Form
{
    private const int CopiedLabelMs = 2500;

    private readonly Func<SensorDiagnosticsView> _build;
    private readonly Font _uiFont = ThemeFontSet.CreateDialogUiFont(9.5f);
    private readonly Font _headlineFont = ThemeFontSet.CreateDialogUiFont(10f, FontStyle.Bold);
    private readonly Font _noteFont = ThemeFontSet.CreateDialogUiFont(8.75f);
    private readonly Font _monoFont = ThemeFontSet.CreateDialogMonoFont(9.5f);

    private readonly Panel _bannerCard = new()
    {
        Dock = DockStyle.Top,
        Height = 46,
        Padding = new Padding(12, 8, 12, 8),
    };

    private readonly Label _headline = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        Padding = new Padding(18, 0, 8, 0),
    };

    private readonly Panel _textFrame = new()
    {
        Dock = DockStyle.Fill,
        Padding = new Padding(1),
    };

    private readonly Panel _textInset = new()
    {
        Dock = DockStyle.Fill,
        Padding = new Padding(10, 8, 6, 8),
    };

    private readonly TextBox _text = new()
    {
        Multiline = true,
        ReadOnly = true,
        WordWrap = false,
        BorderStyle = BorderStyle.None,
        ScrollBars = ScrollBars.Both,
        Dock = DockStyle.Fill,
    };

    private readonly Label _note = new()
    {
        Dock = DockStyle.Bottom,
        Height = 28,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(2, 4, 2, 0),
        Text = "內容只有 CPU 與驅動程式的資訊，不含主機板序號；貼到公開的地方之前，還是請自己看過一遍。",
    };

    private readonly FlowLayoutPanel _buttons = new()
    {
        Dock = DockStyle.Bottom,
        FlowDirection = FlowDirection.RightToLeft,
        AutoSize = true,
        Padding = new Padding(0, 10, 0, 0),
    };

    private readonly Button _copy = CreateFlatButton("複製診斷資訊");
    private readonly Button _refresh = CreateFlatButton("重新整理");
    private readonly Button _close = CreateFlatButton("關閉");
    private readonly System.Windows.Forms.Timer _copiedTimer = new() { Interval = CopiedLabelMs };

    private ThemePalette _palette;
    private SensorDiagnosticsView _current;
    private Color _statusColor;

    public SensorDiagnosticsForm(Func<SensorDiagnosticsView> build, Icon icon, ThemePalette? palette = null)
    {
        _build = build;
        _palette = palette ?? BarTheme.Dark;
        _current = build();
        _statusColor = _palette.Neutral;

        Text = "硬體感測器診斷";
        Icon = icon;
        Font = _uiFont;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(800, 580);
        MinimumSize = new Size(540, 380);
        Padding = new Padding(14);
        CancelButton = _close;
        ShowInTaskbar = true;

        _headline.Font = _headlineFont;
        _text.Font = _monoFont;
        _note.Font = _noteFont;
        _close.DialogResult = DialogResult.Cancel;

        _copy.Font = _uiFont;
        _refresh.Font = _uiFont;
        _close.Font = _uiFont;
        _copy.Margin = new Padding(0, 0, 8, 0);
        _refresh.Margin = new Padding(0, 0, 8, 0);
        _close.Margin = new Padding(0);

        _bannerCard.Controls.Add(_headline);
        _bannerCard.Paint += PaintBannerCard;
        _headline.Paint += PaintStatusDot;

        _textInset.Controls.Add(_text);
        _textFrame.Controls.Add(_textInset);

        var bodySpacer = new Panel { Dock = DockStyle.Top, Height = 10 };

        _buttons.Controls.Add(_close);
        _buttons.Controls.Add(_refresh);
        _buttons.Controls.Add(_copy);

        // 加入順序決定停靠順序：最後加入的最先佔位，所以文字框容器最先加。
        Controls.Add(_textFrame);
        Controls.Add(bodySpacer);
        Controls.Add(_bannerCard);
        Controls.Add(_note);
        Controls.Add(_buttons);

        _copy.Click += (_, _) => CopyCurrent();
        _refresh.Click += (_, _) => Reload();
        _copiedTimer.Tick += (_, _) =>
        {
            _copiedTimer.Stop();
            _copy.Text = "複製診斷資訊";
        };

        ApplyTheme(_palette);
        Render(_current);
    }

    /// <summary>同步套用最新的深淺主題色票（若視窗開著時使用者切換外觀，也會即時更新）。</summary>
    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        BackColor = palette.DialogBackground;
        ForeColor = palette.DialogText;

        _bannerCard.BackColor = palette.DialogCard;
        _headline.BackColor = Color.Transparent;

        _textFrame.BackColor = palette.Border;
        _textInset.BackColor = palette.DialogCard;
        _text.BackColor = palette.DialogCard;
        _text.ForeColor = palette.DialogText;

        _note.BackColor = palette.DialogBackground;
        _note.ForeColor = palette.DialogMuted;
        _buttons.BackColor = palette.DialogBackground;

        StylePrimaryButton(_copy, palette);
        StyleSecondaryButton(_refresh, palette);
        StyleSecondaryButton(_close, palette);

        UpdateStatusColors(_current.Diagnosis.Kind);
        if (IsHandleCreated)
        {
            WindowChrome.TryApplyTitleBarTheme(Handle, dark: !palette.IsLight);
        }

        _bannerCard.Invalidate();
        _headline.Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowChrome.TryApplyTitleBarTheme(Handle, dark: !_palette.IsLight);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _copiedTimer.Dispose();
            _uiFont.Dispose();
            _headlineFont.Dispose();
            _noteFont.Dispose();
            _monoFont.Dispose();
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
        UpdateStatusColors(view.Diagnosis.Kind);

        _text.Text = view.Text;
        _text.SelectionStart = 0;
        _text.SelectionLength = 0;

        _bannerCard.Invalidate();
        _headline.Invalidate();
    }

    private void UpdateStatusColors(DiagnosisKind kind)
    {
        _statusColor = kind switch
        {
            DiagnosisKind.Working => _palette.Good,
            DiagnosisKind.Waiting => _palette.Info,
            _ => _palette.Warn,
        };

        _headline.ForeColor = _statusColor;
    }

    private void PaintBannerCard(object? sender, PaintEventArgs e)
    {
        var rect = new Rectangle(0, 0, _bannerCard.Width - 1, _bannerCard.Height - 1);
        using (var tintBrush = new SolidBrush(Color.FromArgb(_palette.IsLight ? 24 : 28, _statusColor)))
        {
            e.Graphics.FillRectangle(tintBrush, rect);
        }

        // 左側 3px 語意色條 + 外圍細邊框
        using (var accentBrush = new SolidBrush(_statusColor))
        {
            e.Graphics.FillRectangle(accentBrush, 0, 0, 3, _bannerCard.Height);
        }

        using var borderPen = new Pen(Color.FromArgb(_palette.IsLight ? 90 : 110, _statusColor), 1f);
        e.Graphics.DrawRectangle(borderPen, rect);
    }

    private void PaintStatusDot(object? sender, PaintEventArgs e)
    {
        SmoothingMode oldMode = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            const float diameter = 8f;
            float y = (_headline.Height - diameter) / 2f;
            using var brush = new SolidBrush(_statusColor);
            e.Graphics.FillEllipse(brush, 4f, y, diameter, diameter);
        }
        finally
        {
            e.Graphics.SmoothingMode = oldMode;
        }
    }

    private static Button CreateFlatButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        Cursor = Cursors.Hand,
        Padding = new Padding(12, 4, 12, 4),
    };

    private static void StylePrimaryButton(Button button, ThemePalette palette)
    {
        button.BackColor = palette.Accent;
        button.ForeColor = Color.FromArgb(250, 249, 245);
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = palette.Accent;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(
            Math.Min(255, palette.Accent.R + 14),
            Math.Min(255, palette.Accent.G + 12),
            Math.Min(255, palette.Accent.B + 10));
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(
            Math.Max(0, palette.Accent.R - 14),
            Math.Max(0, palette.Accent.G - 12),
            Math.Max(0, palette.Accent.B - 10));
    }

    private static void StyleSecondaryButton(Button button, ThemePalette palette)
    {
        button.BackColor = palette.DialogButtonBg;
        button.ForeColor = palette.DialogText;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = palette.Border;
        button.FlatAppearance.MouseOverBackColor = palette.DialogButtonHover;
        button.FlatAppearance.MouseDownBackColor = palette.Border;
    }
}
