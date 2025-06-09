using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Messenger
{
    public class RoundedContextMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly int cornerRadius = 8;
        private readonly Color menuBackgroundColor = Color.FromArgb(250, 250, 250);
        private readonly Color itemHoverColor = Color.FromArgb(0, 190, 255);
        private readonly Color itemPressedColor = Color.FromArgb(72, 120, 255);
        private readonly Color textColor = Color.FromArgb(60, 60, 60);
        private readonly Color separatorColor = Color.FromArgb(230, 230, 230);

        public RoundedContextMenuRenderer() : base(new ModernColorTable())
        {
            this.RoundedEdges = true; // Bật các cạnh bo tròn
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality; // Sử dụng HighQuality để giảm răng cưa
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // Tạo hình chữ nhật bo tròn cho nền
            Rectangle bounds = new Rectangle(0, 0, e.ToolStrip.Width, e.ToolStrip.Height);
            using (var path = GetRoundedRect(e.Graphics, bounds, cornerRadius))
            {
                // Cắt vùng vẽ theo hình chữ nhật bo tròn
                g.SetClip(path);
                using (var brush = new SolidBrush(menuBackgroundColor))
                {
                    g.FillRectangle(brush, bounds);
                }
                g.ResetClip();

                // Áp dụng vùng bo tròn cho ToolStrip
                e.ToolStrip.Region = new Region(path);
            }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;

            Rectangle rect = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height - 1);

            if (e.Item.Selected || e.Item.Pressed)
            {
                using (var path = GetRoundedRect(g, rect, 4)) // Pass 'g' as the Graphics argument
                using (var brush = new SolidBrush(e.Item.Pressed ? itemPressedColor : itemHoverColor))
                {
                    g.FillPath(brush, path);
                }
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = textColor;
            e.TextFont = new Font("Segoe UI", 9);
            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            int padding = 10;

            using (var pen = new Pen(separatorColor, 1))
            {
                g.DrawLine(pen,
                    new Point(e.Item.Bounds.Left + padding, e.Item.Bounds.Height / 2),
                    new Point(e.Item.Bounds.Right - padding, e.Item.Bounds.Height / 2));
            }
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // Ẩn lề hình ảnh mặc định
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;

            // Vẽ viền bo tròn với độ dày lớn hơn để làm mượt
            Rectangle bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using (var path = GetRoundedRect(e.Graphics, bounds, cornerRadius))
            using (var pen = new Pen(Color.FromArgb(240, 240, 240), 1.5f)) // Tăng độ dày viền
            {
                g.DrawPath(pen, path);
            }
        }

        private GraphicsPath GetRoundedRect(Graphics g, Rectangle bounds, int radius)
        {
            // Adjust scale based on DPI
            float scale = g.DpiX / 96f;
            int adjustedRadius = (int)(radius * scale);
            int diameter = adjustedRadius * 2;
            Size size = new Size(diameter, diameter);
            Rectangle arc = new Rectangle(bounds.Location, size);
            GraphicsPath path = new GraphicsPath();

            if (adjustedRadius <= 0 || bounds.Width < diameter || bounds.Height < diameter)
            {
                path.AddRectangle(bounds);
                return path;
            }

            // Top-left corner
            path.AddArc(arc, 180, 90);

            // Top-right corner
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);

            // Bottom-right corner
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);

            // Bottom-left corner
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }
    }

    public class ModernColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Color.Transparent;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuBorder => Color.Transparent;
        public override Color ImageMarginGradientBegin => Color.Transparent;
        public override Color ImageMarginGradientMiddle => Color.Transparent;
        public override Color ImageMarginGradientEnd => Color.Transparent;
        public override Color ToolStripDropDownBackground => Color.Transparent;
        public override Color SeparatorDark => Color.Transparent;
        public override Color SeparatorLight => Color.Transparent;
    }
}