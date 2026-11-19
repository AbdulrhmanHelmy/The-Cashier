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

        public static Color Background(bool dark) => dark ? Color.FromArgb(18, 35, 58) : Color.FromArgb(245, 247, 250);
        public static Color Foreground(bool dark) => dark ? Color.FromArgb(245, 247, 250) : Color.FromArgb(18, 35, 58);
        public static Color Gold => Color.FromArgb(218, 185, 111);
        public static Color Navy => Color.FromArgb(18, 35, 58);
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
                BackColor = dark ? Color.FromArgb(30, 49, 74) : Color.White, ForeColor = ActivationSupport.Foreground(dark) }, 0, 0);
            var copy = new Button { Text = "نسخ الرقم", Dock = DockStyle.Fill, AutoSize = false, Tag = number,
                BackColor = ActivationSupport.Gold, ForeColor = ActivationSupport.Navy, FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleCenter, AccessibleName = "نسخ الرقم " + number };
            copy.Click += (s, e) => { Clipboard.SetText(number); status.Text = "تم نسخ الرقم " + number; };
            row.Controls.Add(copy, 1, 0);
            return row;
        }
    }
}
