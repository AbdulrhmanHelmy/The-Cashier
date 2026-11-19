using System;
using System.Drawing;
using System.IO;
using AlKhayal.Application.Licensing;
using AlKhayal.Data.Licensing;
using System.Threading.Tasks;
using System.Windows.Forms;
using AlKhayal.Server.Host;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AlKhayal.UI
{
    public sealed class MainForm : Form
    {
        private readonly WebView2 _browser = new WebView2();
        private readonly Label _status = new Label();
        private AlKhayalServer _server;
        private readonly LicenseService _licensing;
        private readonly TrialSecurityService _trial;
        private readonly LinkLabel _trialCountdown = new LinkLabel { Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.FromArgb(18, 35, 58), LinkColor = Color.FromArgb(218, 185, 111), ActiveLinkColor = Color.White, Visible = false, AccessibleName = "الفترة التجريبية، تفعيل الآن" };
        private readonly Timer _trialTimer = new Timer { Interval = 60000 };
        private readonly SplashForm _splash;
        private readonly DesktopCredentialStore _credentials = new DesktopCredentialStore();
        private bool _credentialsBridgeAdded;
        private bool _backupDialogsAdded;
        private bool _exportDialogsAdded;
        private bool _pdfOpenerAdded;
        private FileSystemWatcher _licenseWatcher;
        private bool _activationOpen;
        private bool _startupCompleted;
        private bool _exitConfirmed;
        private bool _exitDialogOpen;
        private readonly StartupRecoveryState _startupRecovery = new StartupRecoveryState();
        private StartupFailureForm _failureView;
        private readonly Timer _startupTimeout = new Timer { Interval = 30000 };
        private Uri _allowedOrigin;
        private FileSystemEventHandler _licenseChangedHandler;
        private RenamedEventHandler _licenseRenamedHandler;
        private ErrorEventHandler _licenseErrorHandler;
        private EventHandler<CoreWebView2NavigationStartingEventArgs> _navigationStartingHandler;

        public MainForm(LicenseService licensing)
            : this(licensing, MachineLicensing.CreateTrial(new HostOptions().DatabasePath, licensing), null)
        {
        }

        internal MainForm(LicenseService licensing, TrialSecurityService trial, SplashForm splash)
        {
            _licensing = licensing ?? throw new ArgumentNullException(nameof(licensing));
            _trial = trial ?? throw new ArgumentNullException(nameof(trial));
            _splash = splash;
            Text = "The Cashier";
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath)
                ?? throw new InvalidOperationException("تعذر تحميل أيقونة تطبيق The Cashier.");
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            ClientSize = new Size(1280, 800);
            MinimumSize = new Size(900, 600);
            AutoScaleMode = AutoScaleMode.Dpi;
            RightToLeft = RightToLeft.Yes;
            BackColor = Color.White;
            WindowState = FormWindowState.Maximized;

            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleCenter;
            _status.Text = "جارٍ الاتصال بالخادم…";
            _splash?.SetStage("جارٍ تشغيل الخادم المحلي");
            Controls.Add(_status);

            Activated += (s, e) => RequireActivation();
            _trialTimer.Tick += (s, e) => { UpdateTrialCountdown(); RequireActivation(); };
            _trialCountdown.LinkClicked += (s, e) => { using (var form = new ActivationForm(_licensing)) form.ShowDialog(this); UpdateTrialCountdown(); };
            _startupTimeout.Tick += StartupTimeout;
            Shown += async (sender, args) => await InitializeAsync();
            FormClosing += ConfirmExit;
            FormClosed += (sender, args) =>
            {
                _splash?.CloseSplash();
                _browser.Dispose();
                _startupTimeout.Dispose();
                _trialTimer.Dispose();
                _licenseWatcher?.Dispose();
                _server?.Dispose();
            };
        }

        private void ConfirmExit(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason != CloseReason.UserClosing || _exitConfirmed || _exitDialogOpen
                || !_startupRecovery.ShouldConfirmExit(true))
                return;

            _exitDialogOpen = true;
            try
            {
                using (var confirmation = new ExitConfirmationForm())
                {
                    if (confirmation.ShowDialog(this) == DialogResult.OK)
                    {
                        _exitConfirmed = true;
                        return;
                    }
                }

                e.Cancel = true;
            }
            finally
            {
                _exitDialogOpen = false;
            }
        }

        private async Task InitializeAsync()
        {
            if (_startupRecovery.Retrying)
                return;
            _startupRecovery.BeginRetry();
            _failureView = null;
            Controls.Clear();
            Controls.Add(_status);
            _status.Text = "جارٍ الاتصال بالخادم…";
            try
            {
                if (!_trial.Check().IsValid) { Close(); return; }
                EnsureLicenseWatcher();
                var runtime = CoreWebView2Environment.GetAvailableBrowserVersionString();
                _splash?.SetStage("جارٍ تجهيز واجهة الدخول");
                if (string.IsNullOrWhiteSpace(runtime))
                {
                    throw new InvalidOperationException("لم يتم العثور على WebView2 Runtime.");
                }

                var localApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var dataDirectory = WebView2UserDataFolderResolver.Resolve(
                    Environment.GetEnvironmentVariable("THECASHIER_WEBVIEW2_USER_DATA_FOLDER"),
                    localApplicationDataPath);
                WebView2UserDataFolderResolver.EnsureDirectory(dataDirectory);

                var url = Environment.GetEnvironmentVariable("THECASHIER_SERVER_URL");
                if (string.IsNullOrWhiteSpace(url))
                {
                    _server = new AlKhayalServer(_trial);
                    _server.Start(new HostOptions());
                    url = _server.Options.Url;
                }

                _allowedOrigin = new Uri(url, UriKind.Absolute);
                if (_browser.CoreWebView2 == null)
                {
                    var environment = await CoreWebView2Environment.CreateAsync(null, dataDirectory);
                    await _browser.EnsureCoreWebView2Async(environment);
                }
                if (!_credentialsBridgeAdded) { _browser.CoreWebView2.AddHostObjectToScript("alkhayalCredentials", _credentials); _credentialsBridgeAdded = true; }
                if (_server != null && !_backupDialogsAdded)
                {
                    _browser.CoreWebView2.AddHostObjectToScript("alkhayalBackupDialogs", new DesktopBackupDialogs(this, () => _server));
                    await _browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.AlKhayalHasBackupDialogs = true;");
                    _backupDialogsAdded = true;
                }
                if (_server != null && !_exportDialogsAdded)
                {
                    _browser.CoreWebView2.AddHostObjectToScript("alkhayalExportDialogs", new DesktopExportDialogs(this));
                    await _browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.AlKhayalHasExportDialogs = true;");
                    _exportDialogsAdded = true;
                }
                if (!_pdfOpenerAdded)
                {
                    _browser.CoreWebView2.AddHostObjectToScript("alkhayalPdf", new DesktopPdfOpener());
                    await _browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.AlKhayalHasPdfOpener = true;");
                    _pdfOpenerAdded = true;
                }
                // Frontend files and the screen registry must be from the same deployment version.
                // This preserves cookies/local storage while preventing an older cached shell from
                // consuming a newer no-store screens.json and missing newly registered renderers.
                await _browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.clearBrowserCache", "{}");
                _browser.Dock = DockStyle.Fill;
                _browser.Margin = Padding.Empty;
                _browser.ZoomFactor = 1.0;

                var settings = _browser.CoreWebView2.Settings;
                settings.IsZoomControlEnabled = false;
                settings.IsPinchZoomEnabled = false;
                settings.IsStatusBarEnabled = false;
                settings.AreBrowserAcceleratorKeysEnabled = true;
                settings.AreDevToolsEnabled = false;
                settings.AreDefaultContextMenusEnabled = false;

                // This is scoped to the desktop WebView2 host. Browser users retain normal zoom accessibility.
                await _browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    "document.addEventListener('wheel',function(e){if(e.ctrlKey)e.preventDefault();},{passive:false});" +
                    "document.addEventListener('keydown',function(e){if(e.ctrlKey && (e.key==='+' || e.key==='-' || e.key==='=' || e.key==='0'))e.preventDefault();},true);");

                if (_navigationStartingHandler == null)
                {
                    _navigationStartingHandler = (sender, args) =>
                    {
                        var target = new Uri(args.Uri, UriKind.Absolute);
                        args.Cancel = target.Scheme != _allowedOrigin.Scheme
                            || !string.Equals(target.Host, _allowedOrigin.Host, StringComparison.OrdinalIgnoreCase)
                            || target.Port != _allowedOrigin.Port;
                    };
                    _browser.CoreWebView2.NavigationStarting += _navigationStartingHandler;
                    _browser.NavigationCompleted += BrowserNavigationCompleted;
                }

                if (!_trial.Check().IsValid) RequireActivation();
                if (IsDisposed) return;
                Controls.Clear();
                Controls.Add(_browser);
                Controls.Add(_trialCountdown);
                UpdateTrialCountdown();
                _trialTimer.Start();
                _browser.Source = new Uri(_allowedOrigin, "login.html");
                _startupTimeout.Start();
                _startupRecovery.Complete();
            }
            catch (Exception exception)
            {
                _splash?.CloseSplash();
                StartupFailureReporter.TryLog(exception);
                CleanupFailedInitialization();
                ShowStartupFailure();
            }
        }

        private void EnsureLicenseWatcher()
        {
            if (_licenseWatcher != null)
                return;
            if (!Directory.Exists(Path.GetDirectoryName(FileLicenseStorage.DefaultPath)))
                return; // A first trial may precede administrator initialization of the license directory.

            _licenseWatcher = new FileSystemWatcher(Path.GetDirectoryName(FileLicenseStorage.DefaultPath))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Security
            };
            _licenseChangedHandler = LicenseChanged;
            _licenseRenamedHandler = LicenseChanged;
            _licenseErrorHandler = (s, e) =>
            {
                StartupFailureReporter.TryLog(e.GetException());
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(new Action(() => RequireActivation(true)));
            };
            _licenseWatcher.Changed += _licenseChangedHandler;
            _licenseWatcher.Deleted += _licenseChangedHandler;
            _licenseWatcher.Created += _licenseChangedHandler;
            _licenseWatcher.Renamed += _licenseRenamedHandler;
            _licenseWatcher.Error += _licenseErrorHandler;
            _licenseWatcher.EnableRaisingEvents = true;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            var virtualKey = (uint)(keyData & Keys.KeyCode);
            var controlPressed = (keyData & Keys.Control) == Keys.Control;
            if (WebViewKeyboardPolicy.ShouldBlockRefresh(virtualKey, controlPressed, true))
                return true;

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void CleanupFailedInitialization()
        {
            if (_server == null)
                return;

            _server.Dispose();
            _server = null;
        }

        private void ShowStartupFailure()
        {
            _startupTimeout.Stop();
            _startupRecovery.RecordFailure();
            var failure = new StartupFailureForm();
            _failureView = failure;
            failure.FormClosed += (s, e) =>
            {
                if (failure.DialogResult == DialogResult.Retry)
                    BeginInvoke(new Action(async () => await InitializeAsync()));
                else if (failure.DialogResult != DialogResult.Retry)
                {
                    _startupRecovery.RequestClose();
                    _exitConfirmed = true;
                    Close();
                }
            };
            Controls.Clear();
            Controls.Add(new Panel { Dock = DockStyle.Fill });
            failure.Show(this);
        }

        private void StartupTimeout(object sender, EventArgs e)
        {
            _startupTimeout.Stop();
            if (_startupCompleted || IsDisposed)
                return;

            var exception = new TimeoutException("Startup navigation did not complete within 30 seconds.");
            StartupFailureReporter.TryLog(exception);
            CleanupFailedInitialization();
            ShowStartupFailure();
        }

        private void BrowserNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (_startupCompleted || IsDisposed) return;
            if (!args.IsSuccess)
            {
                _splash?.CloseSplash();
                var source = _browser.Source == null ? "(null)" : _browser.Source.AbsoluteUri;
                var diagnostic = "WebView2 navigation failed: " + args.WebErrorStatus
                    + "; HTTP=" + args.HttpStatusCode
                    + "; URI=" + source;
                if (_server != null)
                {
                    var frontendPath = _server.Options.FrontendPath;
                    diagnostic += "; FrontendPath=" + frontendPath
                        + "; LoginPageExists=" + File.Exists(Path.Combine(frontendPath, "login.html"));
                }
                var exception = new InvalidOperationException(diagnostic);
                StartupFailureReporter.TryLog(exception);
                CleanupFailedInitialization();
                ShowStartupFailure();
                return;
            }

            _startupCompleted = true;
            _startupTimeout.Stop();
            _splash?.CloseSplash();
        }

        private void LicenseChanged(object sender, FileSystemEventArgs args)
        {
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(new Action(() => RequireActivation()));
        }

        private void RequireActivation(bool watcherFailed = false)
        {
            if (_activationOpen || IsDisposed || (!watcherFailed && _server?.IsRestoring == true)) return;
            if (!watcherFailed && _trial.Check().IsValid) return;
            _activationOpen = true;
            _browser.Visible = false;
            try
            {
                if (watcherFailed)
                {
                    MessageBox.Show(this, "تعذر مراقبة ملف التفعيل بأمان. أعد تشغيل البرنامج؛ وإذا تكررت المشكلة تواصل مع الدعم.", "التفعيل", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Close(); return;
                }
                var state = _trial.Evaluate().State;
                if (state == TrialState.Active || state == TrialState.PermanentlyActivated)
                { _browser.Visible = true; return; }
                if (state == TrialState.NotStarted)
                {
                    using (var activation = new ActivationForm(_licensing, null, _trial))
                        if (activation.ShowDialog(this) != DialogResult.OK || !_trial.Check().IsValid) { Close(); return; }
                }
                else
                {
                    using (var expired = new TrialAccessForm(_licensing, _trial.Evaluate()))
                        if (expired.ShowDialog(this) != DialogResult.OK || !_trial.Check().IsValid) { Close(); return; }
                }
                if (_browser.CoreWebView2 != null && _browser.Source != null)
                    _browser.CoreWebView2.Navigate(new Uri(new Uri(_browser.Source.GetLeftPart(UriPartial.Authority)), "login.html").AbsoluteUri);
                _browser.Visible = true;
                UpdateTrialCountdown();
            }
            finally { _activationOpen = false; }
        }

        private void UpdateTrialCountdown()
        {
            if (_server?.IsRestoring == true) return;
            var result = _trial.Evaluate();
            _trialCountdown.Visible = result.State == TrialState.Active;
            if (!_trialCountdown.Visible) return;
            var minutes = Math.Min(7200, (int)Math.Ceiling(result.Remaining.TotalMinutes));
            var days = minutes / 1440; var hours = (minutes % 1440) / 60; var remainder = minutes % 60;
            var remaining = days > 0 ? days + " " + (days == 1 ? "يوم" : "أيام") + (hours > 0 ? " و" + hours + " ساعة" : "") :
                hours > 0 ? hours + " ساعة" + (remainder > 0 ? " و" + remainder + " دقيقة" : "") : remainder + " دقيقة";
            _trialCountdown.Text = "الفترة التجريبية — متبقي " + remaining + " — تفعيل الآن";
        }
    }
}

