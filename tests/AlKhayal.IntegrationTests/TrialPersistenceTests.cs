using System;
using System.IO;
using AlKhayal.Application.Licensing;
using AlKhayal.Data.Licensing;
using Microsoft.Win32;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class TrialPersistenceTests
    {
        [Fact]
        public void Surviving_event_log_blocks_a_complete_record_reset()
        {
            var root = Path.Combine(Path.GetTempPath(), "alk-trial-events-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var registry = @"Software\AlKhayal\TrialTests\" + Guid.NewGuid().ToString("N");
            var database = Path.Combine(root, "test.db");
            var file = Path.Combine(root, "trial.dat");
            var security = Path.Combine(root, "Security", "TrialState.bin");
            try
            {
                var store = new WindowsTrialStore(database, file, registry, security);
                var clock = new Clock();
                Assert.Equal(TrialState.Active, new TrialSecurityService(new Permanent(), store, clock, "device").Start().State);
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                File.Delete(database); File.Delete(file); File.Delete(security);
                Registry.CurrentUser.DeleteSubKey(registry);
                Assert.Equal(TrialState.Invalidated, new TrialSecurityService(new Permanent(), store, clock, "device").Evaluate().State);
            }
            finally
            {
                Registry.CurrentUser.DeleteSubKeyTree(registry, false);
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Corrupted_event_log_denies_trial_access()
        {
            var root = Path.Combine(Path.GetTempPath(), "alk-trial-corrupt-events-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var registry = @"Software\AlKhayal\TrialTests\" + Guid.NewGuid().ToString("N");
            var security = Path.Combine(root, "Security", "TrialState.bin");
            try
            {
                var store = new WindowsTrialStore(Path.Combine(root, "test.db"), Path.Combine(root, "trial.dat"), registry, security);
                var clock = new Clock();
                Assert.Equal(TrialState.Active, new TrialSecurityService(new Permanent(), store, clock, "device").Start().State);
                File.WriteAllText(security + ".events", "damaged");
                Assert.Equal(TrialState.Invalidated, new TrialSecurityService(new Permanent(), store, clock, "device").Evaluate().State);
            }
            finally
            {
                Registry.CurrentUser.DeleteSubKeyTree(registry, false);
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                Directory.Delete(root, true);
            }
        }

        private sealed class Clock : ITrialClock
        {
            public DateTime UtcNow { get; set; } = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);
            public TimeSpan Elapsed { get; set; }
        }
        private sealed class Permanent : ILicenseGate
        {
            public LicenseStatus Check() => new LicenseStatus(false, "test");
        }
        [Fact]
        public void Windows_layers_survive_restart_and_repair_one_missing_layer()
        {
            var root = Path.Combine(Path.GetTempPath(), "alk-trial-store-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var database = Path.Combine(root, "test.db"); var file = Path.Combine(root, "trial.dat");
            var registry = @"Software\AlKhayal\TrialTests\" + Guid.NewGuid().ToString("N");
            try
            {
                var security = Path.Combine(root, "Security", "TrialState.bin");
                var clock = new Clock(); var store = new WindowsTrialStore(database, file, registry, security);
                var first = new TrialSecurityService(new Permanent(), store, clock, "device");
                Assert.Equal(TrialState.Active, first.Start().State);
                Assert.True(File.Exists(database)); Assert.True(File.Exists(file)); Assert.True(File.Exists(security));
                Assert.True(File.Exists(security + ".events"));
                var originalExpiry = store.ReadAll()[0].ExpiresAtUtc;
                File.Delete(security); // Existing three-record installation migrates in place.
                Assert.Equal(TrialState.Active, new TrialSecurityService(new Permanent(), store, clock, "device").Evaluate().State);
                Assert.True(File.Exists(security));
                Assert.Equal(originalExpiry, store.ReadAll()[3].ExpiresAtUtc);
                using (var key = Registry.CurrentUser.OpenSubKey(registry)) Assert.NotNull(key.GetValue("TrialState"));
                Assert.Equal(TrialState.Active, new TrialSecurityService(new Permanent(), store, clock, "device").Evaluate().State);
                File.Delete(file);
                Assert.Equal(TrialState.Active, new TrialSecurityService(new Permanent(), store, clock, "device").Evaluate().State);
                Assert.True(File.Exists(file));
                Registry.CurrentUser.DeleteSubKey(registry);
                Assert.Equal(TrialState.Active, new TrialSecurityService(new Permanent(), store, clock, "device").Evaluate().State);
                using (var key = Registry.CurrentUser.OpenSubKey(registry)) Assert.NotNull(key.GetValue("TrialState"));
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                File.Delete(database);
                Assert.Equal(TrialState.Active, new TrialSecurityService(new Permanent(), store, clock, "device").Evaluate().State);
                Assert.True(File.Exists(database));
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                File.Delete(database); File.Delete(file);
                Registry.CurrentUser.DeleteSubKey(registry);
                Assert.Equal(TrialState.Active, new TrialSecurityService(new Permanent(), store, clock, "device").Evaluate().State);
                Assert.True(File.Exists(database)); Assert.True(File.Exists(file));
                File.WriteAllText(file, "tampered");
                Assert.Equal(TrialState.Invalidated, new TrialSecurityService(new Permanent(), store, clock, "device").Evaluate().State);
            }
            finally
            {
                Registry.CurrentUser.DeleteSubKeyTree(registry, false);
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                Directory.Delete(root, true);
            }
        }
    }
}
