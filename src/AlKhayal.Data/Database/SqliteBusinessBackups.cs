using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using AlKhayal.Application.Backups;
using AlKhayal.Data.Database;

using Dapper;

namespace AlKhayal.Data.Backups
{
    public sealed class SqliteBusinessBackups : IBusinessBackups, IDisposable
    {
        private const long MaxDatabase = 2L * 1024 * 1024 * 1024;
        private const long MaxArchive = MaxDatabase + 16 * 1024 * 1024;
        private const string DatabaseEntry = "business.sqlite";
        private const string ManifestEntry = "manifest.json";
        private const string TrialColumns = "Singleton,TrialId,DeviceFingerprint,StartedAtUtc,ExpiresAtUtc,LastSeenUtc,HighestObservedUtc,Status,StateRevision,IntegrityMetadata";
        private readonly string database;
        private readonly string work;
        private readonly string statePath;
        private readonly SemaphoreSlim operations = new SemaphoreSlim(1);
        private readonly Func<DateTime> utcNow;
        private Timer timer;
        private int stopped;
        private State state;
        private DateTime nextAttempt;
        private PendingRestore pendingRestore;

        public SqliteBusinessBackups(string databasePath, Func<DateTime> utcNow = null)
        {
            database = Path.GetFullPath(databasePath);
            work = database + ".backup-work";
            statePath = database + ".backup-settings.json";
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
            state = new State
            {
                Installation = Guid.NewGuid(),
                Configuration = new BackupConfiguration { Directory = Path.Combine(Path.GetDirectoryName(database), "Backups") }
            };
            try
            {
                if (File.Exists(statePath))
                {
                    if (new FileInfo(statePath).Length > 32768) throw Invalid();
                    var saved = ReadJson<State>(File.ReadAllText(statePath));
                    if (saved?.Configuration == null || saved.Installation == Guid.Empty ||
                        string.IsNullOrWhiteSpace(saved.Configuration.Directory)) throw Invalid();
                    state = saved;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                ex is ArgumentException || ex is InvalidOperationException || ex is BackupException)
            {
                Trace.TraceWarning("Backup configuration could not be loaded: {0}", ex.GetType().Name);
                state.LastFailureUtc = this.utcNow();
                state.LastError = "تعذر قراءة إعدادات النسخ. أُوقف النسخ التلقائي حتى تُحفظ الإعدادات مجددًا.";
            }
        }

        public string ConfiguredDirectory => LocalFolder(Volatile.Read(ref state).Configuration.Directory);

        public void Start()
        {
            CleanAbandonedWorkspaces();
            timer = new Timer(_ => RunScheduled(), null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref stopped, 1);
            var scheduled = Interlocked.Exchange(ref timer, null);
            if (scheduled != null)
            {
                using (var finished = new ManualResetEvent(false))
                    if (scheduled.Dispose(finished)) finished.WaitOne();
            }

            operations.Wait();
            try { ClearPending(); }
            finally { operations.Release(); }
        }

        public Task<BackupStatus> StatusAsync() => Run(() =>
        {
            BackupFile[] files;
            try
            {
                var folder = ConfiguredDirectory;
                files = Directory.Exists(folder)
                    ? new DirectoryInfo(folder).EnumerateFiles("*.alkbackup")
                        .Where(file => string.Equals(file.Extension, ".alkbackup", StringComparison.OrdinalIgnoreCase)
                            && (file.Attributes & FileAttributes.ReparsePoint) == 0)
                        .OrderByDescending(file => file.LastWriteTimeUtc).Take(100).Select(Describe).ToArray()
                    : Array.Empty<BackupFile>();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is BackupException || ex is ArgumentException)
            {
                Trace.TraceWarning("Backup history could not be listed: {0}", ex.GetType().Name);
                files = Array.Empty<BackupFile>();
            }

            return new BackupStatus
            {
                Configuration = state.Configuration, LastSuccess = state.LastSuccess,
                LastFailureUtc = state.LastFailureUtc, LastError = state.LastError, Files = files
            };
        });

        public Task<BackupStatus> ConfigureAsync(BackupConfiguration configuration) => ConfigureAndReadAsync(configuration);

        private async Task<BackupStatus> ConfigureAndReadAsync(BackupConfiguration configuration)
        {
            await Run(() =>
            {
                var disablingCurrent = configuration?.Enabled == false &&
                    string.Equals(configuration.Directory, state.Configuration.Directory, StringComparison.OrdinalIgnoreCase);
                var folder = disablingCurrent ? state.Configuration.Directory : LocalFolder(configuration?.Directory);
                if (!disablingCurrent)
                {
                    Directory.CreateDirectory(folder);
                    var probe = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".probe");
                    using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.DeleteOnClose)) { }
                }
                var updated = CopyState();
                if (!string.Equals(folder, state.Configuration.Directory, StringComparison.OrdinalIgnoreCase))
                    updated.LastAutomaticDay = null;
                updated.Configuration = new BackupConfiguration { Enabled = configuration.Enabled, Directory = folder };
                Save(updated);
                nextAttempt = DateTime.MinValue;
                return true;
            }).ConfigureAwait(false);
            return await StatusAsync().ConfigureAwait(false);
        }

        public Task<BackupFile> CreateAsync(string directory = null) =>
            Run(() => RecordBackup("manual", directory ?? state.Configuration.Directory));

        internal Task<BackupFile> CreateInventoryRecoveryAsync() =>
            Run(() => Publish("before-inventory-rebuild", Path.Combine(Path.GetDirectoryName(database), "InventoryBackups")));

        public Task<Stream> DownloadAsync(string name) => Run<Stream>(() =>
        {
            if (string.IsNullOrEmpty(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetFileName(name) != name ||
                !string.Equals(Path.GetExtension(name), ".alkbackup", StringComparison.OrdinalIgnoreCase))
                throw Invalid();
            var path = Path.Combine(LocalFolder(state.Configuration.Directory), name);
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw Invalid();
            return File.OpenRead(path);
        });

        public async Task<BackupManifest> InspectAsync(Stream input, CancellationToken ct)
        {
            // Reserve one staging slot without queueing large uploads.
            if (!await operations.WaitAsync(0, ct).ConfigureAwait(false))
                throw new BackupException("توجد عملية نسخ أو تحقق قيد التنفيذ. حاول مجددًا.");
            Workspace workspace = null;
            try
            {
                ClearPending();
                workspace = NewWorkspace();
                var archive = workspace.File("upload.alkbackup");
                var staged = workspace.File("restore.sqlite");
                using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int count;
                    while ((count = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) != 0)
                    {
                        total += count;
                        if (total > MaxArchive) throw Invalid();
                        await output.WriteAsync(buffer, 0, count, ct).ConfigureAwait(false);
                    }
                }
                var manifest = await Task.Run(() =>
                {
                    var result = Extract(archive, staged);
                    ValidateAgainstLive(staged, result);
                    return result;
                }, ct).ConfigureAwait(false);
                DeleteTemporary(archive);
                manifest.Confirmation = Guid.NewGuid().ToString("N");
                pendingRestore = new PendingRestore { Workspace = workspace, Manifest = manifest, Expires = utcNow().AddMinutes(10) };
                workspace = null;
                return manifest;
            }
            finally
            {
                workspace?.Dispose();
                operations.Release();
            }
        }

        public Task<BackupFile> RestoreAsync(string confirmation, CancellationToken ct) => Run(() =>
        {
            if (pendingRestore == null || pendingRestore.Expires <= utcNow() ||
                !string.Equals(confirmation, pendingRestore.Manifest.Confirmation, StringComparison.Ordinal))
                throw new BackupException("انتهت صلاحية التأكيد أو تغير الملف. افحص النسخة وأكد الاستعادة مجددًا.");

            var staged = pendingRestore.Workspace.File("restore.sqlite");
            try
            {
                if (Hash(staged) != pendingRestore.Manifest.Sha256) throw Invalid();
                ValidateAgainstLive(staged, pendingRestore.Manifest);
                using (var compact = Open(staged)) compact.Execute("VACUUM");
                ct.ThrowIfCancellationRequested();
                var emergency = Publish("emergency", Path.Combine(Path.GetDirectoryName(database), "RestoreRecovery"));
                ct.ThrowIfCancellationRequested();
                // This is the same cross-process mutex used by trial evaluation and
                // checkpointing. Hold it until SQLite has committed the replacement.
                using (var trial = new AlKhayal.Data.Licensing.WindowsTrialOperationLock().TryAcquire(TimeSpan.FromSeconds(15)))
                {
                    if (trial == null) throw new BackupException("تعذر الحصول على قفل الصيانة. حاول مجددًا.");
                    ct.ThrowIfCancellationRequested();
                    ReplaceBusinessDatabase(staged);
                }
                return emergency;
            }
            finally { ClearPending(); }
        });

        private void ReplaceBusinessDatabase(string staged)
        {
            using (var current = Open(database))
            using (var restored = Open(staged))
            {
                restored.Execute("ATTACH DATABASE @path AS destination", new { path = database });
                try
                {
                    restored.Execute("INSERT INTO main.TrialState (" + TrialColumns + ") SELECT " +
                        TrialColumns + " FROM destination.TrialState");
                }
                finally { restored.Execute("DETACH DATABASE destination"); }

                current.Execute("PRAGMA synchronous=FULL");
                // The SQLite backup API commits the destination as one transaction.
                // Journal/WAL recovery leaves the original or complete replacement.
                Copy(restored, current);
            }
        }

        private void ClearPending()
        {
            pendingRestore?.Workspace.Dispose();
            pendingRestore = null;
        }

        public async Task<bool> RunScheduledAsync()
        {
            if (!await operations.WaitAsync(0).ConfigureAwait(false)) return false;
            try
            {
                return await Task.Run(() =>
                {
                    var now = utcNow();
                    if (pendingRestore != null && pendingRestore.Expires <= now) ClearPending();
                    var day = now.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    if (Volatile.Read(ref stopped) != 0 || !state.Configuration.Enabled || now < nextAttempt || day == state.LastAutomaticDay) return false;
                    nextAttempt = now.AddMinutes(5);
                    try
                    {
                        RecordBackup("auto", state.Configuration.Directory, day);
                        RetainAutomatic();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceError("Automatic backup failed: {0}", ex.GetType().Name);
                        return false;
                    }
                }).ConfigureAwait(false);
            }
            finally { operations.Release(); }
        }

        private void RunScheduled()
        {
            try { RunScheduledAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { Trace.TraceError("Backup scheduler failed: {0}", ex.GetType().Name); }
        }

        private BackupFile RecordBackup(string kind, string folder, string day = null)
        {
            BackupFile result;
            try { result = (day == null ? null : FindPublishedAutomatic(folder, day)) ?? Publish(kind, folder, day); }
            catch (Exception ex)
            {
                var failure = CopyState();
                failure.LastFailureUtc = utcNow();
                failure.LastError = ex is BackupException ? ex.Message : "تعذر إنشاء النسخة. تحقق من المجلد ومساحة القرص وصلاحيات الكتابة.";
                try { Save(failure); }
                catch (Exception saveError)
                {
                    Trace.TraceError("Could not persist backup failure status: {0}", saveError.GetType().Name);
                    state = failure;
                }
                throw;
            }

            var updated = CopyState();
            updated.LastSuccess = result;
            if (day != null) updated.LastAutomaticDay = day;
            try { Save(updated); }
            catch (Exception ex)
            {
                // Publication is already durable. Keep the current status and avoid
                // claiming that the archived data failed when only bookkeeping failed.
                Volatile.Write(ref state, updated);
                Trace.TraceError("Could not persist backup success status: {0}", ex.GetType().Name);
            }
            return result;
        }

        private string AutoPrefix => "alkhayal-auto-" + state.Installation.ToString("N") + "-";

        // Recover a publish completed just before a crash or metadata write failure.
        private BackupFile FindPublishedAutomatic(string folder, string day)
        {
            folder = LocalFolder(folder);
            if (!Directory.Exists(folder)) return null;
            var prefix = AutoPrefix + day.Replace("-", "") + "-";
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles(prefix + "*.alkbackup"))
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                try
                {
                    using (var workspace = NewWorkspace())
                    {
                        var staged = workspace.File("verify.sqlite");
                        var manifest = Extract(file.FullName, staged);
                        ValidateAgainstLive(staged, manifest);
                        return new BackupFile { Name = file.Name, Path = file.FullName, Size = file.Length, CreatedUtc = manifest.CreatedUtc };
                    }
                }
                catch (Exception ex) when (ex is BackupException || ex is IOException || ex is InvalidDataException ||
                    ex is SQLiteException || ex is ArgumentException || ex is InvalidOperationException || ex is UnauthorizedAccessException)
                { Trace.TraceWarning("Prior automatic archive could not be recovered: {0}", ex.GetType().Name); }
            }
            return null;
        }

        private void RetainAutomatic()
        {
            try
            {
                var files = new DirectoryInfo(LocalFolder(state.Configuration.Directory))
                    .EnumerateFiles(AutoPrefix + "*.alkbackup")
                    .Where(file => (file.Attributes & FileAttributes.ReparsePoint) == 0)
                    .Where(file => !string.Equals(file.FullName, state.LastSuccess.Path, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(file => file.Name, StringComparer.Ordinal).Skip(6).ToArray();
                foreach (var file in files) file.Delete();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is BackupException)
            { Trace.TraceWarning("Automatic backup retention failed: {0}", ex.GetType().Name); }
        }

        private BackupFile Publish(string kind, string folder, string day = null)
        {
            folder = LocalFolder(folder);
            Directory.CreateDirectory(folder);
            var id = Guid.NewGuid();
            var now = utcNow();
            var name = (day == null ? "alkhayal-" + kind + "-" : AutoPrefix + day.Replace("-", "") + "-") +
                now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + id.ToString("N") + ".alkbackup";
            var final = Path.Combine(folder, name);
            var pending = final + ".tmp";
            var ownsPending = false;
            using (var workspace = NewWorkspace())
            {
                var snapshot = workspace.File("snapshot.sqlite");
                var verification = workspace.File("verify.sqlite");
                try
                {
                    string schema;
                    using (var source = Open(database, true))
                    using (var clean = Open(snapshot, create: true))
                    {
                        if (source.ExecuteScalar<long>("PRAGMA page_count") * source.ExecuteScalar<long>("PRAGMA page_size") > MaxDatabase)
                            throw Invalid();
                        Copy(source, clean);
                        clean.Execute("PRAGMA journal_mode=DELETE; PRAGMA secure_delete=ON; DELETE FROM TrialState; DELETE FROM Sessions; VACUUM;");
                        CheckIntegrity(clean);
                        schema = Version(clean);
                    }
                    var manifest = new BackupManifest
                    {
                        FormatVersion = 1, ApplicationVersion = typeof(SqliteBusinessBackups).Assembly.GetName().Version.ToString(),
                        SchemaVersion = schema, CreatedUtc = now, Id = id, Component = DatabaseEntry,
                        Size = new FileInfo(snapshot).Length, Sha256 = Hash(snapshot)
                    };
                    using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        ownsPending = true;
                        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
                        {
                            using (var writer = new StreamWriter(zip.CreateEntry(ManifestEntry).Open()))
                                writer.Write(Json(manifest));
                            using (var entry = zip.CreateEntry(DatabaseEntry, CompressionLevel.Fastest).Open())
                            using (var input = File.OpenRead(snapshot))
                                input.CopyTo(entry);
                        }
                        output.Flush(true);
                    }
                    Extract(pending, verification);
                    using (var validated = Open(verification, true)) CheckIntegrity(validated);
                    var size = new FileInfo(pending).Length;
                    File.Move(pending, final);
                    ownsPending = false;
                    return new BackupFile { Name = name, Path = final, CreatedUtc = now, Size = size };
                }
                finally { if (ownsPending) DeleteTemporary(pending); }
            }
        }

        private void ValidateAgainstLive(string staged, BackupManifest manifest)
        {
            using (var candidate = Open(staged, true))
            using (var current = Open(database, true))
            {
                // The restore format accepts only the installed migration set and the
                // exact trusted table/index/view/trigger definitions, including internal tables.
                var migrations = MigrationNames(current);
                if (manifest.SchemaVersion != migrations.Last() ||
                    !migrations.SequenceEqual(MigrationNames(candidate)) ||
                    !Schema(current).SequenceEqual(Schema(candidate), StringComparer.Ordinal))
                    throw new BackupException("بنية قاعدة البيانات في النسخة لا تطابق الإصدار المثبت.");
                if (candidate.ExecuteScalar<long>("SELECT COUNT(*) FROM TrialState") != 0 ||
                    candidate.ExecuteScalar<long>("SELECT COUNT(*) FROM Sessions") != 0)
                    throw Invalid();
                CheckIntegrity(candidate);
            }
        }

        private static string[] MigrationNames(SQLiteConnection c) =>
            c.Query<string>("SELECT Name FROM SchemaVersion ORDER BY Name COLLATE BINARY").ToArray();

        private static string Version(SQLiteConnection c) => MigrationNames(c).Last();

        private static IEnumerable<string> Schema(SQLiteConnection c) =>
            c.Query<string>(@"SELECT quote(type)||'|'||quote(name)||'|'||quote(tbl_name)||'|'||quote(sql)
                FROM sqlite_schema ORDER BY type COLLATE BINARY,name COLLATE BINARY");

        private static void CheckIntegrity(SQLiteConnection c)
        {
            if (!c.Query<string>("PRAGMA integrity_check(1)").SequenceEqual(new[] { "ok" }) ||
                c.ExecuteScalar<long>("SELECT EXISTS(SELECT 1 FROM pragma_foreign_key_check)") != 0)
                throw Invalid();
        }

        private static SQLiteConnection Open(string path, bool readOnly = false, bool create = false)
        {
            var builder = new SQLiteConnectionStringBuilder
            {
                DataSource = path, Version = 3, FailIfMissing = !create, ReadOnly = readOnly,
                ForeignKeys = true, Pooling = false, DefaultTimeout = 15
            };
            var c = new SQLiteConnection(builder.ConnectionString);
            try
            {
                c.Open();
                c.Execute("PRAGMA trusted_schema=OFF");
                return c;
            }
            catch { c.Dispose(); throw; }
        }

        private static void Copy(SQLiteConnection source, SQLiteConnection destination)
        {
            var timeout = Stopwatch.StartNew();
            source.BackupDatabase(destination, "main", "main", 256,
                (sender, sourceName, dest, destName, pages, remaining, total, retry) =>
                {
                    if ((retry && timeout.Elapsed >= TimeSpan.FromSeconds(15)) || timeout.Elapsed >= TimeSpan.FromMinutes(10))
                        throw new IOException("Timed out waiting for SQLite backup to complete.");
                    return true;
                }, 50);
        }

        private static BackupManifest Extract(string path, string staged)
        {
            if (new FileInfo(path).Length > MaxArchive) throw Invalid();
            using (var input = File.OpenRead(path))
            {
                CheckZipDirectory(input);
                using (var zip = new ZipArchive(input, ZipArchiveMode.Read))
                    return ExtractEntries(zip, staged);
            }
        }

        private static BackupManifest ExtractEntries(ZipArchive zip, string staged)
        {
            if (zip.Entries.Count != 2) throw Invalid();
            var header = zip.Entries.FirstOrDefault(entry => entry.FullName == ManifestEntry);
            var data = zip.Entries.FirstOrDefault(entry => entry.FullName == DatabaseEntry);
            if (header == null || data == null || header.Length <= 0 || header.Length > 8192 ||
                data.Length <= 0 || data.Length > MaxDatabase) throw Invalid();

            BackupManifest manifest;
            using (var bytes = new MemoryStream())
            using (var content = header.Open())
            {
                CopyBounded(content, bytes, 8192);
                manifest = ReadJson<BackupManifest>(System.Text.Encoding.UTF8.GetString(bytes.ToArray()));
            }
            if (manifest == null || manifest.FormatVersion != 1 || manifest.Id == Guid.Empty ||
                manifest.Component != DatabaseEntry || manifest.Size != data.Length ||
                string.IsNullOrWhiteSpace(manifest.ApplicationVersion) || string.IsNullOrWhiteSpace(manifest.SchemaVersion) ||
                manifest.CreatedUtc == default(DateTime) || manifest.Sha256?.Length != 64) throw Invalid();

            using (var content = data.Open())
            using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                if (CopyBounded(content, output, manifest.Size) != manifest.Size) throw Invalid();
            if (!string.Equals(Hash(staged), manifest.Sha256, StringComparison.Ordinal)) throw Invalid();
            return manifest;
        }

        private static void CheckZipDirectory(Stream input)
        {
            // Format 1 uses a single-disk ZIP with exactly two entries, without
            // ZIP64 or an archive comment. Bound central-directory allocations.
            if (input.Length < 22) throw Invalid();
            input.Position = input.Length - 22;
            using (var reader = new BinaryReader(input, System.Text.Encoding.UTF8, true))
            {
                if (reader.ReadUInt32() != 0x06054b50 || reader.ReadUInt16() != 0 || reader.ReadUInt16() != 0 ||
                    reader.ReadUInt16() != 2 || reader.ReadUInt16() != 2) throw Invalid();
                long size = reader.ReadUInt32();
                long offset = reader.ReadUInt32();
                if (size > 8192 || offset + size != input.Length - 22 || reader.ReadUInt16() != 0) throw Invalid();
            }
            input.Position = 0;
        }

        private async Task<T> Run<T>(Func<T> action)
        {
            await operations.WaitAsync().ConfigureAwait(false);
            try { return await Task.Run(action).ConfigureAwait(false); }
            finally { operations.Release(); }
        }

        private void CleanAbandonedWorkspaces()
        {
            try
            {
                if (!Directory.Exists(work) || (File.GetAttributes(work) & FileAttributes.ReparsePoint) != 0) return;
                foreach (var directory in new DirectoryInfo(work).EnumerateDirectories())
                {
                    if (!Guid.TryParseExact(directory.Name, "N", out _) || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;
                    using (new Workspace(directory.FullName)) { }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { Trace.TraceWarning("Could not clean abandoned backup staging: {0}", ex.GetType().Name); }
        }

        private Workspace NewWorkspace()
        {
            Directory.CreateDirectory(LocalFolder(work));
            return new Workspace(Path.Combine(work, Guid.NewGuid().ToString("N")));
        }

        private static string LocalFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length < 3 || path[1] != ':' ||
                (path[2] != '\\' && path[2] != '/') || path.IndexOf(':', 2) >= 0)
                throw new BackupException("أدخل مسار مجلد محلي كامل على جهاز الخادم.");
            var full = Path.GetFullPath(path);
            var drive = new DriveInfo(Path.GetPathRoot(full));
            if (drive.DriveType == DriveType.Network)
                throw new BackupException("اختر قرصًا محليًا أو خارجيًا متصلًا بجهاز الخادم.");
            for (var parent = new DirectoryInfo(full); parent != null; parent = parent.Parent)
                if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new BackupException("اختر مجلدًا محليًا مباشرًا دون ارتباطات إلى مجلدات أخرى.");
            return full;
        }

        private State CopyState() => new State
        {
            Installation = state.Installation, Configuration = state.Configuration,
            LastSuccess = state.LastSuccess, LastAutomaticDay = state.LastAutomaticDay,
            LastFailureUtc = state.LastFailureUtc, LastError = state.LastError
        };

        private void Save(State updated)
        {
            var temp = statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(Json(updated));
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(statePath)) File.Replace(temp, statePath, null);
                else File.Move(temp, statePath);
                Volatile.Write(ref state, updated);
            }
            finally { DeleteTemporary(temp); }
        }

        private static long CopyBounded(Stream input, Stream output, long max)
        {
            var buffer = new byte[81920];
            long length = 0;
            int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                length += count;
                if (length > max) throw Invalid();
                output.Write(buffer, 0, count);
            }
            return length;
        }

        private static string Hash(string path)
        {
            using (var input = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }

        private static string Json(object value) => new JavaScriptSerializer().Serialize(value);
        private static T ReadJson<T>(string value) => new JavaScriptSerializer { RecursionLimit = 8 }.Deserialize<T>(value);
        private static BackupException Invalid() => new BackupException("ملف النسخة غير صالح أو تالف أو يتجاوز الحد المسموح.");
        private static BackupFile Describe(FileInfo file) => new BackupFile
        { Name = file.Name, Path = file.FullName, Size = file.Length, CreatedUtc = file.LastWriteTimeUtc };

        private static void DeleteTemporary(string path)
        {
            try { File.Delete(path); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { Trace.TraceWarning("Backup temporary file cleanup failed: {0}", ex.GetType().Name); }
        }

        public sealed class State
        {
            public Guid Installation { get; set; }
            public BackupConfiguration Configuration { get; set; }
            public BackupFile LastSuccess { get; set; }
            public DateTime? LastFailureUtc { get; set; }
            public string LastError { get; set; }
            public string LastAutomaticDay { get; set; }
        }

        private sealed class PendingRestore
        {
            public Workspace Workspace { get; set; }
            public BackupManifest Manifest { get; set; }
            public DateTime Expires { get; set; }
        }

        private sealed class Workspace : IDisposable
        {
            private readonly string path;
            public Workspace(string path) { this.path = path; Directory.CreateDirectory(path); }
            public string File(string name) => Path.Combine(path, name);
            public void Dispose()
            {
                try
                {
                    foreach (var name in new[] { "snapshot.sqlite", "verify.sqlite", "restore.sqlite", "upload.alkbackup" })
                        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
                            DeleteTemporary(File(name + suffix));
                    Directory.Delete(path);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                { Trace.TraceWarning("Backup workspace cleanup failed: {0}", ex.GetType().Name); }
            }
        }
    }
}
