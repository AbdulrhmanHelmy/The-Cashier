using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace AlKhayal.UI
{
    [ComVisible(true)]
    public sealed class DesktopPdfOpener
    {
        private const int MaxPdfBytes = 20 * 1024 * 1024;

        public bool open(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64) || base64.Length > (MaxPdfBytes / 3 + 1) * 4)
                throw new InvalidOperationException("ملف الفاتورة كبير جدًا أو غير صالح.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(base64); }
            catch (FormatException) { throw new InvalidOperationException("ملف الفاتورة غير صالح."); }
            if (bytes.Length < 8 || bytes.Length > MaxPdfBytes || bytes[0] != '%' || bytes[1] != 'P' || bytes[2] != 'D' || bytes[3] != 'F' || bytes[4] != '-')
                throw new InvalidOperationException("ملف الفاتورة غير صالح.");

            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TheCashier", "InvoicePdfPreviews");
            Directory.CreateDirectory(folder);
            DeleteOldPreviews(folder);
            var path = Path.Combine(folder, "invoice-" + Guid.NewGuid().ToString("N") + ".pdf");
            File.WriteAllBytes(path, bytes);
            try
            {
                // ShellExecute on a file URI uses the user's default browser; fall back
                // to the registered PDF app if this Windows configuration rejects it.
                try { Launch(new Uri(path).AbsoluteUri); }
                catch (Win32Exception) { Launch(path); }
                return true;
            }
            catch
            {
                File.Delete(path);
                throw;
            }
        }

        private static void Launch(string target)
        {
            using (var process = Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true }))
            {
                if (process == null) throw new InvalidOperationException("تعذر فتح ملف الفاتورة في المتصفح.");
            }
        }

        private static void DeleteOldPreviews(string folder)
        {
            foreach (var path in Directory.GetFiles(folder, "invoice-*.pdf"))
            {
                try { if (File.GetCreationTimeUtc(path) < DateTime.UtcNow.AddDays(-7)) File.Delete(path); }
                catch (IOException ex) { Trace.TraceWarning("Could not remove an old invoice PDF preview: {0}", ex.GetType().Name); }
                catch (UnauthorizedAccessException ex) { Trace.TraceWarning("Could not remove an old invoice PDF preview: {0}", ex.GetType().Name); }
            }
        }
    }
}
