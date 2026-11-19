using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AlKhayal.Application.Licensing;
using AlKhayal.Data.Licensing;
using AlKhayal.UI;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class TrialDialogTests
    {
        private sealed class Clock : ITrialClock
        {
            public DateTime UtcNow { get; set; } = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);
            public TimeSpan Elapsed { get; set; }
        }
        private sealed class Store : ITrialStore
        {
            public TrialRecord[] Records = new TrialRecord[3];
            public TrialRecord[] ReadAll() => (TrialRecord[])Records.Clone();
            public void WriteAll(TrialRecord record) { for (int i = 0; i < 3; i++) Records[i] = record; }
        }
        private sealed class LicenseStorage : ILicenseStorage
        {
            public string Text;
            public string Read() => Text;
            public void Write(string text) { Text = text; }
        }
        private sealed class Hardware : IHardwareIdentity
        {
            public IDictionary<string, string> ReadClaims() => LicenseFormat.HashClaims(new Dictionary<string, string>
            { { "uuid", "trial-ui-uuid" }, { "board", "trial-ui-board" }, { "bios", "trial-ui-bios" } });
        }
        private sealed class DirectInstaller : ILicenseInstaller
        {
            private readonly LicenseService license;
            public DirectInstaller(LicenseService license) { this.license = license; }
            public Task<LicenseInstallResult> InstallAsync(string text) => Task.FromResult(license.Activate(text).IsValid ? LicenseInstallResult.Success : LicenseInstallResult.InvalidLicense);
        }

        [Fact]
        public void Original_activation_starts_trial_once_and_expired_actions_use_existing_form_and_contacts()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var rsa = RsaLicenseSignatures.CreateRsa())
                    {
                        var hardware = new Hardware(); var storage = new LicenseStorage(); var clock = new Clock(); var store = new Store();
                        var permanent = new LicenseService(storage, new RsaLicenseSignatures(new Dictionary<string, string> { { "trial-test", rsa.ToXmlString(false) } }), hardware);
                        var trial = new TrialSecurityService(permanent, store, clock, "trial-device");
                        using (var entry = new ActivationForm(permanent, new DirectInstaller(permanent), trial))
                        {
                            entry.Show(); System.Windows.Forms.Application.DoEvents(); Preview(entry, "activation-start");
                            Assert.Equal(RightToLeft.Yes, entry.RightToLeft); Assert.True(entry.RightToLeftLayout);
                            entry.Scale(new SizeF(1.5f, 1.5f)); System.Windows.Forms.Application.DoEvents();
                            Assert.True(entry.Width <= 1366 && entry.Height <= 768);
                            foreach (var button in Descendants(entry).OfType<Button>())
                            {
                                var bounds = entry.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                                Assert.True(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= entry.ClientSize.Width && bounds.Bottom <= entry.ClientSize.Height);
                            }
                            Assert.Contains(Descendants(entry).OfType<Label>(), x => x.Text.Contains("تفعيل النسخة"));
                            var start = Assert.Single(Descendants(entry).OfType<Button>(), x => x.Text == "ابدأ التجربة المجانية — 5 أيام");
                            start.PerformClick(); Assert.Equal(DialogResult.OK, entry.DialogResult);
                            Assert.Equal(TrialState.Active, trial.Evaluate().State);
                        }
                        using (var active = new ActivationForm(permanent, new DirectInstaller(permanent), trial))
                            Assert.DoesNotContain(Descendants(active).OfType<Button>(), x => x.Text.Contains("ابدأ التجربة"));
                        clock.UtcNow = clock.UtcNow.AddHours(120);
                        Assert.Equal(TrialState.Expired, trial.Evaluate().State);
                        using (var expired = new TrialAccessForm(permanent, () => SignedActivation(permanent, hardware, rsa)))
                        {
                            expired.Show(); System.Windows.Forms.Application.DoEvents(); Preview(expired, "expired");
                            Assert.Equal(RightToLeft.Yes, expired.RightToLeft);
                            var buttons = Descendants(expired).OfType<Button>().ToArray();
                            Assert.Equal(new[] { "التواصل للتفعيل", "الذهاب لشاشة التفعيل" }, buttons.Select(x => x.Text).ToArray());
                            Assert.Contains("بياناتك محفوظة بأمان", string.Join(" ", Descendants(expired).OfType<Label>().Select(x => x.Text)));
                            VerifyContactPopup(buttons[0]);
                            buttons[1].PerformClick(); Assert.Equal(DialogResult.OK, expired.DialogResult);
                            Assert.True(permanent.Check().IsValid);
                            Assert.Equal(TrialState.PermanentlyActivated, trial.Evaluate().State);
                        }
                    }
                }
                catch (Exception e) { failure = e; }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Trial dialog test did not exit.");
            if (failure != null) throw new Exception("Trial dialog test failed", failure);
        }

        [Fact]
        public void Native_license_dialogs_use_RTL_light_and_dark_palettes()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var hardware = new Hardware(); var permanent = new LicenseService(new LicenseStorage(),
                        new RsaLicenseSignatures(new Dictionary<string, string>()), hardware);
                    foreach (var dark in new[] { false, true })
                    {
                        using (var activation = new ActivationForm(permanent, new DirectInstaller(permanent), null, dark))
                        using (var expired = new TrialAccessForm(permanent, null, dark))
                        using (var contacts = new ActivationContactsForm(dark))
                        {
                            Assert.Equal(ActivationSupport.Background(dark), activation.BackColor);
                            Assert.Equal(ActivationSupport.Background(dark), expired.BackColor);
                            Assert.Equal(ActivationSupport.Background(dark), contacts.BackColor);
                            Assert.Equal(RightToLeft.Yes, activation.RightToLeft);
                            Assert.Equal(RightToLeft.Yes, expired.RightToLeft);
                            Assert.Equal(RightToLeft.Yes, contacts.RightToLeft);
                            activation.Show(); expired.Show(); contacts.Show(); System.Windows.Forms.Application.DoEvents();
                            Preview(activation, dark ? "activation-dark" : "activation-light");
                            Preview(expired, dark ? "expired-dark" : "expired-light");
                            Preview(contacts, dark ? "contacts-dark" : "contacts-light");
                        }
                    }
                }
                catch (Exception e) { failure = e; }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
            if (failure != null) throw failure;
        }

        [Fact]
        public void Temporary_storage_failure_is_not_presented_as_trial_expiration()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var permanent = new LicenseService(new LicenseStorage(),
                        new RsaLicenseSignatures(new Dictionary<string, string>()), new Hardware());
                    var result = new TrialResult(TrialState.StorageUnavailable, TimeSpan.Zero, "storage_read");
                    using (var form = new TrialAccessForm(permanent, result, null, false))
                    {
                        var text = string.Join(" ", Descendants(form).OfType<Label>().Select(x => x.Text));
                        Assert.DoesNotContain("انتهت الفترة التجريبية", text);
                        Assert.Contains("تعذر التحقق من حالة التجربة مؤقتًا", text);
                        Assert.Contains("أعد تشغيل البرنامج", text);
                    }
                }
                catch (Exception e) { failure = e; }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
            if (failure != null) throw failure;
        }

        private static ActivationForm SignedActivation(LicenseService service, Hardware hardware, System.Security.Cryptography.RSACryptoServiceProvider rsa)
        {
            var form = new ActivationForm(service, new DirectInstaller(service));
            form.Shown += (s, e) =>
            {
                var payload = LicenseFormat.CreatePayload(hardware.ReadClaims(), "trial-test", Guid.NewGuid(), DateTime.UtcNow);
                Descendants(form).OfType<TextBox>().Single(x => !x.ReadOnly).Text = payload + "|" + Convert.ToBase64String(rsa.SignData(LicenseFormat.Bytes(payload), "SHA256"));
                form.BeginInvoke(new Action(() => Descendants(form).OfType<Button>().Single(x => x.Text == "تفعيل البرنامج").PerformClick()));
            };
            return form;
        }

        private static void VerifyContactPopup(Button contact)
        {
            var clipboard = Clipboard.GetDataObject();
            try
            {
                bool inspected = false;
                using (var timer = new System.Windows.Forms.Timer { Interval = 30 })
                {
                    timer.Tick += (s, e) =>
                    {
                        var popup = System.Windows.Forms.Application.OpenForms.OfType<ActivationContactsForm>().FirstOrDefault();
                        if (popup == null) return;
                        timer.Stop(); inspected = true;
                        Assert.Equal(RightToLeft.Yes, popup.RightToLeft);
                        Assert.Equal("التواصل لتفعيل الخيال", popup.Text);
                        Assert.Equal(new[] { "01157999456", "01273266695" }, Descendants(popup).OfType<TextBox>().Select(x => x.Text).ToArray());
                        var copy = Descendants(popup).OfType<Button>().Where(x => x.Text == "نسخ الرقم").ToArray();
                        Assert.Equal(2, copy.Length);
                        copy[0].PerformClick(); Assert.Equal("01157999456", Clipboard.GetText());
                        copy[1].PerformClick(); Assert.Equal("01273266695", Clipboard.GetText());
                        Preview(popup, "contacts"); popup.Close();
                    };
                    timer.Start(); contact.PerformClick(); Assert.True(inspected);
                }
            }
            finally { if (clipboard != null) Clipboard.SetDataObject(clipboard, true); else Clipboard.Clear(); }
        }

        private static IEnumerable<Control> Descendants(Control root)
        { foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
        private static void Preview(Form form, string suffix)
        {
            var directory = Environment.GetEnvironmentVariable("ALKHAYAL_TRIAL_PREVIEW_DIRECTORY");
            if (string.IsNullOrWhiteSpace(directory)) return;
            System.IO.Directory.CreateDirectory(directory);
            using (var image = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
                image.Save(System.IO.Path.Combine(directory, "trial-" + suffix + ".png")); }
        }
    }
}
