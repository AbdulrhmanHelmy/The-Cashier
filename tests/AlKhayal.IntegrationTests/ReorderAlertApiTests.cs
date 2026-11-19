using System;
using System.IO;
using System.Data.SQLite;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Users;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Database;
using AlKhayal.Data.Inventory;
using AlKhayal.Server.Host;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class ReorderAlertApiTests
    {
        [Fact]
        public async Task Endpoint_preserves_authentication_full_and_legacy_screen_grants()
        {
            var root=Path.Combine(Path.GetTempPath(),"alkhayal-alert-api-"+Guid.NewGuid().ToString("N"));
            var path=Path.Combine(root,"isolated.db");var frontend=Path.Combine(root,"frontend");AlKhayalServer server=null;
            try
            {
                Directory.CreateDirectory(Path.Combine(frontend,"data"));
                File.WriteAllText(Path.Combine(frontend,"data","screens.json"),"{\"screens\":[{\"route\":\"alerts-list\",\"implemented\":true},{\"route\":\"reorder-alert\",\"permissionControllable\":true},{\"route\":\"low-stock-alert\",\"permissionControllable\":true},{\"route\":\"product-add\",\"implemented\":true},{\"route\":\"product-edit\",\"implemented\":true},{\"route\":\"products-list\",\"implemented\":true}]}");
                var cs=Db.ConnectionStringFor(path);Migrator.Run(cs);var users=new SqliteUserStore(cs);var hasher=new PasswordHasher();
                await users.CreateAsync("catalog-admin","مسؤول",hasher.Hash("secret"),UserRoles.Administrator,CancellationToken.None);
                var cashierId=await users.CreateAsync("catalog-cashier","كاشير",hasher.Hash("secret"),UserRoles.Cashier,CancellationToken.None);
                var url="http://127.0.0.1:"+FreePort();server=new AlKhayalServer(new TestLicenseGate());server.Start(new HostOptions{DatabasePath=path,Url=url,FrontendPath=frontend});
                using(var anonymous=Client(url))Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("api/alerts")).StatusCode);
                using(var admin=Client(url))using(var cashier=Client(url))
                {
                    var csrf=await Login(admin,"catalog-admin");await Login(cashier,"catalog-cashier");
                    Assert.Equal(HttpStatusCode.Forbidden,(await cashier.GetAsync("api/alerts")).StatusCode);
                    var created=await Post(admin,csrf,"api/catalog/products","{\"operationKey\":\"reorder-api\",\"name\":\"صنف تنبيه\",\"baseUnitId\":1,\"salePrice\":\"1\",\"reorderPoint\":\"10.5\",\"isActive\":true}");
                    Assert.Equal(HttpStatusCode.Created,created.StatusCode);var text=await created.Content.ReadAsStringAsync();
                    Assert.Contains("\"reorderPoint\":\"10.5\"",text);
                    var productId=Regex.Match(text,"\\\"id\\\":(\\d+)").Groups[1].Value;
                    foreach(var route in new[]{"alerts-list","reorder-alert","low-stock-alert"})
                    {
                        Assert.Equal(HttpStatusCode.OK,(await Put(admin,csrf,"api/users/"+cashierId+"/screen-permissions","{\"screenIds\":[\""+route+"\"]}")).StatusCode);
                        var result=await cashier.GetAsync("api/alerts");Assert.Equal(HttpStatusCode.OK,result.StatusCode);var json=await result.Content.ReadAsStringAsync();
                        Assert.Contains("\"type\":\"low-stock\"",json);Assert.Contains("\"totalQuantity\":\"0\"",json);Assert.Contains("\"reorderPoint\":\"10.5\"",json);
                    }
                    long parsedProductId=long.Parse(productId),variantId,unitId,userId;
                    string expiry=DateTime.UtcNow.Date.AddDays(10).ToString("yyyy-MM-dd"),due=DateTime.UtcNow.Date.AddDays(5).ToString("yyyy-MM-dd");
                    using(var c=new SQLiteConnection(cs))
                    {
                        c.Open();
                        userId=c.ExecuteScalar<long>("SELECT Id FROM Users WHERE Username='catalog-admin'");
                        variantId=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@parsedProductId AND IsDefault=1",new{parsedProductId});
                        unitId=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@parsedProductId AND IsBase=1",new{parsedProductId});
                        foreach(var capability in new[]{"batch-tracking","expiry-dates"})c.Execute("INSERT INTO ProductCapabilityAssignments(ProductId,CapabilityKey,IsEnabled,AssignedUtc,UpdatedUtc,ChangedByUserId) VALUES(@parsedProductId,@capability,1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z',@userId)",new{parsedProductId,capability,userId});
                    }
                    var receipt=await new SqliteInventoryStore(cs).PostAsync(new InventoryPostCommand{OperationKey="alert-api-expiry",Type="manual_receipt",WarehouseId=1,UserId=userId,Reason="اختبار API التنبيهات",Lines=new[]{new InventoryLineInput{VariantId=variantId,ProductUnitId=unitId,Quantity="1",UnitCost="1",SalePrice="1",LotNumber="API-LOT",ExpirationDate=expiry}}},"api-alert-test",CancellationToken.None);
                    Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                    using(var c=new SQLiteConnection(cs))
                    {
                        c.Open();
                        c.Execute("INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('ALERT-API','عميل API','01000000001','',0,0,'أساسي','نشط','2026-01-01','2026-01-01')");
                        var customerId=c.ExecuteScalar<long>("SELECT last_insert_rowid()");
                        c.Execute(@"INSERT INTO CustomerInstallmentContracts(CustomerId,ProductId,OperationKey,RequestHash,ContractNumber,ProductName,ProductPriceScaled,DownPaymentScaled,InterestRateBasisPoints,TotalAmountScaled,InstallmentCount,IntervalMonths,FirstDueOn,CreatedUtc)
                            VALUES(@customerId,@parsedProductId,'alert-api-contract','hash','ALERT-API-CONTRACT','صنف تنبيه',1000000,0,0,1000000,1,1,@due,'2026-01-01T00:00:00Z')",new{customerId,parsedProductId,due});
                        var contractId=c.ExecuteScalar<long>("SELECT last_insert_rowid()");
                        c.Execute("INSERT INTO CustomerInstallmentSchedule(ContractId,InstallmentNumber,DueOn,AmountScaled) VALUES(@contractId,1,@due,1000000)",new{contractId,due});
                    }
                    Assert.Equal(HttpStatusCode.OK,(await Put(admin,csrf,"api/users/"+cashierId+"/screen-permissions","{\"screenIds\":[\"alerts-list\"]}")).StatusCode);
                    var fullJson=await cashier.GetStringAsync("api/alerts");
                    Assert.Contains("\"type\":\"reorder\"",fullJson);Assert.Contains("\"type\":\"expiry\"",fullJson);Assert.Contains("\"type\":\"installment\"",fullJson);
                    Assert.Contains("\"relatedRoute\":\"product-edit\"",fullJson);Assert.Contains("\"relatedRoute\":\"installment-payment\"",fullJson);
                    Assert.Equal(HttpStatusCode.OK,(await Put(admin,csrf,"api/users/"+cashierId+"/screen-permissions","{\"screenIds\":[\"reorder-alert\"]}")).StatusCode);
                    var legacyJson=await cashier.GetStringAsync("api/alerts");Assert.Contains("\"type\":\"reorder\"",legacyJson);Assert.DoesNotContain("\"type\":\"expiry\"",legacyJson);Assert.DoesNotContain("\"type\":\"installment\"",legacyJson);
                    long currentVersion;using(var c=new SQLiteConnection(cs)){c.Open();currentVersion=c.ExecuteScalar<long>("SELECT Version FROM ProductDetails WHERE ProductId=@parsedProductId",new{parsedProductId});}
                    var updated=await Put(admin,csrf,"api/catalog/products/"+productId,"{\"name\":\"صنف تنبيه\",\"baseUnitId\":1,\"salePrice\":\"1\",\"reorderPoint\":\"0\",\"isActive\":true,\"version\":"+currentVersion+"}");
                    Assert.Equal(HttpStatusCode.OK,updated.StatusCode);Assert.Contains("\"data\":[]",await cashier.GetStringAsync("api/alerts"));
                    Assert.Equal(HttpStatusCode.OK,(await Put(admin,csrf,"api/users/"+cashierId+"/screen-permissions","{\"screenIds\":[\"products-list\"]}")).StatusCode);
                    Assert.Equal(HttpStatusCode.Forbidden,(await cashier.GetAsync("api/alerts")).StatusCode);
                }
            }
            finally {if(server!=null)server.Dispose();SQLiteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        private static HttpClient Client(string url){return new HttpClient(new HttpClientHandler{UseCookies=true}){BaseAddress=new Uri(url)};}
        private static StringContent Json(string json){return new StringContent(json,Encoding.UTF8,"application/json");}
        private static async Task<string> Login(HttpClient client,string username){var response=await client.PostAsync("api/auth/login",Json("{\"username\":\""+username+"\",\"password\":\"secret\"}"));Assert.Equal(HttpStatusCode.OK,response.StatusCode);return Regex.Match(await response.Content.ReadAsStringAsync(),"\\\"csrfToken\\\":\\\"([^\\\"]+)\\\"").Groups[1].Value;}
        private static Task<HttpResponseMessage> Put(HttpClient client,string csrf,string path,string body){return Send(client,csrf,HttpMethod.Put,path,body);}
        private static Task<HttpResponseMessage> Post(HttpClient client,string csrf,string path,string body){return Send(client,csrf,HttpMethod.Post,path,body);}
        private static Task<HttpResponseMessage> Send(HttpClient client,string csrf,HttpMethod method,string path,string body){var request=new HttpRequestMessage(method,path){Content=Json(body)};request.Headers.Add("X-CSRF-Token",csrf);return client.SendAsync(request);}
        private static int FreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
    }
}
