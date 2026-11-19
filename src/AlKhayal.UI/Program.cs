using System;
using System.Windows.Forms;
using AlKhayal.Data.Licensing;
namespace AlKhayal.UI
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Narrow helper mode exits before UI/WebView2/host/database composition.
            if (args.Length > 0 && args[0] == AlKhayal.Application.Licensing.LicenseInstallCommand.Switch)
                return LicenseHelperEntry.Execute(args);
            if (args.Length > 0 && (args.Length != 1 || args[0] != "--initialize-license-storage"))
                return (int)AlKhayal.Application.Licensing.LicenseInstallResult.InvalidLicense;
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            SplashForm splash = null;
            while (true)
            {
                try
                {
                    splash = new SplashForm { TopMost = true };
                    splash.Show();
                    System.Windows.Forms.Application.DoEvents();
                    splash.SetStage("جارٍ التحقق من التفعيل");
                    if (args.Length == 1 && args[0] == "--initialize-license-storage")
                    {
                        FileLicenseStorage.InitializeMachineDirectory();
                        MessageBox.Show("تم إعداد مجلد التراخيص. شغّل البرنامج بصورة عادية؛ سيطلب صلاحية حفظ التفعيل عند الحاجة فقط."); return 0;
                    }
                    var licensing = MachineLicensing.Create();
                    var trial = MachineLicensing.CreateTrial(new AlKhayal.Server.Host.HostOptions().DatabasePath, licensing);
                    while (!trial.Check().IsValid)
                    {
                        splash?.CloseSplash();
                        splash = null;
                        var state = trial.Evaluate().State;
                        if (state == AlKhayal.Application.Licensing.TrialState.Active || state == AlKhayal.Application.Licensing.TrialState.PermanentlyActivated) continue;
                        if (state == AlKhayal.Application.Licensing.TrialState.NotStarted)
                        {
                            using (var dialog = new ActivationForm(licensing, null, trial))
                                if (dialog.ShowDialog() != DialogResult.OK) return 0;
                        }
                        else
                        {
                            using (var dialog = new TrialAccessForm(licensing, trial.Evaluate()))
                                if (dialog.ShowDialog() != DialogResult.OK) return 0;
                        }
                    }
                    if (!trial.Check().IsValid) return 0;
                    // The activation dialog closes the splash before showing itself.
                    // Recreate it after a successful trial start or permanent activation.
                    if (splash == null || splash.IsDisposed)
                    {
                        splash = new SplashForm { TopMost = true };
                        splash.Show();
                        System.Windows.Forms.Application.DoEvents();
                    }
                    splash.SetStage("جارٍ تجهيز واجهة النظام");
                    System.Windows.Forms.Application.Run(new MainForm(licensing, trial, splash));
                    return 0;
                }
                catch (Exception e)
                {
                    splash?.CloseSplash();
                    splash = null;
                    StartupFailureReporter.TryLog(e);
                    using (var failure = new StartupFailureForm())
                        if (failure.ShowDialog() != DialogResult.Retry)
                            return 1;
                }
            }
        }
    }
}
