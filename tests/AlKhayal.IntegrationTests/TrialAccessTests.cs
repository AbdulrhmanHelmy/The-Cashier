using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using AlKhayal.Application.Licensing;
using AlKhayal.Server.Host;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class TrialAccessTests
    {
        private sealed class Clock : ITrialClock
        {
            public DateTime UtcNow { get; set; } = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);
            public TimeSpan Elapsed { get; set; }
            public void Advance(TimeSpan span) { UtcNow += span; Elapsed += span; }
        }
        private sealed class Store : ITrialStore
        {
            private readonly TrialRecord[] records = new TrialRecord[3];
            public TrialRecord[] ReadAll() => (TrialRecord[])records.Clone();
            public void WriteAll(TrialRecord record) { for (int i = 0; i < 3; i++) records[i] = record; }
        }
        private sealed class Permanent : ILicenseGate
        {
            public bool Valid;
            public LicenseStatus Check() => new LicenseStatus(Valid, "test");
        }
        [Fact]
        public async Task Direct_business_routes_lock_at_expiry_and_permanent_activation_restores_access()
        {
            var root = Path.Combine(Path.GetTempPath(), "alk-trial-api-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var options = new HostOptions { Url = "http://127.0.0.1:" + port, DatabasePath = Path.Combine(root, "test.db"), FrontendPath = root };
            var clock = new Clock(); var permanent = new Permanent();
            var trial = new TrialSecurityService(permanent, new Store(), clock, "device");
            try
            {
                using (var server = new AlKhayalServer(trial))
                {
                    server.Start(options);
                    using (var client = new HttpClient { BaseAddress = new Uri(options.Url) })
                    {
                        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("api/sales")).StatusCode);
                        Assert.Equal(TrialState.Active, trial.Start().State);
                        Assert.NotEqual(HttpStatusCode.Forbidden, (await client.GetAsync("api/sales")).StatusCode);
                        clock.Advance(TimeSpan.FromHours(120));
                        foreach (var path in new[] { "api/sales", "api/auth/me", "api/system/health", "index.html" })
                            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
                        foreach (var path in new[] { "api/sales/drafts", "api/purchases", "api/inventory/adjustments",
                            "api/customers", "api/suppliers", "api/treasury/transactions", "api/sales/installments" })
                            using (var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"))
                                Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(path, content)).StatusCode);
                        permanent.Valid = true;
                        Assert.NotEqual(HttpStatusCode.Forbidden, (await client.GetAsync("api/sales")).StatusCode);
                    }
                }
            }
            finally { System.Data.SQLite.SQLiteConnection.ClearAllPools(); Directory.Delete(root, true); }
        }
    }
}
