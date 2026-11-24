using System;
using System.Drawing;
using System.Windows.Forms;
using AlKhayal.Application.Licensing;

namespace AlKhayal.UI
{
    public sealed class TrialAccessForm : Form
    {
        private readonly LicenseService permanent;
        private readonly bool dark;
        private readonly Func<ActivationForm> activationFormFactory;
        public TrialAccessForm(LicenseService permanent, Func<ActivationForm> activationFormFactory = null, bool? darkTheme = null)
            : this(permanent, new TrialResult(TrialState.Expired, TimeSpan.Zero), activationFormFactory, darkTheme)
        {
        }
        public TrialAccessForm(LicenseService permanent, TrialResult trialResult, Func<ActivationForm> activationFormFactory = null, bool? darkTheme = null)
        {
            this.permanent = permanent;
            this.activationFormFactory = activationFormFactory ?? (() => new ActivationForm(permanent));
            Text = "ترخيص The Cashier"; RightToLeft = RightToLeft.Yes; RightToLeftLayout = true;
            AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 10);
            StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(590, 345); MinimumSize = new Size(520, 330);
            MaximizeBox = false; MinimizeBox = false;
            dark = darkTheme ?? ActivationSupport.DarkTheme();
            BackColor = ActivationSupport.Background(dark); ForeColor = ActivationSupport.Foreground(dark);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            var unresolved = trialResult != null && (trialResult.State == TrialState.StorageUnavailable || trialResult.State == TrialState.RecoveryRequired);
            var title = unresolved ? "تعذر التحقق من حالة التجربة مؤقتًا" :
                trialResult != null && trialResult.State == TrialState.Invalidated ? "تعذر اعتماد حالة التجربة" : "انتهت الفترة التجريبية";
            var description = unresolved ?
                "تعذر الوصول إلى بيانات التجربة أو إكمال استعادتها. أعد تشغيل البرنامج، وإذا استمرت المشكلة تواصل مع الدعم. لن تبدأ تجربة جديدة ولن تتغير مدة التجربة بسبب هذا الخطأ.\n\nبيانات منشأتك محفوظة ولم تُحذف." :
                trialResult != null && trialResult.State == TrialState.Invalidated ?
                "اكتشف النظام تعارضًا لا يمكن اعتماده في بيانات التجربة. يُرجى التواصل مع الدعم أو تفعيل البرنامج تفعيلًا دائمًا مجانيًا.\n\nبيانات منشأتك محفوظة ولم تُحذف." :
                "انتهت فترة تجربة The Cashier المجانية (5 أيام). يُرجى التواصل معنا للحصول على التفعيل الدائم المجاني.\n\nبياناتك محفوظة بأمان\nيمكنك تفعيل النظام واستكمال العمل دون فقدان بيانات المنشأة.";
            layout.Controls.Add(new Label { Text = title, Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter }, 0, 0);
            layout.Controls.Add(new Label { Text = description,
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter }, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = true };
            buttons.Controls.Add(Button("التواصل للتفعيل", () => { using (var form = new ActivationContactsForm()) form.ShowDialog(this); }));
            buttons.Controls.Add(Button("الذهاب لشاشة التفعيل", OpenActivation));
            layout.Controls.Add(buttons, 0, 2);
            Controls.Add(layout);
        }

        private Button Button(string label, Action action)
        {
            var button = new Button { Text = label, AutoSize = true, MinimumSize = new Size(210, 42),
                BackColor = ActivationSupport.Gold(dark), ForeColor = ActivationSupport.GoldInk, FlatStyle = FlatStyle.Flat, Margin = new Padding(5) };
            button.Click += (s, e) => action(); return button;
        }

        private void OpenActivation()
        {
            using (var form = activationFormFactory())
                if (form.ShowDialog(this) == DialogResult.OK && permanent.Check().IsValid)
                { DialogResult = DialogResult.OK; Close(); }
        }
    }
}
