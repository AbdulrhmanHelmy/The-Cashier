using System;
using System.Drawing;
using System.Windows.Forms;

namespace AlKhayal.UI
{
    internal sealed class StartupFailureForm : Form
    {
        public StartupFailureForm()
        {
            Text = "The Cashier";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 250);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            AutoScaleMode = AutoScaleMode.Dpi;

            var title = new Label
            {
                Dock = DockStyle.Top,
                Height = 58,
                Text = "تعذر بدء تشغيل نظام The Cashier",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 16f, FontStyle.Bold)
            };
            var explanation = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(28, 8, 28, 8),
                Text = "حدثت مشكلة أثناء تجهيز البرنامج. يمكنك إعادة المحاولة بأمان، أو إغلاق البرنامج والمحاولة لاحقًا.",
                TextAlign = ContentAlignment.MiddleCenter
            };
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(8),
                AutoSize = false
            };
            var retry = new Button
            {
                AutoSize = true,
                Text = "إعادة المحاولة",
                DialogResult = DialogResult.Retry,
                UseVisualStyleBackColor = true
            };
            var close = new Button
            {
                AutoSize = true,
                Text = "إغلاق البرنامج",
                DialogResult = DialogResult.Abort,
                UseVisualStyleBackColor = true
            };
            buttons.Controls.Add(retry);
            buttons.Controls.Add(close);
            Controls.Add(explanation);
            Controls.Add(buttons);
            Controls.Add(title);
            AcceptButton = retry;
            CancelButton = close;
        }
    }
}
