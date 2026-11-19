using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace AlKhayal.UI
{
    internal sealed class SplashForm : Form
    {
        private readonly Label _stage = new Label();
        public SplashForm()
        {
            var dark = ActivationSupport.DarkTheme();
            Text = "The Cashier"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 330); BackColor = ActivationSupport.Background(dark); ForeColor = ActivationSupport.Foreground(dark);
            RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; AutoScaleMode = AutoScaleMode.Dpi;
            var logo = new OfficialLogoControl(LoadOfficialLogo()) { Size = new Size(96, 96), Location = new Point(192, 30), BackColor = Color.Transparent, AccessibleName = "شعار The Cashier" };
            var name = Label("The Cashier", 19f, ActivationSupport.Gold, new Point(40, 130), new Size(400, 38), FontStyle.Bold);
            var message = Label("جاري تحميل النظام...", 12f, ForeColor, new Point(40, 180), new Size(400, 30), FontStyle.Regular);
            _stage = Label("جارٍ التحقق من جاهزية النظام", 9f, dark ? Color.FromArgb(190, 205, 225) : Color.FromArgb(76, 94, 120), new Point(40, 211), new Size(400, 24), FontStyle.Regular);
            var progress = new ProgressBar { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 24, Location = new Point(82, 245), Size = new Size(316, 6) };
            var contact = Label("للتواصل والاستفسارات\n" + ActivationSupport.FirstNumber + "  •  " + ActivationSupport.SecondNumber, 8.5f, dark ? Color.FromArgb(180, 196, 216) : Color.FromArgb(83, 100, 124), new Point(40, 268), new Size(400, 42), FontStyle.Regular); contact.RightToLeft = RightToLeft.No;
            Controls.Add(logo); Controls.Add(name); Controls.Add(message); Controls.Add(_stage); Controls.Add(progress); Controls.Add(contact);
        }
        private static Label Label(string text, float size, Color color, Point location, Size dimensions, FontStyle style) { return new Label { Text = text, Font = new Font("Segoe UI", size, style), ForeColor = color, TextAlign = ContentAlignment.MiddleCenter, Location = location, Size = dimensions }; }
        private static Image LoadOfficialLogo()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AlKhayal.UI.Assets.the-cashier-mark.png"))
            {
                if (stream == null) throw new InvalidOperationException("تعذر تحميل شعار The Cashier الرسمي.");
                using (var source = Image.FromStream(stream)) return new Bitmap(source);
            }
        }
        public void SetStage(string value) { if (IsDisposed) return; if (InvokeRequired) { BeginInvoke(new Action<string>(SetStage), value); return; } _stage.Text = value; }
        public void CloseSplash() { if (IsDisposed) return; if (InvokeRequired) { BeginInvoke(new Action(CloseSplash)); return; } Close(); }
        private sealed class OfficialLogoControl : Control
        {
            private readonly Image _image;
            public OfficialLogoControl(Image image) { _image = image ?? throw new ArgumentNullException(nameof(image)); SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e); e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic; e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality; e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
                var scale = Math.Min((float)Width / _image.Width, (float)Height / _image.Height); var width = (int)Math.Round(_image.Width * scale); var height = (int)Math.Round(_image.Height * scale);
                e.Graphics.DrawImage(_image, new Rectangle((Width - width) / 2, (Height - height) / 2, width, height));
            }
            protected override void Dispose(bool disposing) { if (disposing) _image.Dispose(); base.Dispose(disposing); }
        }
    }
}
