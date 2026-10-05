using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace MdViewer;

internal sealed record Palette(
    bool IsDark,
    Color Chrome,
    Color Menu,
    Color Hover,
    Color Border,
    Color Text,
    Color TextDisabled,
    Color EditorBack,
    Color EditorText,
    Color PageBack)
{
    public static readonly Palette Light = new(
        IsDark: false,
        Chrome: SystemColors.Control,
        Menu: SystemColors.Menu,
        Hover: SystemColors.Highlight,
        Border: SystemColors.ControlDark,
        Text: SystemColors.ControlText,
        TextDisabled: SystemColors.GrayText,
        EditorBack: Color.White,
        EditorText: Color.FromArgb(31, 35, 40),
        PageBack: Color.White);

    public static readonly Palette Dark = new(
        IsDark: true,
        Chrome: Color.FromArgb(32, 32, 32),
        Menu: Color.FromArgb(43, 43, 43),
        Hover: Color.FromArgb(65, 65, 65),
        Border: Color.FromArgb(70, 70, 70),
        Text: Color.FromArgb(232, 232, 232),
        TextDisabled: Color.FromArgb(128, 128, 128),
        EditorBack: Color.FromArgb(30, 31, 34),
        EditorText: Color.FromArgb(212, 212, 212),
        PageBack: Color.FromArgb(30, 31, 34));
}

internal static class Theme
{
    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    public static ToolStripRenderer CreateRenderer(Palette p) =>
        p.IsDark ? new DarkRenderer(p) : new ToolStripProfessionalRenderer(new LightColorTable()) { RoundedEdges = false };

    /// <summary>Applies text colours to a strip and all nested drop-down items.</summary>
    public static void ApplyToItems(ToolStripItemCollection items, Palette p)
    {
        foreach (ToolStripItem item in items)
        {
            item.ForeColor = p.Text;
            if (item is ToolStripTextBox tb)
            {
                tb.BackColor = p.IsDark ? p.EditorBack : SystemColors.Window;
                tb.ForeColor = p.IsDark ? p.EditorText : SystemColors.WindowText;
            }
            if (item is ToolStripDropDownItem dd && dd.HasDropDownItems)
            {
                dd.DropDown.BackColor = p.Menu;
                ApplyToItems(dd.DropDownItems, p);
            }
        }
    }

    private sealed class LightColorTable : ProfessionalColorTable
    {
        public LightColorTable() => UseSystemColors = true;
        public override Color MenuStripGradientBegin => SystemColors.Control;
        public override Color MenuStripGradientEnd => SystemColors.Control;
        public override Color ToolStripGradientBegin => SystemColors.Control;
        public override Color ToolStripGradientMiddle => SystemColors.Control;
        public override Color ToolStripGradientEnd => SystemColors.Control;
        public override Color ToolStripBorder => SystemColors.Control;
        public override Color StatusStripGradientBegin => SystemColors.Control;
        public override Color StatusStripGradientEnd => SystemColors.Control;
    }

    private sealed class DarkColorTable(Palette p) : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => p.Chrome;
        public override Color MenuStripGradientEnd => p.Chrome;
        public override Color ToolStripGradientBegin => p.Chrome;
        public override Color ToolStripGradientMiddle => p.Chrome;
        public override Color ToolStripGradientEnd => p.Chrome;
        public override Color ToolStripBorder => p.Chrome;
        public override Color ToolStripDropDownBackground => p.Menu;
        public override Color ImageMarginGradientBegin => p.Menu;
        public override Color ImageMarginGradientMiddle => p.Menu;
        public override Color ImageMarginGradientEnd => p.Menu;
        public override Color MenuBorder => p.Border;
        public override Color MenuItemBorder => p.Hover;
        public override Color MenuItemSelected => p.Hover;
        public override Color MenuItemSelectedGradientBegin => p.Hover;
        public override Color MenuItemSelectedGradientEnd => p.Hover;
        public override Color MenuItemPressedGradientBegin => p.Menu;
        public override Color MenuItemPressedGradientMiddle => p.Menu;
        public override Color MenuItemPressedGradientEnd => p.Menu;
        public override Color SeparatorDark => p.Border;
        public override Color SeparatorLight => p.Border;
        public override Color StatusStripGradientBegin => p.Chrome;
        public override Color StatusStripGradientEnd => p.Chrome;
        public override Color ButtonSelectedBorder => p.Hover;
        public override Color ButtonSelectedHighlight => p.Hover;
        public override Color ButtonSelectedGradientBegin => p.Hover;
        public override Color ButtonSelectedGradientMiddle => p.Hover;
        public override Color ButtonSelectedGradientEnd => p.Hover;
        public override Color ButtonPressedBorder => p.Border;
        public override Color ButtonPressedGradientBegin => p.Border;
        public override Color ButtonPressedGradientMiddle => p.Border;
        public override Color ButtonPressedGradientEnd => p.Border;
        public override Color ButtonCheckedGradientBegin => p.Hover;
        public override Color ButtonCheckedGradientMiddle => p.Hover;
        public override Color ButtonCheckedGradientEnd => p.Hover;
        public override Color ButtonCheckedHighlight => p.Hover;
        public override Color CheckBackground => p.Hover;
        public override Color CheckSelectedBackground => p.Hover;
        public override Color CheckPressedBackground => p.Hover;
    }

    private sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        private readonly Palette p;

        public DarkRenderer(Palette palette) : base(new DarkColorTable(palette))
        {
            p = palette;
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? p.Text : p.TextDisabled;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = p.Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // The stock check glyph is black; draw a light one instead.
            var r = e.ImageRectangle;
            using var bg = new SolidBrush(p.Hover);
            e.Graphics.FillRectangle(bg, r);
            var old = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float w = Math.Max(1.5f, r.Width / 9f);
            using var pen = new Pen(p.Text, w);
            e.Graphics.DrawLines(pen, new[]
            {
                new PointF(r.Left + r.Width * 0.22f, r.Top + r.Height * 0.52f),
                new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.72f),
                new PointF(r.Left + r.Width * 0.78f, r.Top + r.Height * 0.30f),
            });
            e.Graphics.SmoothingMode = old;
        }
    }
}
