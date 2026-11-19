using System;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using AlKhayal.Application.Users;
using AlKhayal.Data.Authentication;
using AlKhayal.Server.Host;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class TreasuryApiTests
    {
        [Fact]
        public async Task Authenticated_api_requests_are_logged_with_actor_time_path_and_outcome_and_logs_are_admin_only_immutable()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var frontend = Path.Combine(Path.GetDirectoryName(f.DatabasePath), "frontend");
                Directory.CreateDirectory(Path.Combine(frontend, "data"));
                File.WriteAllText(Path.Combine(frontend, "data", "screens.json"), "{\"screens\":[]}");
                var password = Guid.NewGuid().ToString("N");
                var users = new SqliteUserStore(f.ConnectionString);
                await users.CreateAsync("audit-admin", "مسؤول السجل", new PasswordHasher().Hash(password), UserRoles.Administrator, CancellationToken.None);
                await users.CreateAsync("audit-cashier", "كاشير السجل", new PasswordHasher().Hash(password), UserRoles.Cashier, CancellationToken.None);
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                var address = new Uri("http://127.0.0.1:" + port);
                using (var server = new AlKhayalServer(new TestLicenseGate()))
                {
                    server.Start(new HostOptions { DatabasePath = f.DatabasePath, FrontendPath = frontend, Url = address.ToString().TrimEnd('/') });
                    using (var admin = Client(address))
                    using (var cashier = Client(address))
                    {
                        var csrf = await Login(admin, "audit-admin", password);
                        await Login(cashier, "audit-cashier", password);
                        var created = await Send(admin, HttpMethod.Post, "api/customers", new { name = "عميل تدقيق", phone = "010", balance = 25, status = "نشط" }, csrf);
                        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
                        var rejected = await Send(admin, HttpMethod.Post, "api/customers", new { name = "", phone = "010" }, csrf);
                        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("api/customers")).StatusCode);

                        var response = await admin.GetAsync("api/operation-logs?search=%2Fapi%2Fcustomers&page=1&pageSize=20");
                        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                        var raw = await response.Content.ReadAsStringAsync();
                        var data = JObject.Load(new JsonTextReader(new StringReader(raw)) { DateParseHandling = DateParseHandling.None })["data"];
                        var items = (JArray)data["items"];
                        Assert.Equal(3, (long)data["totalCount"]);
                        var createdLog = items.Single(row => (int)row["statusCode"] == 201);
                        Assert.Equal("audit-admin", (string)createdLog["actorUsername"]);
                        Assert.Equal("مسؤول السجل", (string)createdLog["actorDisplayName"]);
                        Assert.Equal("POST", (string)createdLog["httpMethod"]);
                        Assert.Equal("/api/customers", (string)createdLog["resource"]);
                        Assert.Equal("success", (string)createdLog["outcome"]);
                        Assert.EndsWith("Z", (string)createdLog["occurredUtc"]);
                        Assert.Contains(items, row => (string)row["actorUsername"] == "audit-admin" && (string)row["actorDisplayName"] == "مسؤول السجل" && (string)row["httpMethod"] == "POST" && (string)row["resource"] == "/api/customers" && (string)row["outcome"] == "success" && (int)row["statusCode"] == 201 && ((string)row["occurredUtc"]).EndsWith("Z", StringComparison.Ordinal));
                        Assert.Contains(items, row => (string)row["outcome"] == "failure" && (int)row["statusCode"] == 400);
                        Assert.Contains(items, row => (string)row["httpMethod"] == "GET" && (string)row["resource"] == "/api/customers" && (string)row["outcome"] == "success");
                        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/operation-logs")).StatusCode);

                        using (var connection = f.Open())
                        {
                            Assert.Throws<System.Data.SQLite.SQLiteException>(() => connection.Execute("UPDATE OperationAuditLog SET Resource='/tampered'"));
                            Assert.Throws<System.Data.SQLite.SQLiteException>(() => connection.Execute("DELETE FROM OperationAuditLog"));
                        }
                    }
                }
            }
        }
    }
}
