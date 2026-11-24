using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AlKhayal.UI
{
    public static class ActivationSupport
    {
        public const string FirstNumber = "01157999456";
        public const string SecondNumber = "01273266695";

        public static bool DarkTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) == 0;
            }
            catch { return false; } // The preference is absent on older Windows versions.
        }

        public static Color Background(bool dark) => dark ? Color.FromArgb(21, 23, 25) : Color.FromArgb(246, 250, 249);
        public static Color Surface(bool dark) => dark ? Color.FromArgb(32, 36, 38) : Color.White;
        public static Color Foreground(bool dark) => dark ? Color.FromArgb(242, 247, 246) : Color.FromArgb(29, 48, 48);
        public static Color Muted(bool dark) => dark ? Color.FromArgb(184, 206, 202) : Color.FromArgb(82, 107, 105);
        public static Color Gold(bool dark) => dark ? Color.FromArgb(255, 204, 51) : Color.FromArgb(245, 183, 0);
        public static Color GoldInk => Color.FromArgb(23, 41, 38);
        public static Color PrimaryInk(bool dark) => dark ? Color.FromArgb(16, 26, 27) : Color.White;
        public static Color Primary(bool dark) => dark ? Color.FromArgb(20, 184, 166) : Color.FromArgb(17, 94, 89);
        internal static Color Subtle(bool dark) => dark ? Color.FromArgb(41, 46, 48) : Color.FromArgb(240, 245, 243);
        internal static Color Divider(bool dark) => dark ? Color.FromArgb(59, 66, 68) : Color.FromArgb(217, 231, 227);
        internal static Color ControlBorder(bool dark) => dark ? Color.FromArgb(123, 153, 148) : Color.FromArgb(123, 150, 144);
        internal static Color GoldText(bool dark) => dark ? Gold(true) : Color.FromArgb(121, 85, 0);
        internal static Color GoldHover(bool dark) => dark ? Color.FromArgb(255, 224, 117) : Gold(true);
        internal static Color GoldPressed => Color.FromArgb(221, 166, 0);
        internal static Color Selected(bool dark) => dark ? Color.FromArgb(38, 70, 66) : Color.FromArgb(223, 242, 236);
        internal static Color DisabledSurface(bool dark) => dark ? Color.FromArgb(64, 85, 86) : Color.FromArgb(228, 229, 224);
        internal static Color DisabledText(bool dark) => dark ? Color.FromArgb(175, 193, 189) : Color.FromArgb(102, 115, 111);
        internal static Color Error(bool dark) => dark ? Color.FromArgb(248, 113, 113) : Color.FromArgb(185, 28, 28);
        internal static Color ErrorSurface(bool dark) => dark ? Color.FromArgb(69, 38, 43) : Color.FromArgb(253, 236, 236);
        internal static Color Success(bool dark) => dark ? Color.FromArgb(74, 222, 128) : Color.FromArgb(20, 107, 52);
        internal static Color SuccessSurface(bool dark) => dark ? Color.FromArgb(27, 60, 48) : Color.FromArgb(237, 249, 240);

        internal static Image LoadOfficialLogo()
        {
            using (var stream = typeof(ActivationSupport).Assembly.GetManifestResourceStream("AlKhayal.UI.Assets.the-cashier-mark.png"))
            {
                if (stream == null) throw new InvalidOperationException("تعذر تحميل شعار The Cashier الرسمي.");
                using (var source = Image.FromStream(stream)) return new Bitmap(source);
            }
        }
    }

    public sealed class ActivationContactsForm : Form
    {
        private readonly Label status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, AccessibleRole = AccessibleRole.Alert };
        public ActivationContactsForm(bool? darkTheme = null)
        {
            var dark = darkTheme ?? ActivationSupport.DarkTheme();
            Text = "التواصل لتفعيل The Cashier"; RightToLeft = RightToLeft.Yes; RightToLeftLayout = true;
            AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 10);
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(410, 250); MinimumSize = new Size(390, 250);
            MaximizeBox = false; MinimizeBox = false; BackColor = ActivationSupport.Background(dark); ForeColor = ActivationSupport.Foreground(dark);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label { Text = "التواصل لتفعيل The Cashier", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter }, 0, 0);
            layout.Controls.Add(NumberRow(ActivationSupport.FirstNumber, dark), 0, 1);
            layout.Controls.Add(NumberRow(ActivationSupport.SecondNumber, dark), 0, 2);
            layout.Controls.Add(status, 0, 3);
            Controls.Add(layout);
        }

        private Control NumberRow(string number, bool dark)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            row.Controls.Add(new TextBox { Text = number, ReadOnly = true, TabStop = false, RightToLeft = RightToLeft.No,
                TextAlign = HorizontalAlignment.Center, Dock = DockStyle.Fill, AccessibleName = "رقم التواصل " + number,
                BackColor = ActivationSupport.Surface(dark), ForeColor = ActivationSupport.Foreground(dark) }, 0, 0);
            var copy = new Button { Text = "نسخ الرقم", Dock = DockStyle.Fill, AutoSize = false, Tag = number,
                BackColor = ActivationSupport.Gold(dark), ForeColor = ActivationSupport.GoldInk, FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleCenter, AccessibleName = "نسخ الرقم " + number };
            copy.Click += (s, e) => { Clipboard.SetText(number); status.Text = "تم نسخ الرقم " + number; };
            row.Controls.Add(copy, 1, 0);
            return row;
        }
    }
}
