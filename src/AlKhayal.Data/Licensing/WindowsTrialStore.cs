using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using AlKhayal.Application.Licensing;
using AlKhayal.Data.Database;
using Microsoft.Win32;

namespace AlKhayal.Data.Licensing
{
    public sealed class WindowsTrialStore : ITrialStore, ITrialSecurityEventSink
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TheCashierPOS|trial-state|1");
        private readonly string databasePath;
        private readonly string filePath;
        private readonly string registryPath;
        private readonly string securityPath;
        private bool loadedEventWritten;
        public static string DefaultFilePath => CashierStoragePaths.Trial;
        public static string DefaultSecurityPath => CashierStoragePaths.TrialSecurity;
        public WindowsTrialStore(string databasePath, string filePath = null, string registryPath = null, string securityPath = null)
        { this.databasePath = Path.GetFullPath(databasePath); this.filePath = Path.GetFullPath(filePath ?? DefaultFilePath); this.registryPath = registryPath ?? @"Software\TheCashier\Licensing";
          this.securityPath = Path.GetFullPath(securityPath ?? DefaultSecurityPath); }

        public TrialRecord[] ReadAll()
        {
            var records = new TrialRecord[4]; Exception failure = null;
            try { records[0] = ReadDatabase(); } catch (Exception e) { failure = e; }
            try { records[1] = ReadFile(); } catch (Exception e) { if (failure == null) failure = e; }
            try { records[2] = ReadRegistry(); } catch (Exception e) { if (failure == null) failure = e; }
            try { records[3] = ReadSecurityFile(); } catch (Exception e) { if (failure == null) failure = e; }
            try { VerifyEvents(); } catch (Exception e) { if (failure == null) failure = e; }
            if (failure == null && File.Exists(EventPath) && Array.TrueForAll(records, x => x == null))
                failure = new CryptographicException("Trial records are missing while security events survive.");
            if (failure != null)
            {
                if (!(failure is CryptographicException)) throw failure;
                throw new TrialStorageIntegrityException(records, failure);
            }
            if (!loadedEventWritten && records[3] != null)
            {
                AppendEvent("trial_state_loaded", records[3]);
                loadedEventWritten = true;
            }
            return records;
        }

        public void WriteAll(TrialRecord record)
        {
            var text = Seal(record);
            // Every store writes the same authenticated revision. A crash between writes
            // leaves only the immediately previous revision, which the service can reconcile.
            WriteSecurityFile(text);
            WriteDatabase(record, text);
            WriteFile(text);
            WriteRegistry(text);
            AppendEvent(record.Status == TrialState.Expired ? "trial_expired" : record.Status == TrialState.Invalidated ? "trial_invalidated" : record.StateRevision == 1 ? "trial_started" : "trial_checkpoint", record);
        }

        private string EventPath => securityPath + ".events";

        public void RecordEvent(string code, TrialRecord record)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > 64) throw new ArgumentException("Invalid trial event code.", nameof(code));
            Directory.CreateDirectory(Path.GetDirectoryName(securityPath));
            AppendEvent(code, record);
        }

        private void VerifyEvents()
        {
            if (!File.Exists(EventPath)) return; // Existing installation before event logging.
            if (new FileInfo(EventPath).Length > 131072) throw new CryptographicException("Trial event log is oversized.");
            var lines = File.ReadAllLines(EventPath, new UTF8Encoding(false, true));
            if (lines.Length > 128) throw new CryptographicException("Trial event log is oversized.");
            var previous = "GENESIS";
            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length != 3 || parts[0] != previous) throw new CryptographicException("Trial event chain is broken.");
                var protectedBytes = Convert.FromBase64String(parts[1]);
                var payload = Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.LocalMachine));
                if (!payload.StartsWith("ALKEVENT|1|", StringComparison.Ordinal) || parts[2] != Hash(previous + "|" + parts[1]))
                    throw new CryptographicException("Trial event integrity failed.");
                previous = parts[2];
            }
        }

        private void AppendEvent(string code, TrialRecord record)
        {
            VerifyEvents();
            var lines = File.Exists(EventPath) ? new System.Collections.Generic.List<string>(File.ReadAllLines(EventPath, Encoding.UTF8)) : new System.Collections.Generic.List<string>();
            if (lines.Count >= 128) lines.Clear(); // Bounded retention; a new chain starts here.
            var previous = lines.Count == 0 ? "GENESIS" : lines[lines.Count - 1].Split('|')[2];
            var payload = "ALKEVENT|1|" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "|" + code + "|" + (record == null ? string.Empty : record.TrialId) + "|" + (record == null ? "0" : record.StateRevision.ToString(CultureInfo.InvariantCulture));
            var sealedEvent = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(payload), Entropy, DataProtectionScope.LocalMachine));
            lines.Add(previous + "|" + sealedEvent + "|" + Hash(previous + "|" + sealedEvent));
            WriteAtomic(EventPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        }

        private static string Hash(string value)
        {
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }

        private TrialRecord ReadDatabase()
        {
            if (!File.Exists(databasePath)) return null;
            using (var connection = new SQLiteConnection(Db.ConnectionStringFor(databasePath)))
            {
                connection.Open();
                using (var exists = new SQLiteCommand("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='TrialState'", connection))
                    if (Convert.ToInt64(exists.ExecuteScalar()) == 0) return null;
                using (var cmd = new SQLiteCommand("SELECT TrialId,DeviceFingerprint,StartedAtUtc,ExpiresAtUtc,LastSeenUtc,HighestObservedUtc,Status,StateRevision,IntegrityMetadata FROM TrialState WHERE Singleton=1", connection))
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    var record = Unseal(reader.GetString(8));
                    if (reader.GetString(0) != record.TrialId || reader.GetString(1) != record.DeviceFingerprint ||
                        reader.GetString(2) != Format(record.StartedAtUtc) || reader.GetString(3) != Format(record.ExpiresAtUtc) ||
                        reader.GetString(4) != Format(record.LastSeenUtc) || reader.GetString(5) != Format(record.HighestObservedUtc) ||
                        reader.GetString(6) != record.Status.ToString() || reader.GetInt64(7) != record.StateRevision)
                        throw new CryptographicException("Trial database fields do not match integrity metadata.");
                    return record;
                }
            }
        }

        private void WriteDatabase(TrialRecord r, string proof)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath));
            Migrator.Run(Db.ConnectionStringFor(databasePath));
            using (var connection = new SQLiteConnection(Db.ConnectionStringFor(databasePath)))
            {
                connection.Open();
                using (var tx = connection.BeginTransaction())
                using (var cmd = new SQLiteCommand("INSERT OR REPLACE INTO TrialState(Singleton,TrialId,DeviceFingerprint,StartedAtUtc,ExpiresAtUtc,LastSeenUtc,HighestObservedUtc,Status,StateRevision,IntegrityMetadata) VALUES(1,@id,@fingerprint,@start,@expiry,@seen,@highest,@status,@revision,@proof)", connection, tx))
                {
                    cmd.Parameters.AddWithValue("@id", r.TrialId);
                    cmd.Parameters.AddWithValue("@fingerprint", r.DeviceFingerprint);
                    cmd.Parameters.AddWithValue("@start", Format(r.StartedAtUtc));
                    cmd.Parameters.AddWithValue("@expiry", Format(r.ExpiresAtUtc));
                    cmd.Parameters.AddWithValue("@seen", Format(r.LastSeenUtc));
                    cmd.Parameters.AddWithValue("@highest", Format(r.HighestObservedUtc));
                    cmd.Parameters.AddWithValue("@status", r.Status.ToString());
                    cmd.Parameters.AddWithValue("@revision", r.StateRevision);
                    cmd.Parameters.AddWithValue("@proof", proof);
                    cmd.ExecuteNonQuery(); tx.Commit();
                }
            }
        }

        private TrialRecord ReadFile()
        {
            if (!File.Exists(filePath)) return null;
            var info = new FileInfo(filePath);
            if (info.Length > 8192) throw new CryptographicException("Trial file is oversized.");
            try { return Unseal(File.ReadAllText(filePath, new UTF8Encoding(false, true))); }
            catch (DecoderFallbackException e) { throw new CryptographicException("Invalid trial file encoding.", e); }
        }

        private TrialRecord ReadSecurityFile()
        {
            if (!File.Exists(securityPath)) return null;
            var info = new FileInfo(securityPath);
            if (info.Length > 8192) throw new CryptographicException("Protected trial file is oversized.");
            try { return Unseal(File.ReadAllText(securityPath, new UTF8Encoding(false, true))); }
            catch (DecoderFallbackException e) { throw new CryptographicException("Invalid protected trial file encoding.", e); }
        }

        private void WriteSecurityFile(string text)
        {
            var directory = Path.GetDirectoryName(securityPath);
            Directory.CreateDirectory(directory);
            // Shared across Windows accounts. Administrators and SYSTEM retain full control;
            // standard authenticated users need writes for an unprivileged POS process.
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), FileSystemRights.Modify | FileSystemRights.Synchronize, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(directory, security);
            WriteAtomic(securityPath, text);
        }

        private void WriteFile(string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            WriteAtomic(filePath, text);
        }

        private static void WriteAtomic(string path, string text)
        {
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    var bytes = new UTF8Encoding(false).GetBytes(text);
                    stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private TrialRecord ReadRegistry()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(registryPath, false))
            {
                var value = key?.GetValue("TrialState", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (value != null && !(value is string)) throw new CryptographicException("Invalid trial Registry value type.");
                var text = value as string;
                return text == null ? null : Unseal(text);
            }
        }

        private void WriteRegistry(string text)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(registryPath))
                key.SetValue("TrialState", text, RegistryValueKind.String);
        }

        private static string Seal(TrialRecord r)
        {
            var value = string.Join("|", new[] { "ALKTRIAL", "1", r.TrialId, r.DeviceFingerprint,
                Format(r.StartedAtUtc), Format(r.ExpiresAtUtc), Format(r.LastSeenUtc), Format(r.HighestObservedUtc),
                r.Status.ToString(), r.StateRevision.ToString(CultureInfo.InvariantCulture) });
            return "ALKTRIAL|1|" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.LocalMachine));
        }

        private static TrialRecord Unseal(string envelope)
        {
            try
            {
                var parts = envelope.Split('|');
                if (parts.Length != 3 || parts[0] != "ALKTRIAL" || parts[1] != "1" || envelope.Length > 8192) throw new FormatException();
                var ciphertext = Convert.FromBase64String(parts[2]);
                if (Convert.ToBase64String(ciphertext) != parts[2]) throw new FormatException();
                var plaintext = new UTF8Encoding(false, true).GetString(ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.LocalMachine));
                var p = plaintext.Split('|');
                if (p.Length != 10 || p[0] != "ALKTRIAL" || p[1] != "1") throw new FormatException();
                var record = new TrialRecord { TrialId = p[2], DeviceFingerprint = p[3], StartedAtUtc = Parse(p[4]),
                    ExpiresAtUtc = Parse(p[5]), LastSeenUtc = Parse(p[6]), HighestObservedUtc = Parse(p[7]),
                    Status = (TrialState)Enum.Parse(typeof(TrialState), p[8], false), StateRevision = long.Parse(p[9], CultureInfo.InvariantCulture) };
                if (record.TrialId != Guid.Parse(record.TrialId).ToString("D") ||
                    Format(record.StartedAtUtc) != p[4] || Format(record.ExpiresAtUtc) != p[5] ||
                    Format(record.LastSeenUtc) != p[6] || Format(record.HighestObservedUtc) != p[7] ||
                    record.Status.ToString() != p[8] || record.StateRevision.ToString(CultureInfo.InvariantCulture) != p[9]) throw new FormatException();
                return record;
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is CryptographicException || e is OverflowException)
            { Trace.TraceError("Trial integrity check failed: {0}", e.GetType().Name); throw new CryptographicException("Invalid protected trial state.", e); }
        }

        private static string Format(DateTime utc) => utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        private static DateTime Parse(string value) => DateTime.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
