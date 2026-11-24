using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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
    public sealed class ActivationDialogTests
    {
        [Fact]
        public void Native_RTL_three_actions_copy_paste_invalid_valid_activation_and_titlebar_close()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using(var rsa = RsaLicenseSignatures.CreateRsa())
                    {
                        var hardware = new Hardware(); var storage = new MemoryStorage();
                        var service = new LicenseService(storage,new RsaLicenseSignatures(new Dictionary<string,string>{{"ui-test",rsa.ToXmlString(false)}}),hardware);
                        using(var dialog = new ActivationForm(service, new DirectTestInstaller(service)))
                        {
                            dialog.Show(); System.Windows.Forms.Application.DoEvents();
                            Assert.Equal(RightToLeft.Yes,dialog.RightToLeft); Assert.True(dialog.RightToLeftLayout);
                            Assert.False(dialog.MinimizeBox); Assert.False(dialog.MaximizeBox);
                            var preview = Environment.GetEnvironmentVariable("ALKHAYAL_ACTIVATION_PREVIEW_PATH");
                            if (!string.IsNullOrEmpty(preview))
                                using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                                { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(preview); }
                            var all = Descendants(dialog).ToArray();
                            Assert.Equal(2,all.OfType<TextBox>().Count());
                            Assert.Equal(2,all.OfType<Button>().Count());
                            Assert.Equal(3,all.Count(c => c.TabStop && (c is TextBox || c is Button)));
                            Assert.True(dialog.Height <= 768);
                            var display = all.OfType<TextBox>().Single(t => t.ReadOnly);
                            Assert.False(display.TabStop);
                            var input = all.OfType<TextBox>().Single(t => !t.ReadOnly);
                            Assert.False(Alert(dialog).Visible);
                            Assert.Contains(all.OfType<Label>(), label => label.Text == "انسخ كود الجهاز، ثم الصق كود التفعيل اللي هيوصلك.");
                            Assert.DoesNotContain(all.OfType<Label>(), label => label.Text.Contains("إنترنت") || label.Text.Contains("الإنترنت"));
                            var copy = all.OfType<Button>().Single(b => b.Text == "نسخ كود الجهاز");
                            var activate = all.OfType<Button>().Single(b => b.Text == "تفعيل البرنامج");
                            var contact = all.OfType<LinkLabel>().Single();
                            Assert.Equal(RightToLeft.No, display.RightToLeft);
                            Assert.Equal(RightToLeft.No, input.RightToLeft);
                            var lastRequestCharacter = display.GetPositionFromCharIndex(display.TextLength - 1);
                            Assert.InRange(lastRequestCharacter.Y + display.Font.Height, 1, display.ClientSize.Height);
                            Assert.True(input.AcceptsReturn);
                            Assert.True(copy.Focus());
                            foreach (var pair in new (Control From, Control To)[] { (copy, input), (input, activate), (activate, contact) })
                            {
                                Assert.True(dialog.SelectNextControl(pair.From, true, true, true, false));
                                Assert.True(pair.To.Focused);
                            }
                            var clipboard = Clipboard.GetDataObject();
                            try
                            {
                                copy.PerformClick();
                                Assert.Equal("تم نسخ كود الجهاز كاملًا.", Alert(dialog).Text);
                                Assert.Equal(service.CreateRequest(),Clipboard.GetText());
                                Assert.Equal(service.CreateRequest(),display.Text);
                                input.Paste(); Assert.Equal(display.Text,input.Text);
                            }
                            finally { if(clipboard != null) Clipboard.SetDataObject(clipboard,true); else Clipboard.Clear(); }
                            input.Text = "invalid"; activate.PerformClick(); Assert.True(dialog.Visible); Assert.False(service.Check().IsValid);
                            Assert.Equal(service.Validate("invalid").Message, Alert(dialog).Text);
                            var payload = LicenseFormat.CreatePayload(hardware.ReadClaims(),"ui-test",Guid.NewGuid(),DateTime.UtcNow);
                            input.Text = payload + "|" + Convert.ToBase64String(rsa.SignData(LicenseFormat.Bytes(payload),"SHA256"));
                            activate.PerformClick(); Assert.Equal(DialogResult.OK,dialog.DialogResult); Assert.True(service.Check().IsValid);
                        }
                        storage.Text = null;
                        using(var exit = new ActivationForm(service, new DirectTestInstaller(service)))
                        {
                            exit.Shown += (s,e) => exit.Close();
                            Assert.Equal(DialogResult.Cancel,exit.ShowDialog()); Assert.False(service.Check().IsValid);
                        }
                    }
                }
                catch(Exception error) { failure = error; }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)),"Activation dialog did not exit cleanly.");
            if(failure != null) throw new Exception("Native dialog test failed",failure);
        }
        [Fact]
        public void Native_activation_shows_pending_state_and_blocks_repeat_install_until_complete()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var rsa = RsaLicenseSignatures.CreateRsa())
                    {
                        var hardware = new Hardware();
                        var storage = new MemoryStorage();
                        var service = new LicenseService(storage,
                            new RsaLicenseSignatures(new Dictionary<string, string> { { "ui-test", rsa.ToXmlString(false) } }), hardware);
                        var installer = new PendingInstaller();
                        using (var dialog = new ActivationForm(service, installer, darkTheme: true))
                        {
                            dialog.Show();
                            var input = Descendants(dialog).OfType<TextBox>().Single(box => !box.ReadOnly);
                            var activate = Descendants(dialog).OfType<Button>().Single(button => button.Text == "تفعيل البرنامج");
                            var payload = LicenseFormat.CreatePayload(hardware.ReadClaims(), "ui-test", Guid.NewGuid(), DateTime.UtcNow);
                            input.Text = payload + "|" + Convert.ToBase64String(rsa.SignData(LicenseFormat.Bytes(payload), "SHA256"));
                            string closingMessage = null;
                            Color closingColor = Color.Empty;
                            dialog.FormClosing += (s, e) =>
                            {
                                closingMessage = Alert(dialog).Text;
                                closingColor = Alert(dialog).ForeColor;
                            };

                            activate.PerformClick();
                            Assert.False(activate.Enabled);
                            Assert.Contains("جارٍ", activate.Text);
                            Assert.Contains("جارٍ التحقق", Alert(dialog).Text);
                            activate.PerformClick();
                            Assert.Equal(1, installer.Calls);
                            Assert.True(service.Activate(input.Text).IsValid);
                            installer.Completion.SetResult(LicenseInstallResult.Success);
                            System.Windows.Forms.Application.DoEvents();
                            Assert.Equal(DialogResult.OK, dialog.DialogResult);
                            Assert.Equal("تم التفعيل بنجاح.", closingMessage);
                            Assert.Equal(ActivationSupport.Success(true), closingColor);
                        }
                    }
                }
                catch (Exception error) { failure = error; }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
            if (failure != null) throw new Exception("Activation pending state failed", failure);
        }
        [Theory]
        [InlineData(LicenseInstallResult.Cancelled)]
        [InlineData(LicenseInstallResult.AccessDenied)]
        public void Cancelled_or_denied_install_keeps_pasted_code_and_allows_retry(LicenseInstallResult failure)
        {
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using(var rsa = RsaLicenseSignatures.CreateRsa())
                    {
                        var hardware=new Hardware();var service=new LicenseService(new MemoryStorage(),new RsaLicenseSignatures(new Dictionary<string,string>{{"ui-test",rsa.ToXmlString(false)}}),hardware);
                        using(var dialog=new ActivationForm(service,new RetryTestInstaller(service,failure)))
                        {
                            dialog.Show();System.Windows.Forms.Application.DoEvents();
                            var controls=Descendants(dialog).ToArray();var input=controls.OfType<TextBox>().Single(t=>!t.ReadOnly);var button=controls.OfType<Button>().Single(b=>b.Text=="تفعيل البرنامج");
                            var payload=LicenseFormat.CreatePayload(hardware.ReadClaims(),"ui-test",Guid.NewGuid(),DateTime.UtcNow);
                            var text=payload+"|"+Convert.ToBase64String(rsa.SignData(LicenseFormat.Bytes(payload),"SHA256"));input.Text=text;
                            button.PerformClick();Assert.True(dialog.Visible);Assert.Equal(text,input.Text);Assert.True(button.Enabled);Assert.False(service.Check().IsValid);Assert.Equal(2,controls.OfType<Button>().Count());
                            button.PerformClick();Assert.True(service.Check().IsValid);Assert.Equal(DialogResult.OK,dialog.DialogResult);
                        }
                    }
                }
                catch(Exception exception){error=exception;}
            });
            thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(30)));if(error!=null)throw new Exception("UAC dialog retry test failed",error);
        }
        [Theory]
        [InlineData(1f)]
        [InlineData(1.25f)]
        [InlineData(1.5f)]
        public void Compact_layout_keeps_three_actions_inside_1366_by_768_at_scaled_DPI(float scale)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var service = new LicenseService(new MemoryStorage(),new RsaLicenseSignatures(new Dictionary<string,string>()),new Hardware());
                    using(var dialog = new ActivationForm(service, new DirectTestInstaller(service)))
                    {
                        dialog.Show(); dialog.Scale(new SizeF(scale,scale)); System.Windows.Forms.Application.DoEvents();
                        Assert.True(dialog.Width <= 1366 && dialog.Height <= 768);
                        foreach(var control in Descendants(dialog).Where(c => c is Button || (c is TextBox box && !box.ReadOnly)))
                        {
                            var bounds=dialog.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                            Assert.True(bounds.Left>=0 && bounds.Top>=0 && bounds.Right<=dialog.ClientSize.Width && bounds.Bottom<=dialog.ClientSize.Height);
                        }
                        Assert.False(Descendants(dialog).OfType<FlowLayoutPanel>().Single().VerticalScroll.Visible);
                        var preview=Environment.GetEnvironmentVariable("ALKHAYAL_ACTIVATION_PREVIEW_PATH");
                        if(!string.IsNullOrEmpty(preview))
                            using(var bitmap=new Bitmap(dialog.Width,dialog.Height))
                            { dialog.DrawToBitmap(bitmap,new Rectangle(Point.Empty,dialog.Size));bitmap.Save(Path.Combine(Path.GetDirectoryName(preview),"activation-dpi-"+(int)(scale*100)+".png")); }
                        dialog.Width = dialog.MinimumSize.Width;
                        System.Windows.Forms.Application.DoEvents();
                        var viewport = Descendants(dialog).OfType<FlowLayoutPanel>().Single();
                        Assert.False(viewport.HorizontalScroll.Visible);
                        Assert.True(viewport.VerticalScroll.Visible);
                        var contact = Descendants(dialog).OfType<LinkLabel>().Single();
                        Assert.True(contact.Focus());
                        var contactBounds = viewport.RectangleToClient(contact.RectangleToScreen(contact.ClientRectangle));
                        Assert.True(viewport.ClientRectangle.Contains(contactBounds));
                    }
                }
                catch(Exception error){failure=error;}
            });
            thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
            if(failure!=null)throw new Exception("Activation scaled layout failed",failure);
        }
        private static IEnumerable<Control> Descendants(Control parent)
        { foreach(Control child in parent.Controls) { yield return child; foreach(var nested in Descendants(child)) yield return nested; } }
        private static Label Alert(Form dialog) =>
            Descendants(dialog).OfType<Label>().Single(label => label.AccessibleRole == AccessibleRole.Alert);

        private sealed class PendingInstaller : ILicenseInstaller
        {
            public int Calls { get; private set; }
            public TaskCompletionSource<LicenseInstallResult> Completion { get; } = new TaskCompletionSource<LicenseInstallResult>();

            public Task<LicenseInstallResult> InstallAsync(string text)
            {
                Calls++;
                return Completion.Task;
            }
        }
        private sealed class MemoryStorage : ILicenseStorage { public string Text; public string Read() => Text; public void Write(string text) { Text = text; } }
        private sealed class RetryTestInstaller:ILicenseInstaller
        {
            private readonly LicenseService service;private readonly LicenseInstallResult failure;private int calls;
            public RetryTestInstaller(LicenseService service,LicenseInstallResult failure){this.service=service;this.failure=failure;}
            public System.Threading.Tasks.Task<LicenseInstallResult> InstallAsync(string text)=>System.Threading.Tasks.Task.FromResult(++calls==1?failure:service.Activate(text).IsValid?LicenseInstallResult.Success:LicenseInstallResult.StorageFailure);
        }
        private sealed class DirectTestInstaller : ILicenseInstaller
        {
            private readonly LicenseService service;
            public DirectTestInstaller(LicenseService service) { this.service = service; }
            public System.Threading.Tasks.Task<LicenseInstallResult> InstallAsync(string text) => System.Threading.Tasks.Task.FromResult(service.Activate(text).IsValid ? LicenseInstallResult.Success : LicenseInstallResult.StorageFailure);
        }
        private sealed class Hardware : IHardwareIdentity { public IDictionary<string,string> ReadClaims() => LicenseFormat.HashClaims(new Dictionary<string,string>{{"uuid","ui-fake-uuid"},{"board","ui-fake-board"},{"bios","ui-fake-bios"}}); }
    }
}
