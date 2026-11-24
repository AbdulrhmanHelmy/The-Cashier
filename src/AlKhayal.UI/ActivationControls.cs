using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AlKhayal.UI
{
    internal sealed class ActivationSurface : Panel
    {
        private readonly int radius;
        private Color outline;

        public ActivationSurface(int radius, Color fill, Color outline)
        {
            this.radius = radius;
            this.outline = outline;
            BackColor = fill;
            Margin = Padding.Empty;
            TabStop = false;
            DoubleBuffered = true;
        }

        public void SetOutline(Color color)
        {
            outline = color;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent.BackColor);
            ActivationDrawing.Surface(e.Graphics, ClientSize, radius, BackColor, outline);
        }
    }

    internal sealed class ActivationButton : Button
    {
        private readonly bool dark;
        private readonly bool primary;
        private bool hovered;
        private bool pressed;

        public ActivationButton(bool dark, bool primary)
        {
            this.dark = dark;
            this.primary = primary;
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var fill = ActivationSupport.Subtle(dark);
            var ink = ActivationSupport.Foreground(dark);
            var outline = ActivationSupport.Divider(dark);
            if (!Enabled)
            {
                fill = ActivationSupport.DisabledSurface(dark);
                ink = ActivationSupport.DisabledText(dark);
                outline = fill;
            }
            else if (primary)
            {
                fill = pressed ? ActivationSupport.GoldPressed : hovered ? ActivationSupport.GoldHover(dark) : ActivationSupport.Gold(dark);
                ink = ActivationSupport.GoldInk;
                outline = fill;
            }
            else if (hovered || pressed)
            {
                fill = ActivationSupport.Selected(dark);
                outline = pressed ? ActivationSupport.Primary(dark) : ActivationSupport.ControlBorder(dark);
            }

            e.Graphics.Clear(Parent.BackColor);
            ActivationDrawing.Surface(e.Graphics, ClientSize, 9, fill, outline);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues)
            {
                var focus = Rectangle.Inflate(ClientRectangle, -4, -4);
                ControlPaint.DrawFocusRectangle(e.Graphics, focus, ink, fill);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); pressed = false; Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) pressed = true; Invalidate(); }
        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); pressed = false; Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); pressed = false; Invalidate(); }
    }

    internal static class ActivationDrawing
    {
        public static void Surface(Graphics graphics, Size size, int radius, Color fill, Color outline)
        {
            if (size.Width < 2 || size.Height < 2) return;
            var diameter = Math.Min(radius * 2 * graphics.DpiX / 96, Math.Min(size.Width, size.Height) - 1);
            var right = size.Width - diameter - 1;
            var bottom = size.Height - diameter - 1;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath())
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(outline))
            {
                path.AddArc(0, 0, diameter, diameter, 180, 90);
                path.AddArc(right, 0, diameter, diameter, 270, 90);
                path.AddArc(right, bottom, diameter, diameter, 0, 90);
                path.AddArc(0, bottom, diameter, diameter, 90, 90);
                path.CloseFigure();
                graphics.FillPath(brush, path);
                graphics.DrawPath(pen, path);
            }
        }
    }
}
