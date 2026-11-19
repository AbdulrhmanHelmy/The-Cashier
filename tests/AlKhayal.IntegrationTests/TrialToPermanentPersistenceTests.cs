using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using AlKhayal.Application.Licensing;
using AlKhayal.Data.Licensing;
using Microsoft.Win32;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class TrialToPermanentPersistenceTests
    {
        private sealed class Clock : ITrialClock
        {
            public DateTime UtcNow { get; set; } = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);
            public TimeSpan Elapsed { get; set; }
            public void Advance(TimeSpan duration) { UtcNow += duration; Elapsed += duration; }
        }

        private sealed class Hardware : IHardwareIdentity
        {
            public IDictionary<string, string> ReadClaims() => LicenseFormat.HashClaims(new Dictionary<string, string>
            { { "uuid", "trial-activation-uuid" }, { "board", "trial-activation-board" }, { "bios", "trial-activation-bios" } });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Trial_accepts_signed_activation_and_keeps_business_data_after_restart(bool expireFirst)
        {
            var root = Path.Combine(Path.GetTempPath(), "alk-trial-to-license-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var database = Path.Combine(root, "business.db");
            var trialFile = Path.Combine(root, "trial.dat");
            var securityFile = Path.Combine(root, "Security", "TrialState.bin");
            var licenseFile = Path.Combine(root, "license.alklic");
            var registry = @"Software\AlKhayal\TrialTests\" + Guid.NewGuid().ToString("N");
            try
            {
                var hardware = new Hardware();
                var clock = new Clock();
                var storage = new FileLicenseStorage(licenseFile);
                using (var rsa = RsaLicenseSignatures.CreateRsa())
                {
                    var signatures = new RsaLicenseSignatures(new Dictionary<string, string> { { "test-key", rsa.ToXmlString(false) } });
                    var permanent = new LicenseService(storage, signatures, hardware);
                    var trialStore = new WindowsTrialStore(database, trialFile, registry, securityFile);
                    var fingerprint = LicenseFormat.Hash(permanent.CreateRequest());
                    var trial = new TrialSecurityService(permanent, trialStore, clock, fingerprint);
                    Assert.Equal(TrialState.Active, trial.Start().State);
                    using (var connection = new SQLiteConnection("Data Source=" + database + ";Version=3;"))
                    {
                        connection.Open();
                        using (var command = new SQLiteCommand("CREATE TABLE AcceptanceData (Value TEXT NOT NULL); INSERT INTO AcceptanceData VALUES ('saved during trial');", connection))
                            command.ExecuteNonQuery();
                    }

                    if (expireFirst) clock.Advance(TimeSpan.FromHours(120));
                    Assert.Equal(expireFirst ? TrialState.Expired : TrialState.Active, trial.Evaluate().State);
                    var originalTrial = trialStore.ReadAll()[0];
                    var wrongClaims = LicenseFormat.HashClaims(new Dictionary<string, string>
                    { { "uuid", "other-uuid" }, { "board", "other-board" }, { "bios", "other-bios" } });
                    var wrongPayload = LicenseFormat.CreatePayload(wrongClaims, "test-key", Guid.NewGuid(), clock.UtcNow);
                    var wrongLicense = wrongPayload + "|" + Convert.ToBase64String(rsa.SignData(LicenseFormat.Bytes(wrongPayload), "SHA256"));
                    Assert.False(permanent.Activate(wrongLicense).IsValid);
                    Assert.False(File.Exists(licenseFile));
                    Assert.Equal(originalTrial.ExpiresAtUtc, trialStore.ReadAll()[0].ExpiresAtUtc);
                    Assert.Equal(expireFirst ? TrialState.Expired : TrialState.Active, trial.Evaluate().State);
                    var payload = LicenseFormat.CreatePayload(hardware.ReadClaims(), "test-key", Guid.NewGuid(), clock.UtcNow);
                    var signed = payload + "|" + Convert.ToBase64String(rsa.SignData(LicenseFormat.Bytes(payload), "SHA256"));
                    Assert.True(permanent.Activate(signed).IsValid);
                    Assert.Equal(TrialState.PermanentlyActivated, trial.Evaluate().State);
                    Assert.Equal(expireFirst ? TrialState.Expired : TrialState.Active, trialStore.ReadAll()[0].Status);

                    var restartedLicense = new LicenseService(new FileLicenseStorage(licenseFile), signatures, new Hardware());
                    var restartedTrial = new TrialSecurityService(restartedLicense,
                        new WindowsTrialStore(database, trialFile, registry, securityFile), clock, fingerprint);
                    Assert.Equal(TrialState.PermanentlyActivated, restartedTrial.Evaluate().State);
                    Assert.Equal(signed, File.ReadAllText(licenseFile));
                    using (var connection = new SQLiteConnection("Data Source=" + database + ";Version=3;"))
                    {
                        connection.Open();
                        using (var command = new SQLiteCommand("SELECT Value FROM AcceptanceData", connection))
                            Assert.Equal("saved during trial", command.ExecuteScalar());
                    }
                }
            }
            finally
            {
                Registry.CurrentUser.DeleteSubKeyTree(registry, false);
                SQLiteConnection.ClearAllPools();
                Directory.Delete(root, true);
            }
        }
    }
}
