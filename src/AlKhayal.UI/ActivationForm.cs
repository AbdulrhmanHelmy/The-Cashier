using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Diagnostics;
using AlKhayal.Application.Licensing;
namespace AlKhayal.UI
{
    public sealed class ActivationForm : Form
    {
        private readonly LicenseService service;
        private readonly LicenseActivationCoordinator activation;
        private readonly TrialSecurityService trial;
        private Button activationButton;
        private Button trialButton;
        // Selectable display only: excluded from the action/tab sequence.
        private readonly TextBox request = new TextBox { Multiline = true, ReadOnly = true, TabStop = false, Dock = DockStyle.Fill, RightToLeft = RightToLeft.No, BorderStyle = BorderStyle.FixedSingle, AccessibleRole = AccessibleRole.StaticText, AccessibleName = "كود الجهاز" };
        private readonly TextBox license = new TextBox { Multiline = true, MaxLength = LicenseFormat.MaximumLength, Dock = DockStyle.Fill, RightToLeft = RightToLeft.No, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "كود التفعيل", TabIndex = 1 };
        private readonly Label message = new Label { Dock = DockStyle.Fill, ForeColor = Color.Maroon, AutoEllipsis = false, AccessibleRole = AccessibleRole.Alert };
        public ActivationForm(LicenseService service, ILicenseInstaller installer = null, TrialSecurityService trial = null, bool? darkTheme = null)
        {
            this.service = service;
            this.trial = trial;
            activation = new LicenseActivationCoordinator(service, installer ?? new WindowsLicenseInstaller());
            Text = "تفعيل برنامج The Cashier"; RightToLeft = RightToLeft.Yes; RightToLeftLayout = true;
            AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 9.5f);
            StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(620, 410); MinimumSize = new Size(580, 440); MaximizeBox = false; MinimizeBox = false;
            var dark = darkTheme ?? ActivationSupport.DarkTheme();
            BackColor = ActivationSupport.Background(dark); ForeColor = ActivationSupport.Foreground(dark);
            request.BackColor = dark ? Color.FromArgb(30, 49, 74) : Color.FromArgb(236, 240, 245);
            request.ForeColor = ForeColor; license.BackColor = dark ? Color.FromArgb(30, 49, 74) : Color.White; license.ForeColor = ForeColor;
            message.ForeColor = dark ? Color.FromArgb(255, 180, 180) : Color.Maroon;
            request.Font = new Font("Consolas", 8.5f); license.Font = request.Font;
            var eligible = trial != null && trial.Evaluate().State == TrialState.NotStarted;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 9 };
            foreach (var height in new[] { 34, 70, 30, 20, 70, 34, eligible ? 62 : 0, 28 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label { Text = "تفعيل النسخة — التفعيل مجاني ودائم لهذا الجهاز.", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
            layout.Controls.Add(request, 0, 1);
            var copy = CreateButton("نسخ كود الجهاز", 0, () => { if (request.Text.Length > 0) { Clipboard.SetText(request.Text); message.Text = "تم نسخ كود الجهاز كاملًا."; } return Task.CompletedTask; });
            layout.Controls.Add(copy, 0, 2);
            layout.Controls.Add(new Label { Text = "كود التفعيل", AutoSize = true }, 0, 3);
            layout.Controls.Add(license, 0, 4);
            var activate = activationButton = CreateButton("تفعيل البرنامج", 2, ActivateLicenseAsync);
            layout.Controls.Add(activate, 0, 5);
            if (eligible)
            {
                var trialSection = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
                trialSection.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
                trialSection.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
                trialSection.Controls.Add(new Label { Text = "كل الميزات متاحة دون إنترنت؛ بعد 5 أيام يلزم التفعيل الدائم المجاني.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, 0);
                trialButton = CreateButton("ابدأ التجربة المجانية — 5 أيام", 3, StartTrialAsync);
                trialButton.MinimumSize = new Size(240, 36);
                trialSection.Controls.Add(trialButton, 0, 1);
                layout.Controls.Add(trialSection, 0, 6);
            }
            var contact = new LinkLabel { Text = "التواصل للتفعيل", AutoSize = true, Anchor = AnchorStyles.Left,
                LinkColor = dark ? ActivationSupport.Gold : ActivationSupport.Navy, TabIndex = 4 };
            contact.LinkClicked += (s, e) => { using (var form = new ActivationContactsForm()) form.ShowDialog(this); };
            layout.Controls.Add(contact, 0, 7);
            layout.Controls.Add(message, 0, 8); Controls.Add(layout);
            message.Text = service.Check().Message;
            try { request.Text = service.CreateRequest(); }
            catch (FormatException e) { message.Text = e.Message; copy.Enabled = false; activate.Enabled = false; }
        }
        private Button CreateButton(string text, int tabIndex, Func<Task> action)
        {
            var button = new Button { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Height = 29, MinimumSize = new Size(120, 28), TabIndex = tabIndex, FlatStyle = FlatStyle.Flat, BackColor = ActivationSupport.Gold, ForeColor = ActivationSupport.Navy, Margin = new Padding(0, 2, 0, 2) };
            button.FlatAppearance.BorderColor = Color.FromArgb(191, 159, 89);
            button.Click += async (s, e) =>
            {
                try { await action(); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is FormatException || error is System.Runtime.InteropServices.ExternalException)
                { message.Text = "تعذر إكمال العملية: " + error.Message; }
            };
            return button;
        }
        private async Task ActivateLicenseAsync()
        {
            activationButton.Enabled = false;
            message.Text = "جارٍ التحقق وحفظ التفعيل. قد يطلب Windows صلاحيات المسؤول لهذه العملية فقط.";
            try
            {
                var result = await activation.ActivateAsync(license.Text.Trim());
                if (IsDisposed) return;
                message.Text = result.Message;
                if (result.IsValid) { DialogResult = DialogResult.OK; Close(); }
            }
            finally { if (!IsDisposed) activationButton.Enabled = true; }
        }
        private Task StartTrialAsync()
        {
            if (trialButton == null || trial == null) return Task.CompletedTask;
            trialButton.Enabled = false;
            try
            {
                var result = trial.Start();
                if (result.State == TrialState.Active || result.State == TrialState.PermanentlyActivated)
                { DialogResult = DialogResult.OK; Close(); }
                else message.Text = "لا يمكن بدء تجربة جديدة على هذا الجهاز. التفعيل الدائم مجاني.";
            }
            catch (Exception e)
            {
                Trace.TraceError("Trial start failed: {0} (0x{1:X8})", e.GetType().Name, e.HResult);
                message.Text = "تعذر حفظ التجربة. تحقق من صلاحيات التخزين ثم أعد المحاولة.";
            }
            finally { if (!IsDisposed) trialButton.Enabled = trial.Evaluate().State == TrialState.NotStarted; }
            return Task.CompletedTask;
        }
    }
}
