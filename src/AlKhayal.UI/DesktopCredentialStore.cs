using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;

namespace AlKhayal.UI
{
    // Exposed only to the application's WebView2 origin. Values are DPAPI CurrentUser scoped.
    [ComVisible(true)]
    public sealed class DesktopCredentialStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TheCashier.POS/DesktopCredentialStore/v1");
        private readonly string _directory;
        public DesktopCredentialStore()
        {
            _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TheCashier", "Credentials");
        }
        public string get(string username)
        {
            var path = PathFor(username); if (path == null || !File.Exists(path)) return null;
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser)); }
            catch (CryptographicException) { return null; }
        }
        public void save(string username, string password)
        {
            var path = PathFor(username); if (path == null || password == null) return;
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser));
        }
        public void remove(string username) { var path = PathFor(username); if (path != null && File.Exists(path)) File.Delete(path); }
        private string PathFor(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;
            using (var sha = SHA256.Create()) return Path.Combine(_directory, BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(username.Trim().ToUpperInvariant()))).Replace("-", "") + ".bin");
        }
    }
}
