using System;
using System.Diagnostics;

namespace AlKhayal.Application.Licensing
{
    public enum TrialState
    {
        NotStarted,
        Active,
        Expired,
        Invalidated,
        PermanentlyActivated,
        // Transient access-denied states. They are never serialized as trial status.
        StorageUnavailable,
        RecoveryRequired
    }

    public sealed class TrialRecord
    {
        public int Version { get; set; } = 1;
        public string TrialId { get; set; }
        public string DeviceFingerprint { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public DateTime HighestObservedUtc { get; set; }
        public TrialState Status { get; set; }
        public long StateRevision { get; set; }
    }

    public sealed class TrialResult
    {
        public TrialState State { get; }
        public TimeSpan Remaining { get; }
        public string Reason { get; }
        public TrialResult(TrialState state, TimeSpan remaining, string reason = null)
        { State = state; Remaining = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining; Reason = reason; }
    }

    public interface ITrialClock
    {
        DateTime UtcNow { get; }
        TimeSpan Elapsed { get; }
    }

    public sealed class SystemTrialClock : ITrialClock
    {
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        public DateTime UtcNow => DateTime.UtcNow;
        public TimeSpan Elapsed => stopwatch.Elapsed;
    }

    public interface ITrialStore
    {
        TrialRecord[] ReadAll(); // Three slots: SQLite, protected file, Registry.
        void WriteAll(TrialRecord record);
    }

    public interface ITrialSecurityEventSink
    {
        void RecordEvent(string code, TrialRecord record);
    }

    public interface ITrialOperationLock
    {
        // Returns null on bounded contention. The lease releases ownership on dispose.
        IDisposable TryAcquire(TimeSpan timeout);
    }

    internal sealed class SessionTrialOperationLock : ITrialOperationLock
    {
        public IDisposable TryAcquire(TimeSpan timeout)
        {
            var mutex = new System.Threading.Mutex(false, @"Local\TheCashierTrialState");
            try
            {
                try { if (!mutex.WaitOne(timeout)) { mutex.Dispose(); return null; } }
                catch (System.Threading.AbandonedMutexException) { /* ownership was transferred */ }
                return new MutexLease(mutex);
            }
            catch { mutex.Dispose(); throw; }
        }
        private sealed class MutexLease : IDisposable
        {
            private System.Threading.Mutex mutex;
            public MutexLease(System.Threading.Mutex mutex) { this.mutex = mutex; }
            public void Dispose()
            {
                var owned = System.Threading.Interlocked.Exchange(ref mutex, null);
                if (owned == null) return;
                try { owned.ReleaseMutex(); } finally { owned.Dispose(); }
            }
        }
    }

    public sealed class TrialStorageIntegrityException : Exception
    {
        public TrialRecord[] Records { get; }
        public TrialStorageIntegrityException(TrialRecord[] records, Exception cause) : base("A protected trial layer failed integrity verification.", cause)
        { Records = records; }
    }

    public sealed class TrialSecurityService : ILicenseGate
    {
        private readonly ILicenseGate permanent;
        private readonly ITrialStore store;
        private readonly ITrialClock clock;
        private readonly string fingerprint;
        private readonly ITrialOperationLock operationLock;
        private readonly object sync = new object();
        private DateTime anchorUtc;
        private TimeSpan anchorElapsed;
        private bool anchored;
        private bool permanentLogged;
        public TrialSecurityService(ILicenseGate permanent, ITrialStore store, ITrialClock clock, string fingerprint, ITrialOperationLock operationLock = null)
        { this.permanent = permanent; this.store = store; this.clock = clock; this.fingerprint = fingerprint;
          this.operationLock = operationLock ?? new SessionTrialOperationLock(); }

        public LicenseStatus Check()
        {
            var result = Evaluate();
            return new LicenseStatus(result.State == TrialState.Active || result.State == TrialState.PermanentlyActivated,
                result.State == TrialState.Active ? "الفترة التجريبية نشطة." :
                result.State == TrialState.PermanentlyActivated ? "تم التفعيل المجاني والدائم." :
                result.State == TrialState.StorageUnavailable || result.State == TrialState.RecoveryRequired ?
                    "تعذر التحقق من حالة التجربة مؤقتًا. أعد المحاولة دون حذف بيانات البرنامج." :
                    "يلزم التفعيل الدائم المجاني.");
        }

        public TrialResult Start()
        {
            lock (sync)
            using (var lease = AcquireOperationLock())
            {
                if (lease == null) return new TrialResult(TrialState.StorageUnavailable, TimeSpan.Zero, "lock_timeout");
                if (permanent.Check().IsValid) { LogPermanent(); return new TrialResult(TrialState.PermanentlyActivated, TimeSpan.Zero); }
                TrialRecord[] records;
                try { records = store.ReadAll(); }
                catch (TrialStorageIntegrityException) { return Evaluate(); }
                catch (Exception e) { Trace.TraceError("Trial start read failed: {0}", e.GetType().Name); return new TrialResult(TrialState.StorageUnavailable, TimeSpan.Zero, "storage_read"); }
                if (records == null || records.Length < 3 || records.Length > 4) return new TrialResult(TrialState.RecoveryRequired, TimeSpan.Zero, "storage_count");
                if (Array.Exists(records, x => x != null)) return Evaluate();
                if (string.IsNullOrWhiteSpace(fingerprint)) return new TrialResult(TrialState.Invalidated, TimeSpan.Zero, "identity_unavailable");
                var now = clock.UtcNow;
                var record = new TrialRecord { TrialId = Guid.NewGuid().ToString("D"), DeviceFingerprint = fingerprint,
                    StartedAtUtc = now, ExpiresAtUtc = now.AddHours(120), LastSeenUtc = now,
                    HighestObservedUtc = now, Status = TrialState.Active, StateRevision = 1 };
                try { store.WriteAll(record); }
                catch (Exception e) { Trace.TraceError("Trial start write failed: {0}", e.GetType().Name); return new TrialResult(TrialState.StorageUnavailable, TimeSpan.Zero, "storage_write"); }
                anchorUtc = now; anchorElapsed = clock.Elapsed; anchored = true;
                return new TrialResult(TrialState.Active, TimeSpan.FromHours(120));
            }
        }

        public TrialResult Evaluate()
        {
            lock (sync)
            using (var lease = AcquireOperationLock())
            {
                if (lease == null) return new TrialResult(TrialState.StorageUnavailable, TimeSpan.Zero, "lock_timeout");
                if (permanent.Check().IsValid) { LogPermanent(); return new TrialResult(TrialState.PermanentlyActivated, TimeSpan.Zero); }
                TrialRecord[] records;
                try { records = store.ReadAll(); }
                catch (TrialStorageIntegrityException e)
                {
                    TrialRecord survivor = null;
                    foreach (var item in e.Records) if (item != null && (survivor == null || item.StateRevision > survivor.StateRevision)) survivor = item;
                    return Invalidate(survivor, "integrity_failure");
                }
                catch (Exception e) { Trace.TraceError("Trial read failed: {0}", e.GetType().Name); return new TrialResult(TrialState.StorageUnavailable, TimeSpan.Zero, "storage_read"); }
                if (records == null || records.Length < 3 || records.Length > 4) return new TrialResult(TrialState.RecoveryRequired, TimeSpan.Zero, "storage_count");
                TrialRecord latest = null;
                foreach (var item in records)
                {
                    if (item == null) continue;
                    if (item.Version != 1 || item.TrialId == null || item.DeviceFingerprint != fingerprint || item.StartedAtUtc.Kind != DateTimeKind.Utc ||
                        item.ExpiresAtUtc - item.StartedAtUtc != TimeSpan.FromHours(120) || item.StateRevision < 1 || item.LastSeenUtc < item.StartedAtUtc ||
                        item.HighestObservedUtc < item.LastSeenUtc ||
                        (item.Status != TrialState.Active && item.Status != TrialState.Expired && item.Status != TrialState.Invalidated))
                        return Invalidate(item, "record_invalid");
                    if (latest == null || item.StateRevision > latest.StateRevision) latest = item;
                }
                if (latest == null) return new TrialResult(TrialState.NotStarted, TimeSpan.Zero);
                foreach (var item in records)
                {
                    if (item == null) continue;
                    if (item.TrialId != latest.TrialId || item.StartedAtUtc != latest.StartedAtUtc || item.ExpiresAtUtc != latest.ExpiresAtUtc ||
                        latest.StateRevision - item.StateRevision > 1 || item.LastSeenUtc > latest.LastSeenUtc ||
                        item.HighestObservedUtc > latest.HighestObservedUtc ||
                        (item.Status == TrialState.Invalidated && latest.Status != TrialState.Invalidated) ||
                        (item.Status == TrialState.Expired && latest.Status == TrialState.Active) ||
                        (item.StateRevision == latest.StateRevision && (item.LastSeenUtc != latest.LastSeenUtc ||
                            item.HighestObservedUtc != latest.HighestObservedUtc || item.Status != latest.Status)))
                        return Invalidate(latest, "conflicting_or_rolled_back_record");
                }
                // A single interrupted write can leave one or two stores at the prior revision.
                // Missing stores are restored only when at least one authenticated record exists.
                var now = clock.UtcNow;
                if (!anchored) { anchorUtc = latest.LastSeenUtc; anchorElapsed = clock.Elapsed; anchored = true; }
                var monotonicNow = anchorUtc + (clock.Elapsed - anchorElapsed);
                if (now < latest.HighestObservedUtc.AddMinutes(-2) || now < monotonicNow.AddMinutes(-2))
                    return Invalidate(latest, "clock_rollback");
                var trusted = now > monotonicNow ? now : monotonicNow;
                if (trusted < latest.HighestObservedUtc) trusted = latest.HighestObservedUtc;
                var state = latest.Status == TrialState.Invalidated ? TrialState.Invalidated :
                    latest.Status == TrialState.Expired || trusted >= latest.ExpiresAtUtc ? TrialState.Expired : TrialState.Active;
                bool needsRepair = Array.Exists(records, x => x == null || x.StateRevision != latest.StateRevision);
                if (needsRepair) LogEvent(records.Length == 4 && records[3] == null ? "trial_migrated_or_recovered" : "trial_state_recovered", latest);
                if (state == TrialState.Active && !needsRepair && trusted - latest.LastSeenUtc < TimeSpan.FromMinutes(1))
                    return new TrialResult(state, latest.ExpiresAtUtc - trusted);
                if (state == TrialState.Active || (state == TrialState.Expired && latest.Status != TrialState.Expired) || needsRepair)
                {
                    var next = Copy(latest); next.StateRevision++; next.LastSeenUtc = trusted; next.HighestObservedUtc = trusted; next.Status = state;
                    try { store.WriteAll(next); }
                    catch (Exception e) { Trace.TraceError("Trial checkpoint failed: {0}", e.GetType().Name); return new TrialResult(TrialState.RecoveryRequired, TimeSpan.Zero, "checkpoint_failed"); }
                    anchorUtc = trusted; anchorElapsed = clock.Elapsed;
                }
                return new TrialResult(state, latest.ExpiresAtUtc - trusted);
            }
        }

        private IDisposable AcquireOperationLock()
        {
            try { return operationLock.TryAcquire(TimeSpan.FromSeconds(10)); }
            catch (Exception e)
            {
                Trace.TraceError("Trial operation lock failed: {0}", e.GetType().Name);
                return null;
            }
        }

        private TrialResult Invalidate(TrialRecord record, string reason)
        {
            Trace.TraceError("Trial invalidated: {0}", reason);
            LogEvent(reason, record);
            if (record != null)
            {
                try { var next = Copy(record); next.StateRevision++; next.Status = TrialState.Invalidated; store.WriteAll(next); }
                catch (Exception e) { Trace.TraceError("Trial invalidation persistence failed: {0}", e.GetType().Name); }
            }
            else if (!string.IsNullOrWhiteSpace(fingerprint))
            {
                try
                {
                    var now = clock.UtcNow;
                    store.WriteAll(new TrialRecord { TrialId = Guid.NewGuid().ToString("D"), DeviceFingerprint = fingerprint,
                        StartedAtUtc = now, ExpiresAtUtc = now.AddHours(120), LastSeenUtc = now,
                        HighestObservedUtc = now, Status = TrialState.Invalidated, StateRevision = 1 });
                }
                catch (Exception e) { Trace.TraceError("Trial invalidation marker failed: {0}", e.GetType().Name); }
            }
            return new TrialResult(TrialState.Invalidated, TimeSpan.Zero, reason);
        }

        private void LogPermanent()
        {
            if (permanentLogged) return;
            permanentLogged = true;
            LogEvent("permanent_license_valid", null);
        }

        private void LogEvent(string code, TrialRecord record)
        {
            try { (store as ITrialSecurityEventSink)?.RecordEvent(code, record); }
            catch (Exception e) { Trace.TraceError("Trial security event logging failed: {0}", e.GetType().Name); }
        }

        private static TrialRecord Copy(TrialRecord r) => new TrialRecord { Version = r.Version, TrialId = r.TrialId,
            DeviceFingerprint = r.DeviceFingerprint, StartedAtUtc = r.StartedAtUtc, ExpiresAtUtc = r.ExpiresAtUtc,
            LastSeenUtc = r.LastSeenUtc, HighestObservedUtc = r.HighestObservedUtc, Status = r.Status, StateRevision = r.StateRevision };
    }
}
