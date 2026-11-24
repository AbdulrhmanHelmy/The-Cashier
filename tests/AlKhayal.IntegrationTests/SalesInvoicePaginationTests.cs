using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Users;
using AlKhayal.Data.Authentication;
using AlKhayal.Server.Host;
using Newtonsoft.Json.Linq;
using AlKhayal.Data.Database;
using AlKhayal.Data.Sales;
using Dapper;
using Xunit;
using Xunit.Abstractions;

namespace AlKhayal.IntegrationTests
{
    public sealed class SalesInvoicePaginationTests
    {
        private readonly ITestOutputHelper _output;

        public SalesInvoicePaginationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Release_host_serves_authenticated_pages_and_keeps_legacy_collection_routes()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var ct = CancellationToken.None;
                await new SqliteUserStore(cs).CreateAsync("paging-admin", "مسؤول", new PasswordHasher().Hash("test-only"), UserRoles.Administrator, ct);
                using (var c = new SQLiteConnection(cs))
                {
                    c.Open();
                    c.Execute(@"INSERT INTO Suppliers(Code,Name,Phone,CreatedAt,UpdatedAt) VALUES('PAG','مورد','','2026-01-01','2026-01-01');
                        WITH RECURSIVE Seed(Id) AS (SELECT 1 UNION ALL SELECT Id+1 FROM Seed WHERE Id<551)
                        INSERT INTO PurchaseInvoices(SupplierId,WarehouseId,InvoiceNumber,InvoiceDate,PaymentMode,Status,TotalScaled,
                            CreatedByUserId,UpdatedByUserId,CreatedUtc,UpdatedUtc)
                        SELECT 1,1,'P-'||Id,'2026-01-01','cash','draft',10000,1,1,'2026-01-01','2026-01-01' FROM Seed");
                    using (var reader = c.ExecuteReader("EXPLAIN QUERY PLAN SELECT Id FROM PurchaseInvoices ORDER BY InvoiceDate DESC, Id DESC LIMIT 50 OFFSET 500"))
                    {
                        Assert.True(reader.Read());
                        Assert.Contains("IX_PurchaseInvoices_InvoiceDate_Id", reader.GetString(3));
                        Assert.False(reader.Read());
                    }
                }
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                using (var server = new AlKhayalServer(new TestLicenseGate()))
                using (var client = new HttpClient(new HttpClientHandler { UseCookies = true }) { BaseAddress = new Uri(url) })
                {
                    server.Start(new HostOptions { DatabasePath = path, Url = url, FrontendPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) });
                    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/sales/page")).StatusCode);
                    using (var login = new StringContent("{\"username\":\"paging-admin\",\"password\":\"test-only\"}", Encoding.UTF8, "application/json"))
                        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("api/auth/login", login)).StatusCode);
                    var sales = JObject.Parse(await client.GetStringAsync("api/sales/page"))["data"];
                    Assert.Equal(50, (int)sales["pageSize"]);
                    Assert.Empty((JArray)sales["items"]);
                    var screenResponse = await client.GetAsync("api/sales/page?search=&page=1&pageSize=50");
                    _output.WriteLine("Screen request: " + screenResponse.StatusCode + " " + await screenResponse.Content.ReadAsStringAsync());
                    Assert.Equal(HttpStatusCode.OK, screenResponse.StatusCode);
                    using (var c = new SQLiteConnection(cs))
                    {
                        c.Open();
                        c.Execute(@"WITH RECURSIVE Seed(Id) AS (SELECT 1 UNION ALL SELECT Id+1 FROM Seed WHERE Id<51)
                            INSERT INTO SalesInvoices(InvoiceNumber,InvoiceDate,WarehouseId,CashierUserId,Status,PaymentMode,SubtotalScaled,DiscountScaled,TaxScaled,TotalScaled,CreatedUtc)
                            SELECT 'SCREEN-'||Id,'2026-01-01',1,1,'draft','cash',10000,0,0,10000,'2026-01-01' FROM Seed");
                    }
                    var first = JObject.Parse(await client.GetStringAsync("api/sales/page?search=&page=1&pageSize=50"))["data"];
                    Assert.Equal(51, (int)first["totalCount"]);
                    Assert.Equal(50, ((JArray)first["items"]).Count);
                    var searched = JObject.Parse(await client.GetStringAsync("api/sales/page?search=SCREEN-1&page=1&pageSize=25"))["data"];
                    Assert.Equal(11, (int)searched["totalCount"]);
                    Assert.Equal(1, (int)JObject.Parse(await client.GetStringAsync("api/sales/page?search=missing&page=8&pageSize=50"))["data"]["page"]);
                    Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("api/sales/page?page=abc")).StatusCode);
                    Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("api/sales/page?page=0")).StatusCode);
                    Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("api/sales/page?pageSize=49")).StatusCode);
                    Assert.IsType<JArray>(JObject.Parse(await client.GetStringAsync("api/sales"))["data"]);
                    var purchases = JObject.Parse(await client.GetStringAsync("api/purchases/page?page=12"))["data"];
                    Assert.Equal(551, (int)purchases["totalCount"]);
                    Assert.Equal(12, (int)purchases["page"]);
                    Assert.Equal(1, (int)Assert.Single((JArray)purchases["items"])["id"]);
                    Assert.Equal(500, ((JArray)JObject.Parse(await client.GetStringAsync("api/purchases"))["data"]).Count);
                }
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                File.Delete(path);
            }
        }

        [Theory]
        [InlineData(1000)]
        [InlineData(10000)]
        [InlineData(50000)]
        [InlineData(100000)]
        [InlineData(500000)]
        public async Task Pages_are_bounded_and_search_reaches_old_invoices(int count)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
            var connectionString = Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(connectionString);
                using (var c = new SQLiteConnection(connectionString))
                {
                    c.Open();
                    c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('paging-test','test-only','2026-01-01')");
                    c.Execute(@"WITH RECURSIVE Seed(Id) AS (SELECT 1 UNION ALL SELECT Id+1 FROM Seed WHERE Id<@Count)
                        INSERT INTO SalesInvoices(InvoiceNumber,InvoiceDate,WarehouseId,CashierUserId,Status,PaymentMode,
                            SubtotalScaled,DiscountScaled,TaxScaled,TotalScaled,CreatedUtc)
                        SELECT 'PAGE-' || printf('%06d',Id),'2026-01-01',1,1,'draft','cash',10000,0,0,10000,'2026-01-01' FROM Seed",
                        new { Count = count });
                    Assert.Empty(c.Query("PRAGMA foreign_key_check"));
                    using (var reader = c.ExecuteReader(@"EXPLAIN QUERY PLAN SELECT si.Id FROM SalesInvoices si
                        WHERE NOT EXISTS(SELECT 1 FROM SalesDraftDeletions dd WHERE dd.SalesInvoiceId=si.Id)
                        AND NOT EXISTS(SELECT 1 FROM SalesInvoiceCorrections correction WHERE correction.PreviousInvoiceId=si.Id)
                        ORDER BY si.Id DESC LIMIT 50 OFFSET 50"))
                    {
                        while (reader.Read()) _output.WriteLine(reader.GetString(3));
                    }
                }

                var sales = new SalesService(new SqliteSalesStore(connectionString));
                var ct = CancellationToken.None;
                var timer = Stopwatch.StartNew();
                var legacy = await sales.ListAsync(null, null, null, ct);
                var legacyTime = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                var first = await sales.ListPageAsync(null, 1, 50, ct);
                var firstTime = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                var second = await sales.ListPageAsync(null, 2, 50, ct);
                var nextTime = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                var last = await sales.ListPageAsync(null, long.MaxValue, 50, ct);
                var lastTime = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                var oldest = await sales.ListPageAsync("PAGE-000001", 1, 25, ct);
                var searchTime = timer.Elapsed.TotalMilliseconds;
                _output.WriteLine($"{count}: legacy {legacyTime:F2} ms/{legacy.Count} rows, first {firstTime:F2} ms, next {nextTime:F2} ms, last {lastTime:F2} ms, search {searchTime:F2} ms");

                Assert.Equal(count, first.TotalCount);
                Assert.Equal(50, first.Items.Count);
                Assert.Equal(legacy.Take(50).Select(x => x.Id), first.Items.Select(x => x.Id));
                Assert.Equal(Enumerable.Range(count - 99, 100).Reverse().Select(x => (long)x),
                    first.Items.Concat(second.Items).Select(x => x.Id));
                Assert.Equal(count / 50, last.Page);
                Assert.Equal(1, last.Items.Last().Id);
                Assert.Equal(1, oldest.TotalCount);
                Assert.Equal(1, Assert.Single(oldest.Items).Id);
                var empty = await sales.ListPageAsync("missing invoice number", 5, 200, ct);
                Assert.Empty(empty.Items);
                Assert.Equal(1, empty.Page);
                Assert.Equal(0, empty.TotalCount);
                await Assert.ThrowsAsync<ArgumentException>(() => sales.ListPageAsync(null, 1, 49, ct));
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                File.Delete(path);
            }
        }
    }
}
