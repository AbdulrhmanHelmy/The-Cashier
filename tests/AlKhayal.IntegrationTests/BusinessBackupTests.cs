using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Backups;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Licensing;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Backups;
using AlKhayal.Data.Database;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Inventory;
using AlKhayal.Data.Licensing;
using AlKhayal.Data.Sales;
using Microsoft.Win32;
using AlKhayal.Server.Host;
using Dapper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class BusinessBackupTests : IDisposable
    {
        private const string TrialMarker = "do-not-export-source-trial";
        private readonly string root = Path.Combine(Environment.GetEnvironmentVariable("THECASHIER_BACKUP_TEST_TEMP") ?? Path.GetTempPath(),
            "alk-backup-tests-" + Guid.NewGuid().ToString("N"));
        private readonly string source;
        private readonly string destination;

        public BusinessBackupTests()
        {
            Directory.CreateDirectory(root);
            source = Path.Combine(root, "source.sqlite");
            destination = Path.Combine(root, "destination.sqlite");
            Migrator.Run(Db.ConnectionStringFor(source));
            Migrator.Run(Db.ConnectionStringFor(destination));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("Active")]
        [InlineData("Expired")]
        public async Task Wal_round_trip_preserves_destination_trial_and_strips_all_sessions(string destinationTrial)
        {
            using var writer = Open(source);
            writer.Execute("PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;");
            await SeedUser(source, "from-backup", "administrator");
            writer.Execute(@"INSERT INTO Products (Name,Price) VALUES ('نسخة الاختبار',12);
                INSERT INTO Sessions(Token,UserId,ExpiresUtc,CreatedUtc,CsrfToken)
                SELECT 'source-session',Id,'2030-01-01','2026-01-01','source-csrf' FROM Users");
            InsertTrial(writer, TrialMarker, "Active");
            Assert.True(new FileInfo(source + "-wal").Length > 0);
            await SeedUser(destination, "destination-admin", "administrator");
            using (var c = Open(destination))
            {
                c.Execute("PRAGMA journal_mode=WAL");
                if (destinationTrial != null) InsertTrial(c, "destination-trial", destinationTrial);
                c.Execute(@"INSERT INTO Sessions(Token,UserId,ExpiresUtc,CreatedUtc,CsrfToken)
                    SELECT 'destination-session',Id,'2030-01-01','2026-01-01','destination-csrf' FROM Users");
            }

            using var backup = new SqliteBusinessBackups(source);
            var first = await backup.CreateAsync();
            var second = await backup.CreateAsync();
            Assert.NotEqual(first.Path, second.Path);
            Assert.Equal(2, Directory.GetFiles(Path.GetDirectoryName(first.Path)).Length);
            var extracted = Path.Combine(root, "extracted.db");
            using (var stream = File.OpenRead(FixtureIoPath(first.Path)))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            using (var input = zip.GetEntry("business.sqlite").Open())
            using (var output = File.Create(extracted))
            {
                Assert.Equal(2, zip.Entries.Count);
                input.CopyTo(output);
            }
            using (var portable = Open(extracted))
            {
                Assert.Equal(0, portable.ExecuteScalar<int>("SELECT COUNT(*) FROM TrialState"));
                Assert.Equal(0, portable.ExecuteScalar<int>("SELECT COUNT(*) FROM Sessions"));
                Assert.Equal("نسخة الاختبار", portable.ExecuteScalar<string>("SELECT Name FROM Products"));
            }
            var raw = Encoding.ASCII.GetString(File.ReadAllBytes(extracted));
            Assert.DoesNotContain(TrialMarker, raw);
            Assert.DoesNotContain("source-session", raw);

            using var restore = new SqliteBusinessBackups(destination);
            using var archive = await backup.DownloadAsync(first.Name);
            var manifest = await restore.InspectAsync(archive, CancellationToken.None);
            await Assert.ThrowsAsync<BackupException>(() => restore.RestoreAsync(Guid.NewGuid().ToString("N"), CancellationToken.None));
            var emergency = await restore.RestoreAsync(manifest.Confirmation, CancellationToken.None);
            Assert.True(File.Exists(FixtureIoPath(emergency.Path)));
            using var result = Open(destination);
            Assert.Equal("from-backup", result.ExecuteScalar<string>("SELECT Username FROM Users"));
            Assert.Equal("نسخة الاختبار", result.ExecuteScalar<string>("SELECT Name FROM Products"));
            Assert.Equal(0, result.ExecuteScalar<int>("SELECT COUNT(*) FROM Sessions"));
            Assert.Equal(destinationTrial, result.ExecuteScalar<string>("SELECT Status FROM TrialState"));
            Assert.Equal(destinationTrial == null ? null : "destination-trial", result.ExecuteScalar<string>("SELECT DeviceFingerprint FROM TrialState"));
            Assert.Equal(TrialMarker, writer.ExecuteScalar<string>("SELECT DeviceFingerprint FROM TrialState"));
            using var recovery = File.OpenRead(FixtureIoPath(emergency.Path));
            var verifiedRecovery = await restore.InspectAsync(recovery, CancellationToken.None);
            Assert.NotEqual(Guid.Empty, verifiedRecovery.Id);
        }

        [Fact]
        public async Task Posted_sales_stock_and_permissions_survive_restoration_to_fresh_database()
        {
            await SeedUser(source, "source-admin", "administrator");
            await SeedUser(source, "source-cashier", "cashier");
            var cs = Db.ConnectionStringFor(source);
            using var current = Open(source);
            var admin = current.ExecuteScalar<long>("SELECT Id FROM Users WHERE Role='administrator'");
            current.Execute(@"INSERT INTO UserScreenPermissions(UserId,ScreenId,GrantedUtc,ChangedByUserId)
                SELECT Id,'products-list','2026-11-01',@admin FROM Users WHERE Role='cashier';
                INSERT INTO CashierShifts(CashierId,RegisterId,OpenedUtc,OpenedBy,OpeningScaled)
                VALUES(@admin,'standalone','2026-11-01',@admin,0);", new { admin });

            var catalog = new CatalogService(new SqliteCatalogStore(cs));
            var product = await catalog.CreateProductAsync(new CreateProductCommand
            {
                OperationKey = "backup-product", Name = "اختبار صنف", BaseUnitId = 1, ProductType = "stock",
                SalePrice = "15", PurchasePrice = "10", IsActive = true
            }, CancellationToken.None);
            Assert.True(product.Succeeded, product.ErrorMessage);
            var ids = new { productId = product.Value.Id };
            var variant = current.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@productId", ids);
            var unit = current.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@productId AND IsBase=1", ids);
            var receipt = await new SqliteInventoryStore(cs).PostAsync(new InventoryPostCommand
            {
                OperationKey = "backup-stock", Type = "manual_receipt", WarehouseId = 1, UserId = admin, Reason = "توريد",
                Lines = new[] { new InventoryLineInput { VariantId = variant, ProductUnitId = unit, Quantity = "5", UnitCost = "10", SalePrice = "15" } }
            }, "backup-stock-hash", CancellationToken.None);
            Assert.True(receipt.Succeeded, receipt.ErrorMessage);
            var sales = new SalesService(new SqliteSalesStore(cs));
            var draft = await sales.CreateDraftAsync(new SaleDraftCommand
            {
                WarehouseId = 1, InvoiceDate = "2026-11-01", PaymentMode = "cash",
                Lines = new() { new SaleLine { VariantId = variant, ProductUnitId = unit, Quantity = "2", UnitPrice = "15" } }
            }, admin, CancellationToken.None);
            var posted = await sales.PostAsync(draft.Id, "backup-invoice", admin, CancellationToken.None);
            Assert.True(posted.Succeeded, posted.ErrorMessage);

            using var producer = new SqliteBusinessBackups(source);
            var file = await producer.CreateAsync(Path.Combine(root, "CustomManual"));
            using var consumer = new SqliteBusinessBackups(destination);
            using var stream = File.OpenRead(FixtureIoPath(file.Path));
            var preview = await consumer.InspectAsync(stream, CancellationToken.None);
            await consumer.RestoreAsync(preview.Confirmation, CancellationToken.None);

            using var restored = Open(destination);
            Assert.Equal("products-list", restored.ExecuteScalar<string>("SELECT ScreenId FROM UserScreenPermissions"));
            Assert.Equal(3000000L, restored.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE VariantId=@variant", new { variant }));
            Assert.Equal(-2000000L, restored.ExecuteScalar<long>("SELECT SUM(QuantityBaseScaled) FROM StockMovements WHERE DocumentId=@id",
                new { id = posted.Invoice.InventoryDocumentId }));
            Assert.Equal(posted.Invoice.TotalScaled, restored.ExecuteScalar<long>("SELECT TotalScaled FROM SalesInvoices WHERE Id=@id", new { id = draft.Id }));
            Assert.True(await new SqliteInventoryStore(Db.ConnectionStringFor(destination)).VerifyProjectionAsync(CancellationToken.None));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Protected_trial_layers_remain_identical_on_a_different_device(bool expired)
        {
            var registry = @"Software\AlKhayal\TrialTests\" + Guid.NewGuid().ToString("N");
            var trialFile = Path.Combine(root, "destination-trial.dat");
            var clock = new TrialClock();
            var store = new WindowsTrialStore(destination, trialFile, registry, Path.Combine(root, "TrialState.bin"));
            try
            {
                var trial = new TrialSecurityService(new NoLicense(), store, clock, "destination-device");
                Assert.Equal(TrialState.Active, trial.Start().State);
                if (expired) clock.UtcNow = clock.UtcNow.AddDays(5);
                var expected = expired ? TrialState.Expired : TrialState.Active;
                Assert.Equal(expected, trial.Evaluate().State);
                var before = store.ReadAll()[0];
                var protectedFile = File.ReadAllBytes(trialFile);
                string protectedRegistry;
                using (var key = Registry.CurrentUser.OpenSubKey(registry)) protectedRegistry = (string)key.GetValue("TrialState");
                using (var c = Open(source)) InsertTrial(c, TrialMarker, "Active");

                using var producer = new SqliteBusinessBackups(source);
                var archive = await producer.CreateAsync();
                using var consumer = new SqliteBusinessBackups(destination);
                using var input = File.OpenRead(FixtureIoPath(archive.Path));
                var preview = await consumer.InspectAsync(input, CancellationToken.None);
                await consumer.RestoreAsync(preview.Confirmation, CancellationToken.None);

                Assert.Equal(protectedFile, File.ReadAllBytes(trialFile));
                using (var key = Registry.CurrentUser.OpenSubKey(registry))
                    Assert.Equal(protectedRegistry, (string)key.GetValue("TrialState"));
                Assert.All(store.ReadAll(), row =>
                {
                    Assert.Equal(before.TrialId, row.TrialId);
                    Assert.Equal(before.ExpiresAtUtc, row.ExpiresAtUtc);
                    Assert.Equal(before.StateRevision, row.StateRevision);
                    Assert.Equal("destination-device", row.DeviceFingerprint);
                });
                Assert.Equal(expected, new TrialSecurityService(new NoLicense(), store, clock, "destination-device").Evaluate().State);
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(registry, false); }
        }

        [Theory]
        [InlineData("../business.sqlite")]
        [InlineData("C:/business.sqlite")]
        [InlineData("BUSINESS.sqlite")]
        [InlineData("manifest.json")]
        [InlineData("bad-hash")]
        [InlineData("newer-schema")]
        [InlineData("oversized")]
        public async Task Malformed_archives_are_rejected_without_touching_current_data(string change)
        {
            using var backup = new SqliteBusinessBackups(source);
            var valid = await backup.CreateAsync();
            using var restore = new SqliteBusinessBackups(destination);
            using var current = Open(destination);
            InsertTrial(current, "keep", "Expired");
            using var bad = new MemoryStream();
            using (var input = File.OpenRead(FixtureIoPath(valid.Path)))
            using (var original = new ZipArchive(input, ZipArchiveMode.Read))
            using (var copy = new ZipArchive(bad, ZipArchiveMode.Create, true))
            {
                using (var reader = new StreamReader(original.GetEntry("manifest.json").Open()))
                using (var writer = new StreamWriter(copy.CreateEntry("manifest.json").Open()))
                {
                    var manifest = JObject.Parse(reader.ReadToEnd());
                    if (change == "newer-schema") manifest["SchemaVersion"] = "999_future.sql";
                    if (change == "oversized") manifest["Size"] = long.MaxValue;
                    writer.Write(manifest.ToString(Formatting.None));
                }
                var dataName = change == "bad-hash" || change == "newer-schema" || change == "oversized"
                    ? "business.sqlite" : change;
                using var payload = original.GetEntry("business.sqlite").Open();
                using var output = copy.CreateEntry(dataName).Open();
                if (change == "bad-hash") { payload.ReadByte(); output.WriteByte(0); }
                payload.CopyTo(output);
            }
            bad.Position = 0;
            await Assert.ThrowsAsync<BackupException>(() => restore.InspectAsync(bad, CancellationToken.None));
            Assert.Equal("keep", current.ExecuteScalar<string>("SELECT DeviceFingerprint FROM TrialState"));
            Assert.False(Directory.Exists(Path.Combine(root, "RestoreRecovery")));
        }

        [Fact]
        public async Task Schedule_restarts_retries_retains_seven_and_preserves_manual_backup()
        {
            var now = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
            var folder = Path.Combine(root, "Scheduled");
            var config = new BackupConfiguration { Enabled = true, Directory = folder };
            using (var setup = new SqliteBusinessBackups(source, () => now))
                await setup.ConfigureAsync(config);
            using var service = new SqliteBusinessBackups(source, () => now);
            File.WriteAllText(Path.Combine(root, "blocker"), "file");
            Directory.Move(folder, folder + "-away");
            File.Move(Path.Combine(root, "blocker"), folder);
            Assert.False(await service.RunScheduledAsync());
            Assert.NotNull((await service.StatusAsync()).LastFailureUtc);
            File.Delete(folder);
            Directory.Move(folder + "-away", folder);
            Assert.False(await service.RunScheduledAsync());
            now = now.AddMinutes(6);
            var attempts = await Task.WhenAll(service.RunScheduledAsync(), service.RunScheduledAsync());
            Assert.Single(attempts, success => success);
            var manual = await service.CreateAsync();
            BackupFile foreign;
            using (var otherInstallation = new SqliteBusinessBackups(destination, () => now.AddDays(-1)))
            {
                await otherInstallation.ConfigureAsync(config);
                Assert.True(await otherInstallation.RunScheduledAsync());
                foreign = (await otherInstallation.StatusAsync()).LastSuccess;
            }
            for (var day = 0; day < 8; day++)
            {
                now = now.AddDays(1);
                Assert.True(await service.RunScheduledAsync());
                Assert.False(await service.RunScheduledAsync());
            }
            var automatic = Directory.GetFiles(folder, "alkhayal-auto-*.alkbackup");
            Assert.Contains(foreign.Path, automatic);
            Assert.Equal(7, automatic.Count(path => path != foreign.Path));
            Assert.True(File.Exists(FixtureIoPath(manual.Path)));
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
            using var restarted = new SqliteBusinessBackups(source, () => now);
            Assert.False(await restarted.RunScheduledAsync());
            Assert.True((await restarted.StatusAsync()).Configuration.Enabled);
            Directory.Move(folder, folder + "-unavailable");
            File.WriteAllText(folder, "path unavailable");
            var disabled = await restarted.ConfigureAsync(new BackupConfiguration { Enabled = false, Directory = folder });
            Assert.False(disabled.Configuration.Enabled);
        }

        [Fact]
        public async Task Schedule_recovers_a_published_archive_when_local_status_write_was_interrupted()
        {
            var now = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
            var folder = Path.Combine(root, "CrashSchedule");
            var statePath = source + ".backup-settings.json";
            using (var scheduler = new SqliteBusinessBackups(source, () => now))
            {
                await scheduler.ConfigureAsync(new BackupConfiguration { Directory = folder, Enabled = true });
                var beforePublication = File.ReadAllBytes(statePath);
                Assert.True(await scheduler.RunScheduledAsync());
                File.WriteAllBytes(statePath, beforePublication);
            }
            using var restarted = new SqliteBusinessBackups(source, () => now);
            Assert.True(await restarted.RunScheduledAsync());
            var archive = Assert.Single(Directory.GetFiles(folder, "*.alkbackup"));
            Assert.Equal(archive, (await restarted.StatusAsync()).LastSuccess.Path);
            Assert.False(await restarted.RunScheduledAsync());
        }

        [Fact]
        public async Task Emergency_publication_failure_keeps_live_database_and_never_reports_success()
        {
            using var service = new SqliteBusinessBackups(source);
            var file = await service.CreateAsync();
            using var restore = new SqliteBusinessBackups(destination);
            await SeedUser(destination, "retained", "administrator");
            using var stream = File.OpenRead(FixtureIoPath(file.Path));
            var manifest = await restore.InspectAsync(stream, CancellationToken.None);
            File.WriteAllText(Path.Combine(root, "RestoreRecovery"), "blocked");
            await Assert.ThrowsAnyAsync<IOException>(() => restore.RestoreAsync(manifest.Confirmation, CancellationToken.None));
            using var c = Open(destination);
            Assert.Equal("retained", c.ExecuteScalar<string>("SELECT Username FROM Users"));
        }

        [Fact]
        public async Task Host_requires_administrator_csrf_and_confirmation_and_invalidates_sessions()
        {
            await SeedUser(source, "backup-admin", "administrator");
            await SeedUser(source, "backup-cashier", "cashier");
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var url = new Uri("http://127.0.0.1:" + port);
            var licensePath = Path.Combine(root, "destination.alklic");
            var hardware = new TestHardware();
            using var rsa = RsaLicenseSignatures.CreateRsa();
            var signature = new RsaLicenseSignatures(new Dictionary<string, string> { { "backup-key", rsa.ToXmlString(false) } });
            var license = new LicenseService(new FileLicenseStorage(licensePath), signature, hardware);
            var payload = LicenseFormat.CreatePayload(hardware.ReadClaims(), "backup-key", Guid.NewGuid(), DateTime.UtcNow);
            var signed = payload + "|" + Convert.ToBase64String(rsa.SignData(LicenseFormat.Bytes(payload), "SHA256"));
            Assert.True(license.Activate(signed).IsValid);
            var originalLicense = File.ReadAllBytes(licensePath);
            using var server = new AlKhayalServer(license);
            server.Start(new HostOptions { DatabasePath = source, Url = url.ToString(), FrontendPath = root });
            using var admin = new HttpClient { BaseAddress = url };
            using var cashier = new HttpClient { BaseAddress = url };
            const string route = "api/settings/backups";
            Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync(route)).StatusCode);
            await Login(cashier, "backup-cashier");
            Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync(route)).StatusCode);
            foreach (var endpoint in new[] { "/create", "/inspect", "/restore" })
                Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsync(route + endpoint, Json(new { }))).StatusCode);
            await Login(admin, "backup-admin", false);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync(route + "/create", Json(new { }))).StatusCode);
            await Login(admin, "backup-admin");
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(route + "/restore", Json(new { }))).StatusCode);
            var folder = Path.Combine(root, "ApiBackup");
            var configured = await admin.PutAsync(route, Json(new { directory = folder, enabled = false }));
            Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
            var company = "api/settings/company";
            await Data(await admin.PutAsync(company, Json(new { businessName = "Saved company" })));
            var created = await admin.PostAsync(route + "/create", Json(new { }));
            var file = await Data(created);
            var name = (string)file["name"];
            await Data(await admin.PutAsync(company, Json(new { businessName = "Discarded company" })));
            Assert.Equal("Discarded company", (string)(await Data(await admin.GetAsync(company)))["businessName"]);
            var downloaded = await admin.GetAsync(route + "/download?name=" + Uri.EscapeDataString(name));
            Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
            using var upload = new ByteArrayContent(await downloaded.Content.ReadAsByteArrayAsync());
            upload.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            var inspected = await Data(await admin.PostAsync(route + "/inspect", upload));
            var restored = await admin.PostAsync(route + "/restore", Json(new { confirmation = (string)inspected["confirmation"] }));
            var recovery = await Data(restored);
            Assert.True(File.Exists(FixtureIoPath((string)recovery["path"])));
            Assert.True(license.Check().IsValid);
            Assert.Equal(originalLicense, File.ReadAllBytes(licensePath));
            Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("api/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await cashier.GetAsync("api/auth/me")).StatusCode);
            admin.DefaultRequestHeaders.Remove("X-CSRF-Token");
            await Login(admin, "backup-admin");
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(route)).StatusCode);
            Assert.Equal("Saved company", (string)(await Data(await admin.GetAsync(company)))["businessName"]);
        }

        private static StringContent Json(object data) =>
            new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");

        private static async Task<JToken> Data(HttpResponseMessage response)
        {
            using (response)
            {
                var body = await response.Content.ReadAsStringAsync();
                Assert.True(response.IsSuccessStatusCode, response.StatusCode + " " + body);
                return JObject.Parse(body)["data"];
            }
        }

        private static async Task Login(HttpClient client, string username, bool csrf = true)
        {
            var data = await Data(await client.PostAsync("api/auth/login", Json(new { username, password = "isolated-test" })));
            if (csrf) client.DefaultRequestHeaders.Add("X-CSRF-Token", (string)data["csrfToken"]);
        }

        private static SQLiteConnection Open(string path)
        {
            var c = new SQLiteConnection(Db.ConnectionStringFor(path));
            c.Open();
            return c;
        }

        private static async Task SeedUser(string path, string name, string role)
        {
            var users = new SqliteUserStore(Db.ConnectionStringFor(path));
            await users.CreateAsync(name, name, new PasswordHasher().Hash("isolated-test"), role, CancellationToken.None);
        }

        private static void InsertTrial(SQLiteConnection c, string fingerprint, string status) =>
            c.Execute(@"INSERT INTO TrialState VALUES (1,'trial',@fingerprint,'2026-11-01','2026-11-06',
                '2026-11-05','2026-11-05',@status,23,'protected-proof')", new { fingerprint, status });

        private static string FixtureIoPath(string path, bool force = false) =>
            force || path.Length >= 248 ? @"\\?\" + path : path;

        public void Dispose()
        {
            SQLiteConnection.ClearAllPools();
            Directory.Delete(FixtureIoPath(root, force: true), true);
        }

        private sealed class TrialClock : ITrialClock
        {
            public DateTime UtcNow { get; set; } = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);
            public TimeSpan Elapsed => TimeSpan.Zero;
        }

        private sealed class NoLicense : ILicenseGate
        {
            public LicenseStatus Check() => new LicenseStatus(false, "test only");
        }

        private sealed class TestHardware : IHardwareIdentity
        {
            public IDictionary<string, string> ReadClaims() => LicenseFormat.HashClaims(new Dictionary<string, string>
            {
                { "uuid", "backup-test-uuid" }, { "board", "backup-test-board" }, { "bios", "backup-test-bios" }
            });
        }
    }
}
