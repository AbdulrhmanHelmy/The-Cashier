using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AlKhayal.Server.Host;

namespace AlKhayal.UI
{
    // Available only in the local WebView. Backup authorization and file writes
    // still go through the API. The bridge can open only the configured folder.
    [ComVisible(true)]
    public sealed class DesktopBackupDialogs
    {
        private readonly Form owner;
        private readonly Func<AlKhayalServer> server;

        internal DesktopBackupDialogs(Form owner, Func<AlKhayalServer> server)
        {
            this.owner = owner;
            this.server = server;
        }

        public string pickFolder() => OnUiThread(() =>
        {
            if (server() == null) return null;
            using (var dialog = new FolderBrowserDialog
            {
                Description = "اختر مجلد النسخ الاحتياطية",
                ShowNewFolderButton = true
            })
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : "";
        });

        public bool openConfiguredFolder() => OnUiThread(() =>
        {
            var path = server()?.BackupDirectory;
            if (!Directory.Exists(path)) return false;
            using (Process.Start(new ProcessStartInfo { FileName = path, Verb = "open", UseShellExecute = true })) { }
            return true;
        });

        private T OnUiThread<T>(Func<T> action) => owner.InvokeRequired ? (T)owner.Invoke(action) : action();
    }
}
