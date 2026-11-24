using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Printing;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Workforce;
using AlKhayal.Application.Users;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Database;
using AlKhayal.Data.Printing;
using AlKhayal.Data.Workforce;
using AlKhayal.Server.Host;
using Dapper;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class TreasuryApiTests
    {
        [Fact]
        public void Delivery_migration_applies_once_on_fresh_and_reopened_database()
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                using(var c=f.Open())
                {
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion WHERE Name LIKE '%083_delivery_service.sql'"));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Drivers'"));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SalesInvoiceDeliveries'"));
                    Assert.Contains(c.Query<string>("SELECT name FROM pragma_table_info('Customers')"),x=>x=="SecondaryPhone");
                }
                Migrator.Run(f.ConnectionString);
                using(var c=f.Open())
                {
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion WHERE Name LIKE '%083_delivery_service.sql'"));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
            }
        }

        [Fact]
        public void Delivery_migration_upgrades_a_pre083_database_preserving_existing_rows()
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                using(var c=f.Open())
                {
                    c.Execute("INSERT INTO Customers(Code,Name,Phone,Address,CreatedAt,UpdatedAt) VALUES('PRE83','عميل قديم','010','قديم','2026-01-01','2026-01-01')");
                    c.Execute("DROP TRIGGER TR_SalesInvoiceDeliveries_NoDelete; DROP TABLE SalesInvoiceDeliveries; DROP TABLE Drivers; ALTER TABLE Customers DROP COLUMN SecondaryPhone; DELETE FROM SchemaVersion WHERE Name LIKE '%083_delivery_service.sql'");
                }
                Migrator.Run(f.ConnectionString);
                using(var c=f.Open())
                {
                    Assert.Equal("عميل قديم",c.ExecuteScalar<string>("SELECT Name FROM Customers WHERE Code='PRE83'"));
                    Assert.Contains(c.Query<string>("SELECT name FROM pragma_table_info('Customers')"),x=>x=="SecondaryPhone");
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion WHERE Name LIKE '%083_delivery_service.sql'"));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
            }
        }

        [Fact]
        public async Task Delivery_http_permissions_driver_crud_paging_filtering_and_status_are_persisted()
        {
            using(var f=new SalesTreasuryTests.Fixture())
            {
                await f.Prepare();
                var frontend=DeliveryFrontend(f.Db.DatabasePath);
                var password=Guid.NewGuid().ToString("N");var users=new SqliteUserStore(f.Db.ConnectionString);var hash=new PasswordHasher().Hash(password);
                await users.CreateAsync("delivery-admin","مدير",hash,UserRoles.Administrator,CancellationToken.None);
                var cashierId=await users.CreateAsync("delivery-cashier","كاشير",hash,UserRoles.Cashier,CancellationToken.None);
                var deniedId=await users.CreateAsync("delivery-denied","ممنوع",hash,UserRoles.Cashier,CancellationToken.None);
                using(var c=f.Db.Open())
                {
                    foreach(var id in new[]{cashierId,deniedId})c.Execute("INSERT INTO UserScreenPermissions(UserId,ScreenId,ChangedByUserId,GrantedUtc) VALUES(@Id,'pos',@Admin,@Now)",new{Id=id,Admin=f.Db.UserId,Now=DateTime.UtcNow.ToString("o")});
                    foreach(var screen in new[]{"drivers-management","delivery-orders"})c.Execute("INSERT INTO UserScreenPermissions(UserId,ScreenId,ChangedByUserId,GrantedUtc) VALUES(@Id,@Screen,@Admin,@Now)",new{Id=cashierId,Screen=screen,Admin=f.Db.UserId,Now=DateTime.UtcNow.ToString("o")});
                }
                var url=DeliveryUrl();using(var server=new AlKhayalServer(new TestLicenseGate()))
                {
                    server.Start(new HostOptions{DatabasePath=f.Db.DatabasePath,FrontendPath=frontend,Url=url.ToString().TrimEnd('/')});
                    using(var admin=Client(url))using(var cashier=Client(url))using(var denied=Client(url))using(var anon=Client(url))
                    {
                        Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync("api/delivery/drivers")).StatusCode);
                        Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync("api/delivery/orders")).StatusCode);
                        Assert.Equal(HttpStatusCode.Unauthorized,(await Send(anon,HttpMethod.Post,"api/delivery/drivers",new{name="x",phone="0123",isActive=true},null)).StatusCode);
                        Assert.Equal(HttpStatusCode.Unauthorized,(await Send(anon,HttpMethod.Post,"api/delivery/orders/1/status",new{status="delivered"},null)).StatusCode);
                        var adminCsrf=await Login(admin,"delivery-admin",password);var cashierCsrf=await Login(cashier,"delivery-cashier",password);var deniedCsrf=await Login(denied,"delivery-denied",password);
                        Assert.Equal(HttpStatusCode.Forbidden,(await denied.GetAsync("api/delivery/drivers")).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden,(await denied.GetAsync("api/delivery/orders")).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden,(await Send(denied,HttpMethod.Post,"api/delivery/drivers",new{name="ممنوع",phone="0123",isActive=true},deniedCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden,(await Send(denied,HttpMethod.Post,"api/delivery/orders/1/status",new{status="delivered"},deniedCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden,(await Send(cashier,HttpMethod.Post,"api/delivery/drivers",new{name="طيار",phone="0123",isActive=true},null)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden,(await Send(cashier,HttpMethod.Post,"api/delivery/orders/1/status",new{status="delivered"},null)).StatusCode);
                        var first=await Send(admin,HttpMethod.Post,"api/delivery/drivers",new{name="طيار ألف",phone="0111000",isActive=true},adminCsrf);Assert.Equal(HttpStatusCode.OK,first.StatusCode);
                        var firstId=(long)JObject.Parse(await first.Content.ReadAsStringAsync())["data"]["id"];
                        var second=await Send(cashier,HttpMethod.Post,"api/delivery/drivers",new{name="طيار باء",phone="0112000",isActive=true},cashierCsrf);Assert.Equal(HttpStatusCode.OK,second.StatusCode);
                        var secondId=(long)JObject.Parse(await second.Content.ReadAsStringAsync())["data"]["id"];
                        var found=(JArray)JObject.Parse(await cashier.GetStringAsync("api/delivery/drivers?search=0111000"))["data"];Assert.Single(found);Assert.Equal(firstId,(long)found[0]["id"]);
                        var edited=await Send(admin,HttpMethod.Post,"api/delivery/drivers",new{id=firstId,name="طيار ألف الجديد",phone="0111000",isActive=false},adminCsrf);Assert.Equal(HttpStatusCode.OK,edited.StatusCode);
                        var active=(JArray)JObject.Parse(await cashier.GetStringAsync("api/delivery/drivers?activeOnly=true"))["data"];Assert.Single(active);Assert.Equal(secondId,(long)active[0]["id"]);
                        Assert.Equal(HttpStatusCode.BadRequest,(await Send(admin,HttpMethod.Post,"api/delivery/drivers",new{id=999999,name="غير موجود",phone="0111000",isActive=true},adminCsrf)).StatusCode);
                        var empty=JObject.Parse(await cashier.GetStringAsync("api/delivery/orders?page=2&pageSize=1"))["data"];Assert.Equal(0,(long)empty["total"]);Assert.Empty((JArray)empty["items"]);
                        Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync("api/delivery/orders")).StatusCode);
                    }
                }
            }
        }

        [Fact]
        public async Task Delivery_http_cash_credit_snapshots_filters_print_correction_cancellation_and_restart()
        {
            using(var f=new SalesTreasuryTests.Fixture())
            {
                await f.Prepare(openShift:false);var frontend=DeliveryFrontend(f.Db.DatabasePath);var today=DateTime.Today.ToString("yyyy-MM-dd");
                var password=Guid.NewGuid().ToString("N");var users=new SqliteUserStore(f.Db.ConnectionString);
                await users.CreateAsync("delivery-sales-admin","مدير",new PasswordHasher().Hash(password),UserRoles.Administrator,CancellationToken.None);
                long customer;using(var c=f.Db.Open())customer=c.ExecuteScalar<long>("INSERT INTO Customers(Code,Name,Phone,SecondaryPhone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('DEL-C','عميل التوصيل','010111','010222','عنوان العميل',0,1000,'أساسي','نشط',@Today,@Today); SELECT last_insert_rowid();",new{Today=today});
                var url=DeliveryUrl();var options=new HostOptions{DatabasePath=f.Db.DatabasePath,FrontendPath=frontend,Url=url.ToString().TrimEnd('/')};long cashId,creditId,driverId,replacementId;
                using(var server=new AlKhayalServer(new TestLicenseGate()))
                {
                    server.Start(options);using(var client=Client(url))
                    {
                        var csrf=await Login(client,"delivery-sales-admin",password);
                        var driver=await Send(client,HttpMethod.Post,"api/delivery/drivers",new{name="طيار التاريخ",phone="01123456",isActive=true},csrf);Assert.Equal(HttpStatusCode.OK,driver.StatusCode);driverId=(long)JObject.Parse(await driver.Content.ReadAsStringAsync())["data"]["id"];
                        var line=new{variantId=f.Variant,productUnitId=f.Unit,quantity="1",unitPrice="12.34",discount="0.00"};
                        object Draft(string mode,string fee,long? selectedDriver= null)=>new{customerId=customer,warehouseId=1,invoiceDate=today,paymentMode=mode,discount="0.00",delivery=new{driverId=selectedDriver??driverId,primaryPhone="010333",secondaryPhone="010444",address="عنوان الطلب",fee},lines=new[]{line}};
                        Assert.Equal(HttpStatusCode.BadRequest,(await Send(client,HttpMethod.Post,"api/sales/drafts",Draft("cash","-1"),csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.BadRequest,(await Send(client,HttpMethod.Post,"api/sales/drafts",Draft("cash","2.50",999999),csrf)).StatusCode);
                        using(var c=f.Db.Open())Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesInvoices"));
                        var cash=await Send(client,HttpMethod.Post,"api/sales/drafts",Draft("cash","2.50"),csrf);Assert.Equal(HttpStatusCode.Created,cash.StatusCode);cashId=(long)JObject.Parse(await cash.Content.ReadAsStringAsync())["data"]["id"];
                        Assert.Equal(HttpStatusCode.BadRequest,(await Send(client,HttpMethod.Post,"api/delivery/orders/"+cashId+"/status",new{status="delivered"},csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK,(await Send(client,HttpMethod.Post,"api/workforce/shifts/open",new{amount="0",operationKey="delivery-open-shift"},csrf)).StatusCode);
                        var posted=await Send(client,HttpMethod.Post,"api/sales/"+cashId+"/post",new{operationKey="delivery-cash-post"},csrf);Assert.Equal(HttpStatusCode.OK,posted.StatusCode);
                        var cashData=JObject.Parse(await posted.Content.ReadAsStringAsync())["data"];Assert.Equal(148400,(long)cashData["totalScaled"]);Assert.Equal(148400,(long)cashData["paidScaled"]);Assert.Equal(25000,(long)cashData["delivery"]["feeScaled"]);
                        Assert.Equal(HttpStatusCode.OK,(await Send(client,HttpMethod.Post,"api/sales/"+cashId+"/post",new{operationKey="delivery-cash-post"},csrf)).StatusCode);
                        var credit=await Send(client,HttpMethod.Post,"api/sales/drafts",Draft("credit","3.00"),csrf);Assert.Equal(HttpStatusCode.Created,credit.StatusCode);creditId=(long)JObject.Parse(await credit.Content.ReadAsStringAsync())["data"]["id"];
                        var creditPost=await Send(client,HttpMethod.Post,"api/sales/"+creditId+"/post",new{operationKey="delivery-credit-post"},csrf);Assert.Equal(HttpStatusCode.OK,creditPost.StatusCode);Assert.Equal(153400,(long)JObject.Parse(await creditPost.Content.ReadAsStringAsync())["data"]["totalScaled"]);
                        var page=JObject.Parse(await client.GetStringAsync("api/delivery/orders?page=1&pageSize=1"))["data"];Assert.Equal(2,(long)page["total"]);Assert.Single((JArray)page["items"]);
                        Assert.Equal(creditId,(long)page["items"][0]["salesInvoiceId"]);
                        var secondPage=JObject.Parse(await client.GetStringAsync("api/delivery/orders?page=2&pageSize=1"))["data"];Assert.Equal(cashId,(long)secondPage["items"][0]["salesInvoiceId"]);
                        Assert.Equal(2,(long)JObject.Parse(await client.GetStringAsync("api/delivery/orders?driverId="+driverId))["data"]["total"]);
                        Assert.Equal(0,(long)JObject.Parse(await client.GetStringAsync("api/delivery/orders?driverId=999999"))["data"]["total"]);
                        Assert.Equal(1,(long)JObject.Parse(await client.GetStringAsync("api/delivery/orders?search="+cashData["invoiceNumber"]))["data"]["total"]);
                        Assert.Equal(HttpStatusCode.OK,(await Send(client,HttpMethod.Post,"api/delivery/orders/"+cashId+"/status",new{status="out_for_delivery"},csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK,(await Send(client,HttpMethod.Post,"api/delivery/orders/"+cashId+"/status",new{status="out_for_delivery"},csrf)).StatusCode);
                        Assert.Equal(1,(long)JObject.Parse(await client.GetStringAsync("api/delivery/orders?status=out_for_delivery"))["data"]["total"]);
                        Assert.Equal(HttpStatusCode.BadRequest,(await Send(client,HttpMethod.Post,"api/delivery/orders/"+cashId+"/status",new{status="invented"},csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK,(await Send(client,HttpMethod.Post,"api/delivery/drivers",new{id=driverId,name="اسم جديد",phone="01199999",isActive=false},csrf)).StatusCode);
                        using(var c=f.Db.Open())c.Execute("UPDATE Customers SET Name='اسم العميل الجديد',Phone='010999',SecondaryPhone='010888',Address='عنوان جديد' WHERE Id=@Id",new{Id=customer});
                        var snapshot=JObject.Parse(await client.GetStringAsync("api/sales/"+cashId))["data"]["delivery"];
                        Assert.Equal("طيار التاريخ",(string)snapshot["driverName"]);Assert.Equal("عميل التوصيل",(string)snapshot["customerName"]);Assert.Equal("010333",(string)snapshot["primaryPhone"]);Assert.Equal("010444",(string)snapshot["secondaryPhone"]);Assert.Equal("عنوان الطلب",(string)snapshot["address"]);
                        var printer=new PrintingService(new SqlitePrintRepository(f.Db.ConnectionString),PrintingPersistenceTests.Provider(f.Db),new PrintingPersistenceTests.RecordingBackend());
                        var settings=await printer.SettingsAsync(CancellationToken.None);foreach(var profile in settings.Profiles)profile.PrinterName="Recording";await printer.SaveAsync(settings,f.Db.UserId,CancellationToken.None);
                        var backend=new PrintingPersistenceTests.RecordingBackend();printer=new PrintingService(new SqlitePrintRepository(f.Db.ConnectionString),PrintingPersistenceTests.Provider(f.Db),backend);
                        var print=await printer.SubmitAsync(new PrintRequest{DocumentType="delivery",DocumentId=cashId,OperationKey="delivery-print",Reprint=true},f.Db.UserId,true,CancellationToken.None);
                        Assert.Equal("submitted_unknown",print.Status);Assert.Equal(1,backend.Submissions);Assert.Equal("بون توصيل",backend.Document.Title);Assert.Equal("0.00",backend.Document.Fields["collectAmount"]);Assert.False(backend.Document.Fields.ContainsKey("grandTotal"));Assert.Null(backend.Document.Items[0].UnitPrice);Assert.Equal("2.50",backend.Document.Fields["deliveryFee"]);Assert.Equal("مدفوع بالكامل",backend.Document.Fields["paymentStatus"]);Assert.Contains("010444",backend.Document.Fields["header"]);Assert.Contains("طيار التاريخ",backend.Document.Fields["header"]);Assert.Single(backend.Document.Items);
                        Assert.True((await printer.SubmitAsync(new PrintRequest{DocumentType="delivery",DocumentId=cashId,OperationKey="delivery-print",Reprint=true},f.Db.UserId,true,CancellationToken.None)).Replayed);Assert.Equal(1,backend.Submissions);
                        settings=await printer.SettingsAsync(CancellationToken.None);settings.AutoPrintSale=true;settings.AutoPrintDelivery=true;await printer.SaveAsync(settings,f.Db.UserId,CancellationToken.None);
                        var saleAuto=await printer.SubmitAsync(new PrintRequest{DocumentType="sale",DocumentId=cashId,OperationKey="auto-sale",Automatic=true},f.Db.UserId,true,CancellationToken.None);
                        Assert.Equal("submitted_unknown",saleAuto.Status);Assert.Equal("14.84",backend.Document.Fields["grandTotal"]);
                        var deliveryAuto=await printer.SubmitAsync(new PrintRequest{DocumentType="delivery",DocumentId=cashId,OperationKey="auto-delivery",Automatic=true},f.Db.UserId,true,CancellationToken.None);
                        Assert.Equal("submitted_unknown",deliveryAuto.Status);Assert.Equal("بون توصيل",backend.Document.Title);Assert.Equal(3,backend.Submissions);
                        Assert.True((await printer.SubmitAsync(new PrintRequest{DocumentType="delivery",DocumentId=cashId,OperationKey="auto-delivery",Automatic=true},f.Db.UserId,true,CancellationToken.None)).Replayed);Assert.Equal(3,backend.Submissions);
                        settings=await printer.SettingsAsync(CancellationToken.None);Assert.True(settings.AutoPrintDelivery);settings.AutoPrintDelivery=false;await printer.SaveAsync(settings,f.Db.UserId,CancellationToken.None);
                        Assert.False((await printer.SettingsAsync(CancellationToken.None)).AutoPrintDelivery);
                        Assert.Equal("disabled",(await printer.SubmitAsync(new PrintRequest{DocumentType="delivery",DocumentId=creditId,OperationKey="auto-delivery-disabled",Automatic=true},f.Db.UserId,true,CancellationToken.None)).Status);
                        Assert.Equal(3,backend.Submissions);
                        var original=await client.GetStringAsync("api/sales/"+cashId);var version=(long)JObject.Parse(original)["data"]["version"];
                        var correction=new{expectedVersion=version,operationKey="delivery-correction",settlementKind="refund",settlementMethod="cash",treasuryAccountId=(long)cashData["treasuryAccountId"],invoice=new{expectedVersion=version,customerId=customer,warehouseId=1,invoiceDate=today,paymentMode="cash",discount="0.00",delivery=new{driverId,primaryPhone="010333",secondaryPhone="010444",address="عنوان الطلب",fee="1.50"},lines=new[]{line}}};
                        // An inactive historical driver cannot be selected for a new revision.
                        Assert.Equal(HttpStatusCode.BadRequest,(await Send(client,HttpMethod.Post,"api/sales/"+cashId+"/corrections",correction,csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK,(await Send(client,HttpMethod.Post,"api/delivery/drivers",new{id=driverId,name="اسم جديد",phone="01199999",isActive=true},csrf)).StatusCode);
                        var corrected=await Send(client,HttpMethod.Post,"api/sales/"+cashId+"/corrections",correction,csrf);Assert.Equal(HttpStatusCode.OK,corrected.StatusCode);
                        var result=JObject.Parse(await corrected.Content.ReadAsStringAsync())["data"];replacementId=(long)result["invoice"]["id"];
                        Assert.Equal(138400,(long)result["invoice"]["totalScaled"]);Assert.Equal("طيار التاريخ",(string)snapshot["driverName"]);
                        Assert.Equal(HttpStatusCode.OK,(await Send(client,HttpMethod.Post,"api/sales/"+cashId+"/corrections",correction,csrf)).StatusCode);
                        var canceled=await Send(client,HttpMethod.Post,"api/sales/"+creditId+"/cancel",new{operationKey="delivery-credit-cancel",reason="إلغاء طلب التوصيل"},csrf);Assert.Equal(HttpStatusCode.OK,canceled.StatusCode);
                        Assert.True((bool)JObject.Parse(await canceled.Content.ReadAsStringAsync())["data"]["succeeded"]);
                        Assert.Equal(HttpStatusCode.Conflict,(await Send(client,HttpMethod.Post,"api/sales/"+cashId+"/cancel",new{operationKey="delivery-cash-cancel",reason="رد نقدي"},csrf)).StatusCode);
                        Assert.Equal(1,(long)JObject.Parse(await client.GetStringAsync("api/delivery/orders"))["data"]["total"]);
                        Assert.Equal(HttpStatusCode.BadRequest,(await Send(client,HttpMethod.Post,"api/delivery/orders/"+cashId+"/status",new{status="delivered"},csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.BadRequest,(await Send(client,HttpMethod.Post,"api/delivery/orders/"+creditId+"/status",new{status="delivered"},csrf)).StatusCode);
                        using(var c=f.Db.Open())
                        {
                            Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments WHERE SalesInvoiceId=@Id AND EventType='cash_sale'",new{Id=cashId}));
                            Assert.Equal(148400,c.ExecuteScalar<long>("SELECT BalanceAfterScaled FROM TreasuryMovements WHERE Id=(SELECT TreasuryMovementId FROM SalesTreasuryReceipts WHERE SalesInvoiceId=@Id)",new{Id=cashId}));
                            Assert.Equal(138400,c.ExecuteScalar<long>("SELECT BalanceAfterScaled FROM TreasuryMovements WHERE AccountId=@Id ORDER BY Id DESC LIMIT 1",new{Id=(long)cashData["treasuryAccountId"]}));
                            Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=customer}));
                            Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesInvoiceCorrections WHERE PreviousInvoiceId=@Id",new{Id=cashId}));
                            Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesCancellations WHERE SalesInvoiceId=@Id",new{Id=creditId}));
                            Assert.Equal(19000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE VariantId=@Variant",new{Variant=f.Variant}));
                        }
                    }
                }
                using(var server=new AlKhayalServer(new TestLicenseGate()))
                {
                    server.Start(options);using(var client=Client(url))
                    {
                        await Login(client,"delivery-sales-admin",password);
                        Assert.Equal("عنوان الطلب",(string)JObject.Parse(await client.GetStringAsync("api/sales/"+cashId))["data"]["delivery"]["address"]);
                        Assert.Equal(138400,(long)JObject.Parse(await client.GetStringAsync("api/sales/"+replacementId))["data"]["totalScaled"]);
                        Assert.Equal(0,(long)JObject.Parse(await client.GetStringAsync("api/delivery/orders?status=out_for_delivery"))["data"]["total"]);
                    }
                }
            }
        }

        [Fact]
        public async Task Delivery_without_rider_posts_and_preserves_null_assignment()
        {
            using(var f=new SalesTreasuryTests.Fixture())
            {
                await f.Prepare();
                long customer;
                using(var c=f.Db.Open())customer=c.ExecuteScalar<long>("INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('DEL-NO-RIDER','عميل بلا طيار','010111','عنوان العميل',0,1000,'أساسي','نشط',@Today,@Today); SELECT last_insert_rowid();",new{Today=DateTime.Today.ToString("yyyy-MM-dd")});
                var draft=await f.Sales.CreateDraftAsync(new SaleDraftCommand{CustomerId=customer,WarehouseId=1,InvoiceDate=DateTime.Today.ToString("yyyy-MM-dd"),PaymentMode="credit",Delivery=new DeliveryDetails{DriverId=null,PrimaryPhone="010555",Address="عنوان الطلب",Fee="2.00"},Lines=new(){new SaleLine{VariantId=f.Variant,ProductUnitId=f.Unit,Quantity="1",UnitPrice="12.34"}}},f.Db.UserId,CancellationToken.None);
                Assert.NotNull(draft);Assert.Null(draft.Delivery.DriverId);
                var posted=await f.Post(draft.Id,"delivery-no-rider-post");Assert.True(posted.Succeeded,posted.ErrorMessage);
                Assert.Equal(143400,posted.Invoice.TotalScaled);Assert.Null(posted.Invoice.Delivery.DriverId);
                using(var c=f.Db.Open())
                {
                    Assert.Null(c.ExecuteScalar<long?>("SELECT DriverId FROM SalesInvoiceDeliveries WHERE SalesInvoiceId=@Id",new{Id=draft.Id}));
                    Assert.Equal("",c.ExecuteScalar<string>("SELECT DriverName FROM SalesInvoiceDeliveries WHERE SalesInvoiceId=@Id",new{Id=draft.Id}));
                    Assert.Equal(19000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE VariantId=@Variant",new{Variant=f.Variant}));
                }
            }
        }

        [Fact]
        public async Task Delivery_http_no_rider_cash_and_credit_drafts_post_and_print_later_assignment()
        {
            using(var f=new SalesTreasuryTests.Fixture())
            {
                await f.Prepare(openShift:false);
                var frontend=DeliveryFrontend(f.Db.DatabasePath);var today=DateTime.Today.ToString("yyyy-MM-dd");
                var password=Guid.NewGuid().ToString("N");var users=new SqliteUserStore(f.Db.ConnectionString);
                await users.CreateAsync("delivery-no-rider-admin","مدير",new PasswordHasher().Hash(password),UserRoles.Administrator,CancellationToken.None);
                long customer;using(var c=f.Db.Open())customer=c.ExecuteScalar<long>("INSERT INTO Customers(Code,Name,Phone,SecondaryPhone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('DEL-NR-HTTP','عميل HTTP بلا طيار','010111','010222','عنوان العميل',0,1000,'أساسي','نشط',@Today,@Today); SELECT last_insert_rowid();",new{Today=today});
                var url=DeliveryUrl();var options=new HostOptions{DatabasePath=f.Db.DatabasePath,FrontendPath=frontend,Url=url.ToString().TrimEnd('/')};
                using(var server=new AlKhayalServer(new TestLicenseGate()))
                {
                    server.Start(options);using(var client=Client(url))
                    {
                        var csrf=await Login(client,"delivery-no-rider-admin",password);
                        Assert.Equal(HttpStatusCode.OK,(await Send(client,HttpMethod.Post,"api/workforce/shifts/open",new{amount="0",operationKey="delivery-no-rider-shift"},csrf)).StatusCode);
                        var line=new{variantId=f.Variant,productUnitId=f.Unit,quantity="1",unitPrice="12.34",discount="0.00"};
                        object Draft(string mode)=>new{customerId=customer,warehouseId=1,invoiceDate=today,paymentMode=mode,discount="0.00",delivery=new{driverId=(long?)null,primaryPhone="010555",secondaryPhone="010666",address="عنوان بلا طيار",fee="2.00"},lines=new[]{line}};
                        var cash=await Send(client,HttpMethod.Post,"api/sales/drafts",Draft("cash"),csrf);Assert.Equal(HttpStatusCode.Created,cash.StatusCode);
                        var cashId=(long)JObject.Parse(await cash.Content.ReadAsStringAsync())["data"]["id"];
                        var cashPost=await Send(client,HttpMethod.Post,"api/sales/"+cashId+"/post",new{operationKey="delivery-no-rider-cash-post"},csrf);Assert.Equal(HttpStatusCode.OK,cashPost.StatusCode);
                        var cashData=JObject.Parse(await cashPost.Content.ReadAsStringAsync())["data"];Assert.Equal(143400,(long)cashData["totalScaled"]);Assert.Null(cashData["delivery"]["driverId"]);
                        var credit=await Send(client,HttpMethod.Post,"api/sales/drafts",Draft("credit"),csrf);Assert.Equal(HttpStatusCode.Created,credit.StatusCode);
                        var creditId=(long)JObject.Parse(await credit.Content.ReadAsStringAsync())["data"]["id"];
                        var creditPost=await Send(client,HttpMethod.Post,"api/sales/"+creditId+"/post",new{operationKey="delivery-no-rider-credit-post"},csrf);Assert.Equal(HttpStatusCode.OK,creditPost.StatusCode);
                        var creditData=JObject.Parse(await creditPost.Content.ReadAsStringAsync())["data"];Assert.Equal(143400,(long)creditData["totalScaled"]);Assert.Null(creditData["delivery"]["driverId"]);
                        using(var c=f.Db.Open())
                        {
                            Assert.Null(c.ExecuteScalar<long?>("SELECT DriverId FROM SalesInvoiceDeliveries WHERE SalesInvoiceId=@Id",new{Id=cashId}));
                            Assert.Equal("",c.ExecuteScalar<string>("SELECT DriverName FROM SalesInvoiceDeliveries WHERE SalesInvoiceId=@Id",new{Id=cashId}));
                            Assert.Null(c.ExecuteScalar<long?>("SELECT DriverId FROM SalesInvoiceDeliveries WHERE SalesInvoiceId=@Id",new{Id=creditId}));
                            Assert.Equal("",c.ExecuteScalar<string>("SELECT DriverName FROM SalesInvoiceDeliveries WHERE SalesInvoiceId=@Id",new{Id=creditId}));
                        }
                        var printer=new PrintingService(new SqlitePrintRepository(f.Db.ConnectionString),PrintingPersistenceTests.Provider(f.Db),new PrintingPersistenceTests.RecordingBackend());
                        var settings=await printer.SettingsAsync(CancellationToken.None);foreach(var profile in settings.Profiles)profile.PrinterName="Recording";await printer.SaveAsync(settings,f.Db.UserId,CancellationToken.None);
                        var backend=new PrintingPersistenceTests.RecordingBackend();printer=new PrintingService(new SqlitePrintRepository(f.Db.ConnectionString),PrintingPersistenceTests.Provider(f.Db),backend);
                        var print=await printer.SubmitAsync(new PrintRequest{DocumentType="delivery",DocumentId=cashId,OperationKey="delivery-no-rider-print",Reprint=true},f.Db.UserId,true,CancellationToken.None);
                        Assert.Equal("submitted_unknown",print.Status);Assert.Equal(1,backend.Submissions);
                        Assert.Contains("تعيين لاحقًا",backend.Document.Fields["header"]);
                        Assert.DoesNotContain("null",backend.Document.Fields["header"]);
                        Assert.Equal("0.00",backend.Document.Fields["collectAmount"]);
                        await printer.SubmitAsync(new PrintRequest{DocumentType="delivery",DocumentId=creditId,OperationKey="delivery-credit-print",Reprint=true},f.Db.UserId,true,CancellationToken.None);
                        Assert.Equal("14.34",backend.Document.Fields["collectAmount"]);
                        var orders=JObject.Parse(await client.GetStringAsync("api/delivery/orders"))["data"];Assert.Equal(2,(long)orders["total"]);
                        Assert.All((JArray)orders["items"],o=>Assert.Equal("",(string)o["driverName"]));
                    }
                }
            }
        }

        [Fact]
        public async Task Delivery_late_post_failure_rolls_back_fee_inventory_cash_and_ledger_then_same_key_retries()
        {
            using(var f=new SalesTreasuryTests.Fixture())
            {
                await f.Prepare(openShift:false);var customer=0L;var driver=0L;
                using(var c=f.Db.Open())
                {
                    customer=c.ExecuteScalar<long>("INSERT INTO Customers(Code,Name,Phone,SecondaryPhone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('ROLL-DEL','عميل','01055','01066','عنوان',0,1000,'أساسي','نشط','2026-01-01','2026-01-01'); SELECT last_insert_rowid()");
                    driver=c.ExecuteScalar<long>("INSERT INTO Drivers(Name,Phone,IsActive,CreatedUtc,UpdatedUtc) VALUES('طيار','01177',1,'2026-01-01','2026-01-01'); SELECT last_insert_rowid()");
                }
                var workforce=new WorkforceService(new SqliteWorkforceStore(f.Db.ConnectionString));await workforce.ExecuteAsync("shift-open",new WorkforceCommand{Amount="0",OperationKey="delivery-rollback-shift"},f.Db.UserId,true,CancellationToken.None);
                var draft=await f.Sales.CreateDraftAsync(new SaleDraftCommand{CustomerId=customer,WarehouseId=1,InvoiceDate=DateTime.Today.ToString("yyyy-MM-dd"),PaymentMode="cash",Delivery=new DeliveryDetails{DriverId=driver,PrimaryPhone="01055",Address="عنوان",Fee="2.00"},Lines=new(){new SaleLine{VariantId=f.Variant,ProductUnitId=f.Unit,Quantity="1",UnitPrice="12.34"}}},f.Db.UserId,CancellationToken.None);
                Assert.Equal(143400,draft.TotalScaled);
                using(var c=f.Db.Open())c.Execute("CREATE TRIGGER FailDeliveryPost BEFORE INSERT ON SalesInvoiceAudit WHEN NEW.EventType='posted' BEGIN SELECT RAISE(ABORT,'delivery rollback test'); END;");
                var failed=await f.Sales.PostAsync(draft.Id,"delivery-rollback-key",f.Db.UserId,CancellationToken.None);Assert.False(failed.Succeeded);
                using(var c=f.Db.Open())
                {
                    Assert.Equal("draft",c.ExecuteScalar<string>("SELECT Status FROM SalesInvoices WHERE Id=@Id",new{Id=draft.Id}));
                    Assert.Equal(143400,c.ExecuteScalar<long>("SELECT TotalScaled FROM SalesInvoices WHERE Id=@Id",new{Id=draft.Id}));
                    Assert.Equal(20000,c.ExecuteScalar<long>("SELECT FeeScaled FROM SalesInvoiceDeliveries WHERE SalesInvoiceId=@Id",new{Id=draft.Id}));
                    Assert.Equal(20000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE VariantId=@Variant",new{Variant=f.Variant}));
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE SalesInvoiceId=@Id",new{Id=draft.Id}));
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));c.Execute("DROP TRIGGER FailDeliveryPost");
                }
                var posted=await f.Sales.PostAsync(draft.Id,"delivery-rollback-key",f.Db.UserId,CancellationToken.None);Assert.True(posted.Succeeded,posted.ErrorMessage);Assert.Equal(143400,posted.Invoice.TotalScaled);
                Assert.True((await f.Sales.PostAsync(draft.Id,"delivery-rollback-key",f.Db.UserId,CancellationToken.None)).Succeeded);
                using(var c=f.Db.Open())
                {
                    Assert.Equal(19000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE VariantId=@Variant",new{Variant=f.Variant}));
                    Assert.Equal(90000,c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM InventoryCostAllocations"));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments WHERE SalesInvoiceId=@Id",new{Id=draft.Id}));
                    Assert.Equal(143400,c.ExecuteScalar<long>("SELECT BalanceAfterScaled FROM TreasuryMovements ORDER BY Id DESC LIMIT 1"));
                }
            }
        }

        [Fact]
        public async Task Delivery_twenty_posted_orders_can_be_corrected_and_reopened_by_persisted_identity()
        {
            using (var f = new SalesTreasuryTests.Fixture())
            {
                await f.Prepare(openShift: false);
                var password = Guid.NewGuid().ToString("N");
                await new SqliteUserStore(f.Db.ConnectionString).CreateAsync(
                    "delivery-repeat", "مدير الاختبار", new PasswordHasher().Hash(password),
                    UserRoles.Administrator, CancellationToken.None);
                var today = DateTime.Today.ToString("yyyy-MM-dd");
                long customer;
                using (var connection = f.Db.Open())
                    customer = connection.ExecuteScalar<long>(@"INSERT INTO Customers
                        (Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt)
                        VALUES('REPEAT-DEL','عميل تجريبي','0101234','عنوان',0,1000,'أساسي','نشط',@Today,@Today);
                        SELECT last_insert_rowid();", new { Today = today });
                var url = DeliveryUrl();
                var options = new HostOptions
                {
                    DatabasePath = f.Db.DatabasePath,
                    FrontendPath = DeliveryFrontend(f.Db.DatabasePath),
                    Url = url.ToString().TrimEnd('/')
                };
                var correctedIds = new long[20];
                using (var server = new AlKhayalServer(new TestLicenseGate()))
                using (var client = Client(url))
                {
                    server.Start(options);
                    var csrf = await Login(client, "delivery-repeat", password);
                    async Task<JToken> Write(HttpMethod method, string route, object body, HttpStatusCode expected = HttpStatusCode.OK)
                    {
                        using (var response = await Send(client, method, route, body, csrf))
                        {
                            var json = await response.Content.ReadAsStringAsync();
                            Assert.True(expected == response.StatusCode, route + ": " + json);
                            return JObject.Parse(json)["data"];
                        }
                    }
                    await Write(HttpMethod.Post, "api/workforce/shifts/open", new { amount = "0", operationKey = "repeat-shift" });
                    var driver = await Write(HttpMethod.Post, "api/delivery/drivers", new {name = "طيار تجريبي", phone = "0112345", isActive = true});
                    for (var i = 0; i < correctedIds.Length; i++)
                    {
                        var driverId = i % 2 == 0 ? (long?)driver["id"] : null;
                        object Sale(string address, long? expectedVersion = null) => new
                        {
                            customerId = customer, warehouseId = 1, invoiceDate = today, paymentMode = "cash",
                            expectedVersion, discount = "0.00",
                            delivery = new {driverId, primaryPhone = "0101234", address, fee = "2.50"},
                            lines = new[] {new {variantId = f.Variant, productUnitId = f.Unit, quantity = "1", unitPrice = "12.34", discount = "0.00"}}
                        };
                        var initial = await Write(HttpMethod.Post, "api/sales/drafts", Sale("عنوان أول " + i), HttpStatusCode.Created);
                        var id = (long)initial["id"];
                        var posted = await Write(HttpMethod.Post, "api/sales/" + id + "/post", new {operationKey = "repeat-post-" + i});
                        var reopened = JObject.Parse(await client.GetStringAsync("api/sales/" + id))["data"];
                        Assert.Equal(id, (long)reopened["id"]);
                        Assert.True((bool)reopened["canEditPosted"]);
                        var version = (long)reopened["version"];
                        var correction = await Write(HttpMethod.Post, "api/sales/" + id + "/corrections", new
                        {
                            expectedVersion = version, operationKey = "repeat-correction-" + i,
                            settlementKind = "none", invoice = Sale("عنوان معدل " + i, version)
                        });
                        correctedIds[i] = (long)correction["invoice"]["id"];
                        Assert.NotEqual(id, correctedIds[i]);
                        var current = JObject.Parse(await client.GetStringAsync("api/sales/" + correctedIds[i]))["data"];
                        Assert.Equal("عنوان معدل " + i, (string)current["delivery"]["address"]);
                        Assert.Equal(driverId, (long?)current["delivery"]["driverId"]);
                        Assert.Equal((long)posted["totalScaled"], (long)current["totalScaled"]);
                        var status = new[] {"pending", "out_for_delivery", "delivered", "failed"}[i % 4];
                        await Write(HttpMethod.Post, "api/delivery/orders/" + correctedIds[i] + "/status", new {status});
                        var orders = JObject.Parse(await client.GetStringAsync("api/delivery/orders?search=" + current["invoiceNumber"] + "&status=" + status))["data"];
                        Assert.Equal(correctedIds[i], (long)Assert.Single((JArray)orders["items"])["salesInvoiceId"]);
                    }
                }
                Assert.Equal(20, correctedIds.Distinct().Count());
                using (var server = new AlKhayalServer(new TestLicenseGate()))
                using (var client = Client(url))
                {
                    server.Start(options);
                    await Login(client, "delivery-repeat", password);
                    for (var i = 0; i < correctedIds.Length; i++)
                    {
                        var reopened = JObject.Parse(await client.GetStringAsync("api/sales/" + correctedIds[i]))["data"];
                        Assert.Equal("عنوان معدل " + i, (string)reopened["delivery"]["address"]);
                    }
                }
            }
        }

        private static string DeliveryFrontend(string databasePath)
        {
            var frontend=Path.Combine(Path.GetDirectoryName(databasePath),"delivery-frontend");Directory.CreateDirectory(Path.Combine(frontend,"data"));
            File.WriteAllText(Path.Combine(frontend,"data","screens.json"),"{\"screens\":[{\"route\":\"pos\",\"title\":\"الكاشير\",\"module\":\"المبيعات\"},{\"route\":\"drivers-management\",\"title\":\"إدارة الطيارين\",\"module\":\"المبيعات\"},{\"route\":\"delivery-orders\",\"title\":\"طلبات التوصيل\",\"module\":\"المبيعات\"}]}");
            return frontend;
        }
        private static Uri DeliveryUrl(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return new Uri("http://127.0.0.1:"+port);}
    }
}
