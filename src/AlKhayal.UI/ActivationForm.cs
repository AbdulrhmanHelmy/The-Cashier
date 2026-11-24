using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using AlKhayal.Application.Licensing;

namespace AlKhayal.UI
{
    public sealed class ActivationForm : Form
    {
        private const string ActivateText = "تفعيل البرنامج";
        private readonly LicenseActivationCoordinator activation;
        private readonly TrialSecurityService trial;
        private readonly bool dark;
        private readonly List<Font> fonts = new List<Font>();
        private readonly Image logo;
        private readonly TextBox request;
        private readonly TextBox license;
        private readonly ActivationSurface feedback;
        private readonly Label message;
        private Button activationButton;
        private Button trialButton;
        private bool activating;

        public ActivationForm(LicenseService service, ILicenseInstaller installer = null, TrialSecurityService trial = null, bool? darkTheme = null)
        {
            this.trial = trial;
            activation = new LicenseActivationCoordinator(service, installer ?? new WindowsLicenseInstaller());
            dark = darkTheme ?? ActivationSupport.DarkTheme();

            SuspendLayout();
            Text = "تفعيل برنامج The Cashier";
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = CreateFont(10);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(872, 456);
            MinimumSize = new Size(570, 340);
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = ActivationSupport.Background(dark);
            ForeColor = ActivationSupport.Foreground(dark);

            logo = ActivationSupport.LoadOfficialLogo();
            var codeFont = CreateFont(9, family: "Consolas");
            request = CodeBox("كود الجهاز", codeFont, true);
            license = CodeBox("كود التفعيل", codeFont, false);
            license.MaxLength = LicenseFormat.MaximumLength;
            message = Caption(string.Empty);
            message.AccessibleRole = AccessibleRole.Alert;
            feedback = new ActivationSurface(10, ActivationSupport.Subtle(dark), ActivationSupport.Divider(dark))
            {
                Dock = DockStyle.Fill, Padding = new Padding(12, 5, 12, 5), Visible = false
            };
            feedback.Controls.Add(message);

            var brand = BuildWelcome(trial != null && trial.Evaluate().State == TrialState.NotStarted);
            var copy = CreateButton("نسخ كود الجهاز", 0, CopyRequestAsync);
            var workspace = BuildWorkspace(copy);
            var viewport = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Padding = new Padding(14),
                RightToLeft = RightToLeft.No, FlowDirection = FlowDirection.LeftToRight
            };
            workspace.Margin = new Padding(0, 0, 14, 4);
            viewport.Controls.Add(workspace);
            viewport.Controls.Add(brand);
            Controls.Add(viewport);

            try { request.Text = service.CreateRequest(); }
            catch (FormatException e)
            {
                ShowStatus(e.Message, Feedback.Error);
                copy.Enabled = false;
                activationButton.Enabled = false;
            }

            ResumeLayout();
        }

        protected override void OnLoad(EventArgs e)
        {
            var area = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            CenterToScreen();
            base.OnLoad(e);
        }

        private Control BuildWorkspace(Button copy)
        {
            var card = Card(518, ActivationSupport.Surface(dark), 0);
            var rows = Rows(38, 26, 34, 111, 4, 22, 46, 8, 40, 6);
            card.Controls.Add(rows);
            rows.Controls.Add(Caption("تفعيل النسخة", 22, FontStyle.Bold), 0, 0);
            rows.Controls.Add(Caption("انسخ كود الجهاز، ثم الصق كود التفعيل اللي هيوصلك.", color: ActivationSupport.Muted(dark)), 0, 1);

            var requestHeader = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, RowCount = 1, TabIndex = 0 };
            requestHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
            requestHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            copy.Margin = new Padding(0, 2, 0, 5);
            requestHeader.Controls.Add(copy, 0, 0);
            requestHeader.Controls.Add(Caption("كود الجهاز", style: FontStyle.Bold), 1, 0);
            rows.Controls.Add(requestHeader, 0, 2);
            rows.Controls.Add(Field(request, ActivationSupport.Subtle(dark), 1), 0, 3);
            rows.Controls.Add(Caption("كود التفعيل", style: FontStyle.Bold), 0, 5);
            rows.Controls.Add(Field(license, ActivationSupport.Surface(dark), 2), 0, 6);

            activationButton = CreateButton(ActivateText, 3, ActivateLicenseAsync, true);
            rows.Controls.Add(activationButton, 0, 8);
            rows.Controls.Add(feedback, 0, 10);
            return card;
        }

        private Control BuildWelcome(bool eligible)
        {
            var card = Card(294, ActivationSupport.Subtle(dark), 1);
            var rows = Rows(36, 142, 14, 43, 55, eligible ? 40 : 0);
            card.Controls.Add(rows);
            var tile = new ActivationSurface(22, ActivationSupport.Surface(false), ActivationSupport.Divider(dark))
            {
                Size = new Size(142, 142), Anchor = AnchorStyles.None, Padding = new Padding(10)
            };
            tile.Controls.Add(new PictureBox
            {
                Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, Margin = Padding.Empty,
                AccessibleName = "شعار The Cashier", TabStop = false
            });
            rows.Controls.Add(tile, 0, 1);
            rows.Controls.Add(new Label
            {
                Text = "The Cashier", RightToLeft = RightToLeft.No, TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill, Margin = Padding.Empty, Font = CreateFont(18, FontStyle.Bold)
            }, 0, 3);
            if (eligible)
            {
                trialButton = CreateButton("ابدأ التجربة المجانية — 5 أيام", 0, StartTrialAsync);
                rows.Controls.Add(trialButton, 0, 5);
            }

            var contact = new LinkLabel
            {
                Text = "التواصل للتفعيل", Dock = DockStyle.Fill, Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleCenter,
                RightToLeft = RightToLeft.Yes, LinkColor = ActivationSupport.Primary(dark),
                ActiveLinkColor = ActivationSupport.GoldText(dark), VisitedLinkColor = ActivationSupport.Primary(dark), TabIndex = 1
            };
            contact.LinkClicked += (s, e) => { using (var form = new ActivationContactsForm(dark)) form.ShowDialog(this); };
            rows.Controls.Add(contact, 0, 6);
            return card;
        }

        private ActivationSurface Card(int width, Color fill, int tabIndex) =>
            new ActivationSurface(18, fill, ActivationSupport.Divider(dark))
            {
                Size = new Size(width, 422), Padding = new Padding(22, 18, 22, 18), TabIndex = tabIndex
            };

        private static TableLayoutPanel Rows(params int[] heights)
        {
            var rows = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 1, RowCount = heights.Length + 1
            };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var height in heights) rows.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            rows.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            return rows;
        }

        private Label Caption(string text, float size = 10, FontStyle style = FontStyle.Regular, Color? color = null) =>
            new Label
            {
                Text = text, Dock = DockStyle.Fill, Margin = Padding.Empty, RightToLeft = RightToLeft.Yes,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = color ?? ForeColor, Font = size == 10 && style == FontStyle.Regular ? Font : CreateFont(size, style)
            };

        private static TextBox CodeBox(string name, Font font, bool readOnly) =>
            new TextBox
            {
                Multiline = true, ReadOnly = readOnly, TabStop = !readOnly, Dock = DockStyle.Fill,
                RightToLeft = RightToLeft.No, TextAlign = HorizontalAlignment.Left, BorderStyle = BorderStyle.None,
                AcceptsReturn = !readOnly,
                AccessibleName = name, Font = font
            };

        private Control Field(TextBox box, Color fill, int tabIndex)
        {
            var border = ActivationSupport.ControlBorder(dark);
            var surface = new ActivationSurface(10, fill, border)
            {
                Dock = DockStyle.Fill, Padding = new Padding(11, 5, 10, 5), TabIndex = tabIndex
            };
            box.BackColor = fill;
            box.ForeColor = ForeColor;
            box.GotFocus += (s, e) => surface.SetOutline(ActivationSupport.GoldText(dark));
            box.LostFocus += (s, e) => surface.SetOutline(border);
            surface.Controls.Add(box);
            return surface;
        }

        private Button CreateButton(string text, int tabIndex, Func<Task> action, bool primary = false)
        {
            var button = new ActivationButton(dark, primary)
            {
                Text = text, AccessibleName = text, TabIndex = tabIndex, Dock = DockStyle.Fill,
                Margin = Padding.Empty, Font = CreateFont(primary ? 11 : 9, FontStyle.Bold)
            };
            button.Click += async (s, e) =>
            {
                try { await action(); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is FormatException || error is ExternalException)
                {
                    if (!IsDisposed) ShowStatus("تعذر إكمال العملية: " + error.Message, Feedback.Error);
                }
            };
            return button;
        }

        private Task CopyRequestAsync()
        {
            if (request.TextLength > 0)
            {
                Clipboard.SetText(request.Text);
                if (!activating) ShowStatus("تم نسخ كود الجهاز كاملًا.", Feedback.Success);
            }
            return Task.CompletedTask;
        }

        private async Task ActivateLicenseAsync()
        {
            if (activating) return;
            activating = true;
            activationButton.Enabled = false;
            activationButton.Text = "جارٍ التفعيل…";
            var trialWasAvailable = trialButton?.Enabled == true;
            if (trialWasAvailable) trialButton.Enabled = false;
            ShowStatus("جارٍ التحقق من كود التفعيل…", Feedback.Information);
            try
            {
                var result = await activation.ActivateAsync(license.Text.Trim());
                if (IsDisposed) return;
                ShowStatus(result.IsValid ? "تم التفعيل بنجاح." : result.Message, result.IsValid ? Feedback.Success : Feedback.Error);
                if (result.IsValid) { DialogResult = DialogResult.OK; Close(); }
            }
            finally
            {
                activating = false;
                if (!IsDisposed)
                {
                    activationButton.Text = ActivateText;
                    activationButton.Enabled = true;
                    if (trialWasAvailable) trialButton.Enabled = true;
                }
            }
        }

        private Task StartTrialAsync()
        {
            if (trialButton == null || trial == null || activating) return Task.CompletedTask;
            trialButton.Enabled = false;
            try
            {
                var result = trial.Start();
                if (result.State == TrialState.Active || result.State == TrialState.PermanentlyActivated)
                { DialogResult = DialogResult.OK; Close(); }
                else ShowStatus("لا يمكن بدء تجربة جديدة على هذا الجهاز.", Feedback.Error);
            }
            catch (Exception e)
            {
                Trace.TraceError("Trial start failed: {0} (0x{1:X8})", e.GetType().Name, e.HResult);
                ShowStatus("تعذر حفظ التجربة. تحقق من صلاحيات التخزين ثم أعد المحاولة.", Feedback.Error);
            }
            finally { if (!IsDisposed) trialButton.Enabled = trial.Evaluate().State == TrialState.NotStarted; }
            return Task.CompletedTask;
        }

        private void ShowStatus(string text, Feedback state)
        {
            var ink = ActivationSupport.Muted(dark);
            var fill = ActivationSupport.Subtle(dark);
            switch (state)
            {
                case Feedback.Error:
                    ink = ActivationSupport.Error(dark);
                    fill = ActivationSupport.ErrorSurface(dark);
                    break;
                case Feedback.Success:
                    ink = ActivationSupport.Success(dark);
                    fill = ActivationSupport.SuccessSurface(dark);
                    break;
            }
            feedback.BackColor = fill;
            feedback.SetOutline(fill);
            message.ForeColor = ink;
            message.Text = text;
            feedback.Visible = true;
        }

        private Font CreateFont(float size, FontStyle style = FontStyle.Regular, string family = "Segoe UI")
        {
            var font = new Font(family, size, style);
            fonts.Add(font);
            return font;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            logo?.Dispose();
            foreach (var font in fonts) font.Dispose();
            fonts.Clear();
        }

        private enum Feedback { Information, Success, Error }
    }
}
