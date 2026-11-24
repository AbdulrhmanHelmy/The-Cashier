using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Users;
using AlKhayal.Data.Authentication;
using AlKhayal.Server.Host;
using Dapper;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class TreasuryApiTests
    {
        [Fact]
        public async Task Global_search_directory_filters_records_and_enforces_effective_screen_access()
        {
            using (var fixture = new TreasuryPersistenceTests.Fixture())
            {
                var frontend = Path.Combine(Path.GetDirectoryName(fixture.DatabasePath), "search-frontend");
                Directory.CreateDirectory(Path.Combine(frontend, "data"));
                var routes = new[] { "customers-list", "customer-statement", "suppliers-list", "purchase-management", "maintenance-tracking",
                    "installment-payment", "drivers-management", "delivery-orders", "expense-add", "accounts-list",
                    "products-list", "sales-management" };
                File.WriteAllText(Path.Combine(frontend, "data", "screens.json"), new JObject
                {
                    ["screens"] = new JArray(routes.Select(route => new JObject
                    {
                        ["route"] = route, ["title"] = route, ["module"] = "اختبار"
                    }))
                }.ToString());
                using (var connection = fixture.Open())
                {
                    connection.Execute(@"WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM n WHERE i<5000)
                        INSERT INTO Customers(Code,Name,Phone,CreatedAt,UpdatedAt)
                        SELECT 'GLOB-'||i,'عميل تجريبي طويل '||i,'01000'||printf('%05d',i),'2026-11-21','2026-11-21' FROM n");
                    connection.Execute(@"WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM n WHERE i<4000)
                        INSERT INTO Products(Name,Price) SELECT 'منتج GLOB '||printf('%05d',i),0 FROM n;
                        INSERT INTO ProductDetails(ProductId,Description,CategoryId,BaseUnitId,ProductType,SellPriceScaled,PurchasePriceScaled,IsActive,ArchivedUtc,MigrationState,Version,CreatedUtc,UpdatedUtc)
                        SELECT Id,'',NULL,1,'stock',125000,100000,1,NULL,'ready',1,'2026-11-21T00:00:00Z','2026-11-21T00:00:00Z' FROM Products WHERE Name LIKE 'منتج GLOB %';
                        INSERT INTO Suppliers(Id,Code,Name,Phone,CreatedAt,UpdatedAt)
                        VALUES(100,'GLOB-SUP','مورد تجريبي GLOB','01009990000','2026-11-21','2026-11-21');
                        WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM n WHERE i<2500)
                        INSERT INTO PurchaseInvoices(SupplierId,WarehouseId,InvoiceNumber,InvoiceDate,PaymentMode,Status,TotalScaled,CreatedByUserId,UpdatedByUserId,CreatedUtc,UpdatedUtc)
                        SELECT 100,1,'GLOB-'||printf('%05d',i),'2026-11-21','credit','draft',0,@UserId,@UserId,'2026-11-21','2026-11-21' FROM n;
                        WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM n WHERE i<2500)
                        INSERT INTO SalesInvoices(InvoiceNumber,InvoiceDate,WarehouseId,CashierUserId,Status,PaymentMode,SubtotalScaled,DiscountScaled,TaxScaled,TotalScaled,CreatedUtc)
                        SELECT 'GLOB-'||printf('%05d',i),'2026-11-21',1,@UserId,'draft','cash',0,0,0,0,'2026-11-21' FROM n",
                        new { fixture.UserId });
                }
                var password = Guid.NewGuid().ToString("N");
                var users = new SqliteUserStore(fixture.ConnectionString);
                var hash = new PasswordHasher().Hash(password);
                await users.CreateAsync("search-admin", "مدير", hash, UserRoles.Administrator, CancellationToken.None);
                var operatorId = await users.CreateAsync("search-operator", "مشغل", hash, UserRoles.Cashier, CancellationToken.None);
                await users.CreateAsync("search-restricted", "كاشير", hash, UserRoles.Cashier, CancellationToken.None);
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                var url = new Uri("http://127.0.0.1:" + port);
                using (var server = new AlKhayalServer(new TestLicenseGate()))
                using (var admin = Client(url))
                using (var permitted = Client(url))
                using (var restricted = Client(url))
                using (var anonymous = Client(url))
                {
                    server.Start(new HostOptions { DatabasePath = fixture.DatabasePath, FrontendPath = frontend, Url = url.ToString().TrimEnd('/') });
                    var csrf = await Login(admin, "search-admin", password);
                    await Login(permitted, "search-operator", password);
                    await Login(restricted, "search-restricted", password);
                    var grants = "api/users/" + operatorId + "/screen-permissions";
                    Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, grants, new { screenIds = routes }, csrf)).StatusCode);
                    const string customerPage = "api/customers/page?page=2&pageSize=25&sort=name";
                    Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(customerPage)).StatusCode);
                    Assert.Equal(HttpStatusCode.Forbidden, (await restricted.GetAsync(customerPage)).StatusCode);
                    var directoryPage = JObject.Parse(await permitted.GetStringAsync(customerPage))["data"];
                    Assert.Equal(5000, (int)directoryPage["totalCount"]);
                    Assert.Equal(25, ((JArray)directoryPage["items"]).Count);
                    var id = (long)directoryPage["items"][0]["id"];
                    var statement = JObject.Parse(await permitted.GetStringAsync("api/customers/" + id + "/statement?page=1"))["data"];
                    Assert.Equal(1, (int)statement["page"]);
                    Assert.Equal(0, (int)statement["totalCount"]);
                    const string customerSearch = "api/customers/directory-search?query=";
                    var searches = new[] { customerSearch + "GLOB", "api/suppliers/directory-search?query=GLOB",
                        "api/purchases/directory-search?query=GLOB", "api/maintenance/tickets/directory-search?query=GLOB",
                        "api/customers/installment-search?query=GLOB", "api/delivery/drivers?limit=20&search=GLOB",
                        "api/delivery/orders?search=GLOB&pageSize=10", "api/expenses/vouchers/directory-search?query=GLOB",
                        "api/treasury/accounts/directory-search?query=GLOB", "api/catalog/products?directorySearch=true&pageSize=20&search=GLOB",
                        "api/sales?searchLimit=20&search=GLOB" };
                    foreach (var search in searches)
                    {
                        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(search)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await restricted.GetAsync(search)).StatusCode);
                        using (var response = await admin.GetAsync(search))
                            Assert.True(response.StatusCode == HttpStatusCode.OK, "Administrator: " + search + " returned " + response.StatusCode);
                        using (var response = await permitted.GetAsync(search))
                            Assert.True(response.StatusCode == HttpStatusCode.OK, "Operator: " + search + " returned " + response.StatusCode);
                    }
                    var stopwatch = Stopwatch.StartNew();
                    var broad = (JArray)JObject.Parse(await permitted.GetStringAsync(customerSearch + "GLOB"))["data"];
                    stopwatch.Stop();
                    Console.WriteLine("HTTP customer directory search with 5,000 rows: {0} ms, {1} results", stopwatch.Elapsed.TotalMilliseconds, broad.Count);
                    Assert.Equal(20, broad.Count);
                    foreach (var sample in new[] {
                        new { Label = "products (4,000)", Url = "api/catalog/products?directorySearch=true&pageSize=20&search=GLOB" },
                        new { Label = "sales invoices (2,500)", Url = "api/sales?searchLimit=20&search=GLOB" },
                        new { Label = "purchase invoices (2,500)", Url = "api/purchases/directory-search?query=GLOB" }
                    })
                    {
                        stopwatch.Restart();
                        var page = JObject.Parse(await permitted.GetStringAsync(sample.Url))["data"];
                        stopwatch.Stop();
                        var items = page as JArray ?? (JArray)page["items"];
                        Assert.Equal(20, items.Count);
                        Console.WriteLine("HTTP directory search {0}: {1} ms, {2} results", sample.Label, stopwatch.Elapsed.TotalMilliseconds, items.Count);
                    }
                    foreach (var endpoint in new[] { "api/sales?searchLimit=20&search=", "api/purchases/directory-search?query=" })
                    {
                        var invoices = (JArray)JObject.Parse(await permitted.GetStringAsync(endpoint + "GLOB-00001"))["data"];
                        Assert.Equal("GLOB-00001", (string)Assert.Single(invoices)["invoiceNumber"]);
                    }
                    var exact = (JArray)JObject.Parse(await permitted.GetStringAsync(customerSearch + "0100005000"))["data"];
                    var match = Assert.Single(exact);
                    Assert.Equal("GLOB-5000", (string)match["code"]);
                    Assert.Equal(new[] { "code", "id", "name", "phone" }, ((JObject)match).Properties().Select(p => p.Name).OrderBy(x => x).ToArray());
                    var detail = "api/customers/directory-record/" + (long)match["id"];
                    Assert.True(JToken.DeepEquals(match, JObject.Parse(await permitted.GetStringAsync(detail))["data"]));
                    Assert.Equal(HttpStatusCode.Forbidden, (await restricted.GetAsync(detail)).StatusCode);
                    Assert.Equal(HttpStatusCode.NotFound, (await permitted.GetAsync("api/customers/directory-record/999999")).StatusCode);
                    Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(customerSearch)).StatusCode);
                    Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, grants, new { screenIds = new string[0] }, csrf)).StatusCode);
                    Assert.Equal(HttpStatusCode.Forbidden, (await permitted.GetAsync(customerSearch + "GLOB")).StatusCode);
                }
            }
        }
    }
}
