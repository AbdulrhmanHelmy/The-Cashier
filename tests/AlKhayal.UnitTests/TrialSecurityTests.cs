using System;
using AlKhayal.Application.Licensing;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class TrialSecurityTests
    {
        private sealed class Clock : ITrialClock
        {
            public DateTime UtcNow { get; set; } = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
            public TimeSpan Elapsed { get; set; }
            public void Advance(TimeSpan span) { UtcNow += span; Elapsed += span; }
        }
        private sealed class Gate : ILicenseGate
        {
            public bool Active;
            public LicenseStatus Check() => new LicenseStatus(Active, "test");
        }
        private sealed class Store : ITrialStore
        {
            public TrialRecord[] Items = new TrialRecord[3];
            public TrialRecord[] ReadAll() => (TrialRecord[])Items.Clone();
            public void WriteAll(TrialRecord r) { for (int i = 0; i < Items.Length; i++) Items[i] = Copy(r); }
            public static TrialRecord Copy(TrialRecord r) => new TrialRecord { TrialId = r.TrialId, DeviceFingerprint = r.DeviceFingerprint,
                StartedAtUtc = r.StartedAtUtc, ExpiresAtUtc = r.ExpiresAtUtc, LastSeenUtc = r.LastSeenUtc,
                HighestObservedUtc = r.HighestObservedUtc, Status = r.Status, StateRevision = r.StateRevision };
        }
        private sealed class DeniedStore : ITrialStore
        {
            public TrialRecord[] ReadAll() => new TrialRecord[4];
            public void WriteAll(TrialRecord record) => throw new UnauthorizedAccessException();
        }
        private sealed class ReadFailureStore : ITrialStore
        {
            public TrialRecord[] ReadAll() => throw new System.IO.IOException("temporary read failure");
            public void WriteAll(TrialRecord record) => throw new InvalidOperationException();
        }
        private sealed class PartialWriteStore : ITrialStore
        {
            public TrialRecord[] Items = new TrialRecord[4];
            public bool FailWrites;
            public TrialRecord[] ReadAll() => (TrialRecord[])Items.Clone();
            public void WriteAll(TrialRecord record)
            {
                Items[0] = Store.Copy(record);
                Items[1] = Store.Copy(record);
                if (FailWrites) throw new System.IO.IOException("temporary write failure");
                Items[2] = Store.Copy(record);
                Items[3] = Store.Copy(record);
            }
        }
        private sealed class ContendedLock : ITrialOperationLock
        {
            public IDisposable TryAcquire(TimeSpan timeout) => null;
        }
        private static TrialSecurityService Service(Store store, Clock clock, Gate gate = null) =>
            new TrialSecurityService(gate ?? new Gate(), store, clock, "device-test");

        [Fact]
        public void Permission_denial_during_start_fails_closed()
        {
            var result = new TrialSecurityService(new Gate(), new DeniedStore(), new Clock(), "device-test").Start();
            Assert.Equal(TrialState.StorageUnavailable, result.State);
            Assert.Equal("storage_write", result.Reason);
        }

        [Fact]
        public void Temporary_read_failure_is_not_persisted_as_tampering_or_expiration()
        {
            var result = new TrialSecurityService(new Gate(), new ReadFailureStore(), new Clock(), "device-test").Evaluate();
            Assert.Equal(TrialState.StorageUnavailable, result.State);
            Assert.Equal("storage_read", result.Reason);
        }

        [Fact]
        public void Cross_process_lock_contention_is_reported_as_temporary_unavailability()
        {
            var result = new TrialSecurityService(new Gate(), new Store(), new Clock(), "device-test", new ContendedLock()).Evaluate();
            Assert.Equal(TrialState.StorageUnavailable, result.State);
            Assert.Equal("lock_timeout", result.Reason);
        }

        [Fact]
        public void Windows_machine_lock_serializes_independent_service_instances()
        {
            var first = new AlKhayal.Data.Licensing.WindowsTrialOperationLock();
            var second = new AlKhayal.Data.Licensing.WindowsTrialOperationLock();
            using (Assert.IsAssignableFrom<IDisposable>(first.TryAcquire(TimeSpan.FromSeconds(1))))
            {
                IDisposable competing = new DummyLease();
                var thread = new System.Threading.Thread(() => competing = second.TryAcquire(TimeSpan.FromMilliseconds(100)));
                thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(2)));
                Assert.Null(competing);
            }
            using (Assert.IsAssignableFrom<IDisposable>(second.TryAcquire(TimeSpan.FromSeconds(1)))) { }
        }
        private sealed class DummyLease : IDisposable { public void Dispose() { } }

        [Fact]
        public void Partial_checkpoint_write_requires_recovery_without_changing_original_expiry()
        {
            var clock = new Clock(); var store = new PartialWriteStore();
            var service = new TrialSecurityService(new Gate(), store, clock, "device-test");
            Assert.Equal(TrialState.Active, service.Start().State);
            var started = store.Items[0].StartedAtUtc;
            var expiry = store.Items[0].ExpiresAtUtc;
            clock.Advance(TimeSpan.FromHours(24)); store.FailWrites = true;
            var failed = service.Evaluate();
            Assert.Equal(TrialState.RecoveryRequired, failed.State);
            Assert.Equal("checkpoint_failed", failed.Reason);
            Assert.Equal(started, store.Items[0].StartedAtUtc);
            Assert.Equal(expiry, store.Items[0].ExpiresAtUtc);
            store.FailWrites = false;
            var recovered = new TrialSecurityService(new Gate(), store, clock, "device-test").Evaluate();
            Assert.Equal(TrialState.Active, recovered.State);
            Assert.Equal(started, store.Items[3].StartedAtUtc);
            Assert.Equal(expiry, store.Items[3].ExpiresAtUtc);
        }

        [Fact]
        public void Fourth_witness_recovers_deleted_legacy_records_and_rejects_old_replay()
        {
            var clock = new Clock(); var store = new Store { Items = new TrialRecord[4] };
            var original = Service(store, clock).Start();
            Assert.Equal(TrialState.Active, original.State);
            var start = store.Items[3].StartedAtUtc;
            store.Items[0] = null; store.Items[1] = null; store.Items[2] = null;
            Assert.Equal(TrialState.Active, Service(store, clock).Evaluate().State);
            Assert.Equal(start, store.Items[0].StartedAtUtc);
            var old = Store.Copy(store.Items[3]);
            clock.Advance(TimeSpan.FromMinutes(2)); Service(store, clock).Evaluate();
            clock.Advance(TimeSpan.FromMinutes(2)); Service(store, clock).Evaluate();
            store.Items[3] = old;
            Assert.Equal(TrialState.Invalidated, Service(store, clock).Evaluate().State);
        }

        [Fact]
        public void Trial_is_explicit_and_expires_at_exactly_120_hours()
        {
            var clock = new Clock(); var store = new Store(); var service = Service(store, clock);
            Assert.Equal(TrialState.NotStarted, service.Evaluate().State);
            Assert.Equal(TrialState.Active, service.Start().State);
            Assert.Equal(clock.UtcNow.AddHours(120), store.Items[0].ExpiresAtUtc);
            clock.Advance(TimeSpan.FromHours(24)); Assert.Equal(TrialState.Active, service.Evaluate().State);
            clock.Advance(TimeSpan.FromHours(95) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59));
            Assert.Equal(TrialState.Active, service.Evaluate().State);
            clock.Advance(TimeSpan.FromSeconds(1)); Assert.Equal(TrialState.Expired, service.Evaluate().State);
            Assert.Equal(TrialState.Expired, store.Items[2].Status);
            Assert.Equal(TrialState.Expired, Service(store, clock).Start().State);
        }

        [Fact]
        public void Rollback_beyond_tolerance_invalidates_and_permanent_activation_wins()
        {
            var clock = new Clock(); var store = new Store(); var gate = new Gate(); var service = Service(store, clock, gate);
            service.Start(); clock.Advance(TimeSpan.FromHours(2)); service.Evaluate();
            clock.UtcNow = clock.UtcNow.AddMinutes(-3);
            Assert.Equal(TrialState.Invalidated, service.Evaluate().State);
            Assert.Equal(TrialState.Invalidated, store.Items[1].Status);
            gate.Active = true; Assert.Equal(TrialState.PermanentlyActivated, service.Evaluate().State);
        }

        [Fact]
        public void Minor_correction_does_not_extend_monotonic_runtime()
        {
            var clock = new Clock(); var store = new Store(); var service = Service(store, clock);
            service.Start(); clock.Advance(TimeSpan.FromHours(119)); service.Evaluate();
            clock.UtcNow = clock.UtcNow.AddMinutes(-1);
            Assert.Equal(TrialState.Active, service.Evaluate().State);
            clock.Elapsed += TimeSpan.FromHours(1);
            Assert.Equal(TrialState.Invalidated, service.Evaluate().State);
        }

        [Fact]
        public void Missing_layer_is_repaired_but_conflicting_identity_and_old_replay_invalidate()
        {
            var clock = new Clock(); var store = new Store(); var service = Service(store, clock);
            service.Start(); store.Items[0] = null;
            Assert.Equal(TrialState.Active, service.Evaluate().State);
            Assert.NotNull(store.Items[0]);
            var old = Store.Copy(store.Items[0]);
            clock.Advance(TimeSpan.FromMinutes(2)); service.Evaluate();
            clock.Advance(TimeSpan.FromMinutes(2)); service.Evaluate();
            store.Items[0] = old;
            Assert.Equal(TrialState.Invalidated, service.Evaluate().State);
            var conflict = new Store(); Service(conflict, clock).Start();
            conflict.Items[1].TrialId = Guid.NewGuid().ToString("D");
            Assert.Equal(TrialState.Invalidated, Service(conflict, clock).Evaluate().State);
        }

        [Fact]
        public void One_revision_interruption_recovers_without_new_trial()
        {
            var clock = new Clock(); var store = new Store(); var service = Service(store, clock);
            service.Start(); var previous = Store.Copy(store.Items[0]);
            clock.Advance(TimeSpan.FromMinutes(2)); service.Evaluate();
            store.Items[1] = previous; store.Items[2] = previous;
            Assert.Equal(TrialState.Active, Service(store, clock).Evaluate().State);
            Assert.Equal(store.Items[0].TrialId, store.Items[2].TrialId);
            Assert.Equal(store.Items[0].StateRevision, store.Items[2].StateRevision);
        }

        [Fact]
        public void Forward_jump_expires_and_device_change_invalidates()
        {
            var clock = new Clock(); var store = new Store(); var service = Service(store, clock);
            service.Start(); clock.UtcNow = clock.UtcNow.AddDays(10);
            Assert.Equal(TrialState.Expired, service.Evaluate().State);
            Assert.Equal(TrialState.Expired, Service(store, clock).Evaluate().State);
            var other = new Store(); Service(other, clock).Start();
            var moved = new TrialSecurityService(new Gate(), other, clock, "other-device");
            Assert.Equal(TrialState.Invalidated, moved.Evaluate().State);
        }

        [Fact]
        public void Same_revision_divergence_and_terminal_status_rollback_invalidate()
        {
            var clock = new Clock(); var store = new Store(); var service = Service(store, clock);
            service.Start(); clock.Advance(TimeSpan.FromMinutes(2)); service.Evaluate();
            store.Items[1].LastSeenUtc = store.Items[1].LastSeenUtc.AddSeconds(-1);
            Assert.Equal(TrialState.Invalidated, service.Evaluate().State);
            var second = new Store(); Service(second, clock).Start();
            second.Items[1].Status = TrialState.Invalidated;
            Assert.Equal(TrialState.Invalidated, Service(second, clock).Evaluate().State);
            var third = new Store(); Service(third, clock).Start();
            third.Items[0].Status = (TrialState)999;
            Assert.Equal(TrialState.Invalidated, Service(third, clock).Evaluate().State);
        }

        [Fact]
        public void Restart_sequence_preserves_exact_utc_expiry_and_timezone_has_no_effect()
        {
            var clock = new Clock(); var store = new Store();
            var start = clock.UtcNow;
            Assert.Equal(TrialState.Active, Service(store, clock).Start().State);
            foreach (var hours in new[] { 0, 24, 72, 119 })
            {
                clock.UtcNow = start.AddHours(hours);
                clock.Elapsed = TimeSpan.Zero; // New process, including after a Windows restart.
                Assert.Equal(TrialState.Active, Service(store, clock).Evaluate().State);
            }
            clock.UtcNow = start.AddHours(120).AddTicks(-1);
            Assert.Equal(TrialState.Active, Service(store, clock).Evaluate().State);
            clock.UtcNow = start.AddHours(120);
            Assert.Equal(TrialState.Expired, Service(store, clock).Evaluate().State);
            clock.UtcNow = start.AddHours(121);
            Assert.Equal(TrialState.Expired, Service(store, clock).Evaluate().State);
            Assert.Equal(start.AddHours(120), store.Items[0].ExpiresAtUtc);
        }

        [Fact]
        public void Deleted_all_layers_can_start_a_new_trial_and_is_a_known_offline_limit()
        {
            var clock = new Clock(); var store = new Store { Items = new TrialRecord[4] };
            var first = Service(store, clock).Start();
            Assert.Equal(TrialState.Active, first.State);
            var original = store.Items[0].TrialId;
            store.Items = new TrialRecord[4]; // All four records and any event anchor are absent.
            clock.UtcNow = clock.UtcNow.AddDays(6);
            var second = Service(store, clock).Start();
            Assert.Equal(TrialState.Active, second.State);
            Assert.NotEqual(original, store.Items[0].TrialId);
        }

        [Fact]
        public void Recent_backup_replay_is_accepted_and_shows_rollback_window()
        {
            var clock = new Clock(); var store = new Store(); var service = Service(store, clock);
            service.Start();
            var previous = Store.Copy(store.Items[0]);
            clock.Advance(TimeSpan.FromMinutes(2)); service.Evaluate();
            store.Items = new[] { Store.Copy(previous), Store.Copy(previous), Store.Copy(previous) };
            Assert.Equal(TrialState.Active, Service(store, clock).Evaluate().State);
        }
    }
}
