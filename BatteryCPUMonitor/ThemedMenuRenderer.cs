using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BatteryCPUMonitor;

/// <summary>
/// 符合 Claude 暖曜黑／暖象牙白設計語言的右鍵選單渲染器：
/// 取代 WinForms 預設系統灰選單，包含陶土橘選取焦點、向量打勾與柔和分隔線。
/// </summary>
internal sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly ThemePalette _palette;

    public ThemedMenuRenderer(ThemePalette palette)
        : base(new ThemedMenuColorTable(palette))
    {
        _palette = palette;
        RoundedEdges = false;
    }

    /// <summary>套用本渲染器與一致的字色、底色到整個選單與所有子選單。</summary>
    public static void Apply(ContextMenuStrip menu, ThemePalette palette)
    {
        var renderer = new ThemedMenuRenderer(palette);
        menu.Renderer = renderer;
        menu.BackColor = palette.MenuBackground;
        menu.ForeColor = palette.MenuText;
        ApplyItems(menu.Items, palette, renderer);
    }

    public static void ApplyItems(ToolStripItemCollection items, ThemePalette palette, ToolStripRenderer? renderer = null)
    {
        foreach (ToolStripItem item in items)
        {
            item.BackColor = palette.MenuBackground;
            item.ForeColor = item.Enabled ? palette.MenuText : palette.MenuMuted;

            if (item is ToolStripMenuItem menuItem)
            {
                if (renderer is not null)
                {
                    menuItem.DropDown.Renderer = renderer;
                }

                menuItem.DropDown.BackColor = palette.MenuBackground;
                menuItem.DropDown.ForeColor = palette.MenuText;
                ApplyItems(menuItem.DropDownItems, palette, renderer);
            }
        }
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(_palette.MenuBackground);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(_palette.MenuBorder, 1f);
        e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(_palette.MenuBackground);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Enabled || !e.Item.Selected)
        {
            return;
        }

        var rect = new Rectangle(4, 1, Math.Max(1, e.Item.Width - 8), Math.Max(1, e.Item.Height - 2));
        using (var bgBrush = new SolidBrush(_palette.MenuHover))
        {
            e.Graphics.FillRectangle(bgBrush, rect);
        }

        // 左側 2px 陶土橘細指示條，與橫條語言呼應
        using var accentBrush = new SolidBrush(_palette.Accent);
        e.Graphics.FillRectangle(accentBrush, rect.X, rect.Y + 3, 2, Math.Max(4, rect.Height - 6));
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? _palette.MenuText : _palette.MenuMuted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        Rectangle box = e.ImageRectangle;
        SmoothingMode oldMode = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            // 淡陶土橘底圓框 + 俐落向量勾
            var badge = new Rectangle(box.X + 1, box.Y + 1, Math.Max(12, box.Width - 2), Math.Max(12, box.Height - 2));
            using (var bgBrush = new SolidBrush(Color.FromArgb(_palette.IsLight ? 36 : 46, _palette.Accent)))
            {
                e.Graphics.FillEllipse(bgBrush, badge);
            }

            Color stroke = e.Item.Enabled ? _palette.Accent : _palette.MenuMuted;
            using var pen = new Pen(stroke, 1.8f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };

            float cx = badge.X + badge.Width * 0.5f;
            float cy = badge.Y + badge.Height * 0.5f;
            e.Graphics.DrawLines(pen,
            [
                new PointF(cx - 3.2f, cy + 0.2f),
                new PointF(cx - 0.8f, cy + 2.6f),
                new PointF(cx + 3.4f, cy - 2.4f),
            ]);
        }
        finally
        {
            e.Graphics.SmoothingMode = oldMode;
        }
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(_palette.MenuBorder, 1f);
        e.Graphics.DrawLine(pen, 10, y, Math.Max(10, e.Item.Width - 10), y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled == true ? _palette.MenuMuted : _palette.Divider;
        base.OnRenderArrow(e);
    }

    private sealed class ThemedMenuColorTable(ThemePalette palette) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => palette.MenuBackground;
        public override Color MenuBorder => palette.MenuBorder;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => palette.MenuHover;
        public override Color MenuItemSelectedGradientBegin => palette.MenuHover;
        public override Color MenuItemSelectedGradientEnd => palette.MenuHover;
        public override Color ImageMarginGradientBegin => palette.MenuBackground;
        public override Color ImageMarginGradientMiddle => palette.MenuBackground;
        public override Color ImageMarginGradientEnd => palette.MenuBackground;
        public override Color SeparatorDark => palette.MenuBorder;
        public override Color SeparatorLight => palette.MenuBackground;
        public override Color CheckBackground => Color.Transparent;
        public override Color CheckSelectedBackground => Color.Transparent;
        public override Color CheckPressedBackground => Color.Transparent;
    }
}
