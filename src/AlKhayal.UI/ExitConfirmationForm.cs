using System.Drawing;
using System.Windows.Forms;

namespace AlKhayal.UI
{
    internal sealed class ExitConfirmationForm : Form
    {
        private readonly Button _cancelButton;

        public ExitConfirmationForm()
        {
            Text = "The Cashier";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(360, 140);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;

            var message = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 68,
                Padding = new Padding(16, 18, 16, 8),
                Text = "هل تريد إغلاق نظام The Cashier؟",
                TextAlign = ContentAlignment.MiddleCenter
            };

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8),
                WrapContents = false
            };

            var closeButton = new Button
            {
                AutoSize = true,
                Text = "إغلاق البرنامج",
                DialogResult = DialogResult.OK,
                UseVisualStyleBackColor = true
            };
            _cancelButton = new Button
            {
                AutoSize = true,
                Text = "إلغاء",
                DialogResult = DialogResult.Cancel,
                UseVisualStyleBackColor = true
            };

            buttons.Controls.Add(closeButton);
            buttons.Controls.Add(_cancelButton);
            Controls.Add(message);
            Controls.Add(buttons);
            AcceptButton = _cancelButton;
            CancelButton = _cancelButton;
        }

        protected override void OnShown(System.EventArgs e)
        {
            base.OnShown(e);
            _cancelButton.Focus();
        }
    }
}
