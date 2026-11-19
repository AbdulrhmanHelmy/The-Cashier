using System;
using System.IO;
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
        public async Task Touch_pos_invoice_http_view_versioned_edit_and_posted_rejection()
        {
            using(var f=new SalesTreasuryTests.Fixture())
            {
                await f.Prepare(openShift:false);
                var frontend=Path.Combine(Path.GetDirectoryName(f.Db.DatabasePath),"frontend");Directory.CreateDirectory(Path.Combine(frontend,"data"));
                File.WriteAllText(Path.Combine(frontend,"data","screens.json"),"{\"screens\":[{\"route\":\"pos\",\"title\":\"الكاشير اللمسي\",\"module\":\"المبيعات\"}]}");
                var password=Guid.NewGuid().ToString("N");var users=new SqliteUserStore(f.Db.ConnectionString);
                await users.CreateAsync("edit-http", "مدير",new PasswordHasher().Hash(password),UserRoles.Administrator,CancellationToken.None);
                var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
                using(var server=new AlKhayalServer(new TestLicenseGate()))
                {
                    var url=new Uri("http://127.0.0.1:"+port);server.Start(new HostOptions{DatabasePath=f.Db.DatabasePath,FrontendPath=frontend,Url=url.ToString().TrimEnd('/')});
                    using(var client=Client(url))using(var anon=Client(url))
                    {
                        var csrf=await Login(client,"edit-http",password);
                        var created=await Send(client,HttpMethod.Post,"api/sales/drafts",new{warehouseId=1,invoiceDate="2026-11-01",paymentMode="cash",discount="0.00",lines=new[]{new{variantId=f.Variant,productUnitId=f.Unit,quantity="1",unitPrice="12.34",discount="0.00"}}},csrf);
                        Assert.Equal(HttpStatusCode.Created,created.StatusCode);var original=JObject.Parse(await created.Content.ReadAsStringAsync())["data"];var id=(long)original["id"];var version=(long)original["version"];
                        var route="api/sales/"+id;
                        Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync(route)).StatusCode);
                        var viewed=JObject.Parse(await (await client.GetAsync(route)).Content.ReadAsStringAsync())["data"];
                        Assert.Equal(original["invoiceNumber"],viewed["invoiceNumber"]);Assert.Equal("1",(string)viewed["lines"][0]["quantity"]);
                        var edit=new{expectedVersion=version,warehouseId=1,invoiceDate="2026-11-01",paymentMode="cash",discount="1.00",lines=new[]{new{variantId=f.Variant,productUnitId=f.Unit,quantity="2",unitPrice="12.34",discount="0.00"}}};
                        Assert.Equal(HttpStatusCode.Forbidden,(await Send(client,HttpMethod.Put,route,edit,null)).StatusCode);
                        var updated=await Send(client,HttpMethod.Put,route,edit,csrf);Assert.Equal(HttpStatusCode.OK,updated.StatusCode);
                        Assert.Equal(236800,(long)JObject.Parse(await updated.Content.ReadAsStringAsync())["data"]["totalScaled"]);
                        Assert.Equal(HttpStatusCode.Conflict,(await Send(client,HttpMethod.Put,route,edit,csrf)).StatusCode);
                        using(var c=f.Db.Open()){Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryCostAllocations"));}
                        Assert.Equal(HttpStatusCode.Conflict,(await Send(client,HttpMethod.Post,route+"/post",new{operationKey="edit-http-post"},csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK,(await Send(client,HttpMethod.Post,"api/workforce/shifts/open",new{amount="0",operationKey="edit-http-shift"},csrf)).StatusCode);
                        var posted=await Send(client,HttpMethod.Post,route+"/post",new{operationKey="edit-http-post"},csrf);Assert.Equal(HttpStatusCode.OK,posted.StatusCode);
                        var posting=JObject.Parse(await posted.Content.ReadAsStringAsync())["data"];
                        Assert.Equal(HttpStatusCode.Conflict,(await Send(client,HttpMethod.Put,route,edit,csrf)).StatusCode);
                        var replay=await Send(client,HttpMethod.Post,route+"/post",new{operationKey="edit-http-post"},csrf);Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
                        Assert.Equal(posting["treasuryMovementId"],JObject.Parse(await replay.Content.ReadAsStringAsync())["data"]["treasuryMovementId"]);
                        var postedView=JObject.Parse(await (await client.GetAsync(route)).Content.ReadAsStringAsync())["data"];
                        Assert.True((bool)postedView["canEditPosted"]);var accountId=(long)postedView["settlementAccounts"][0]["id"];
                        var correction=new{expectedVersion=(long)postedView["version"],operationKey="edit-http-correction",settlementKind="refund",settlementMethod="cash",treasuryAccountId=accountId,invoice=new{expectedVersion=(long)postedView["version"],customerId=(long?)null,warehouseId=1,invoiceDate="2026-11-01",paymentMode="cash",immediateMethod="cash",discount="0.00",lines=new[]{new{variantId=f.Variant,productUnitId=f.Unit,quantity="1",unitPrice="12.34",discount="0.00"}}}};
                        Assert.Equal(HttpStatusCode.Forbidden,(await Send(client,HttpMethod.Post,route+"/corrections",correction,null)).StatusCode);
                        var correctedResponse=await Send(client,HttpMethod.Post,route+"/corrections",correction,csrf);Assert.Equal(HttpStatusCode.OK,correctedResponse.StatusCode);
                        var corrected=JObject.Parse(await correctedResponse.Content.ReadAsStringAsync())["data"];Assert.True((bool)corrected["succeeded"]);Assert.Equal(original["invoiceNumber"],corrected["invoice"]["invoiceNumber"]);Assert.Equal(123400,(long)corrected["invoice"]["totalScaled"]);
                        var correctionReplay=JObject.Parse(await (await Send(client,HttpMethod.Post,route+"/corrections",correction,csrf)).Content.ReadAsStringAsync())["data"];Assert.True((bool)correctionReplay["replayed"]);
                        using(var c=f.Db.Open())
                        {
                            Assert.Equal(19000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1"));
                            Assert.Equal(270000,c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM InventoryCostAllocations"));
                            Assert.Equal(123400,c.ExecuteScalar<long>("SELECT SUM(CASE WHEN EventType IN ('cash_sale','customer_collection') THEN AmountScaled WHEN EventType='refund' AND PaymentMethod<>'credit_adjustment' THEN -AmountScaled ELSE 0 END) FROM SalesPayments"));
                            Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesTreasuryReceipts"));
                            Assert.Equal(123400,c.ExecuteScalar<long>("SELECT BalanceAfterScaled FROM TreasuryMovements ORDER BY Id DESC LIMIT 1"));
                            Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesInvoiceCorrections"));
                        }
                    }
                }
            }
        }
    }
}
