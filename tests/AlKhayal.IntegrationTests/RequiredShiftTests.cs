using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Users;
using AlKhayal.Application.Workforce;
using AlKhayal.Data.Authentication;
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
        public async Task Required_shift_authenticated_cashier_post_ownership_restart_close_details_and_flag_off()
        {
            using(var f=new SalesTreasuryTests.Fixture())
            {
                await f.Prepare(openShift:false);
                var frontend=Path.Combine(Path.GetDirectoryName(f.Db.DatabasePath),"frontend");Directory.CreateDirectory(Path.Combine(frontend,"data"));
                File.WriteAllText(Path.Combine(frontend,"data","screens.json"),"{\"screens\":[{\"route\":\"pos\",\"title\":\"POS\",\"module\":\"sales\"}]}");
                var password=Guid.NewGuid().ToString("N");var users=new SqliteUserStore(f.Db.ConnectionString);var hash=new PasswordHasher().Hash(password);
                var cashierId=await users.CreateAsync("required-cashier","Cashier",hash,UserRoles.Cashier,CancellationToken.None);
                var otherId=await users.CreateAsync("required-other","Other",hash,UserRoles.Cashier,CancellationToken.None);
                using(var c=f.Db.Open())
                {
                    c.Execute("INSERT INTO UserScreenPermissions(UserId,ScreenId,ChangedByUserId,GrantedUtc) VALUES(@user,'pos',@admin,@now)",new{user=cashierId,admin=f.Db.UserId,now=DateTime.UtcNow.ToString("o")});
                    c.Execute("INSERT INTO UserScreenPermissions(UserId,ScreenId,ChangedByUserId,GrantedUtc) VALUES(@user,'pos',@admin,@now)",new{user=otherId,admin=f.Db.UserId,now=DateTime.UtcNow.ToString("o")});
                }
                var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
                var url=new Uri("http://127.0.0.1:"+port);var options=new HostOptions{DatabasePath=f.Db.DatabasePath,FrontendPath=frontend,Url=url.ToString().TrimEnd('/')};
                long shiftId, invoiceId;
                using(var server=new AlKhayalServer(new TestLicenseGate()))
                {
                    server.Start(options);
                    using(var cashier=Client(url))using(var other=Client(url))
                    {
                        var csrf=await Login(cashier,"required-cashier",password);var otherCsrf=await Login(other,"required-other",password);
                        var draft=await f.Sales.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-11-01",PaymentMode="cash",Lines=new(){new SaleLine{VariantId=f.Variant,ProductUnitId=f.Unit,Quantity="1",UnitPrice="12.34"}}},cashierId,CancellationToken.None);invoiceId=draft.Id;
                        var route="api/sales/"+invoiceId+"/post";var post=new{operationKey="required-post"};
                        var rejected=await Send(cashier,HttpMethod.Post,route,post,csrf);Assert.Equal(HttpStatusCode.Conflict,rejected.StatusCode);Assert.Equal("open_shift_required",(string)JObject.Parse(await rejected.Content.ReadAsStringAsync())["error"]["code"]);
                        var opened=await Send(cashier,HttpMethod.Post,"api/workforce/shifts/open",new{amount="20",operationKey="required-open"},csrf);Assert.Equal(HttpStatusCode.OK,opened.StatusCode);
                        shiftId=(long)JObject.Parse(await opened.Content.ReadAsStringAsync())["data"]["id"];
                        Assert.Equal(HttpStatusCode.Conflict,(await Send(other,HttpMethod.Post,route,new{operationKey="other-post"},otherCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.NotFound,(await Send(other,HttpMethod.Post,"api/workforce/shifts/close",new{id=shiftId,amount="20",operationKey="other-close"},otherCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK,(await Send(cashier,HttpMethod.Post,route,post,csrf)).StatusCode);
                        var activity=JObject.Parse(await cashier.GetStringAsync("api/workforce/shifts/"+shiftId+"/activity"))["data"];Assert.True((long)activity["total"]>0);Assert.Contains((JArray)activity["items"],x=>(string)x["kind"]=="invoice");Assert.Contains((JArray)activity["items"],x=>(string)x["kind"]=="operation");
                        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync("api/workforce/shifts/"+shiftId+"/activity")).StatusCode);
                        using(var c=f.Db.Open())Assert.Equal(shiftId,c.ExecuteScalar<long>("SELECT ShiftId FROM SalesInvoiceShiftOrigins WHERE SalesInvoiceId=@invoiceId",new{invoiceId}));
                    }
                }
                using(var server=new AlKhayalServer(new TestLicenseGate()))
                {
                    server.Start(options);
                    using(var cashier=Client(url))
                    {
                        var csrf=await Login(cashier,"required-cashier",password);
                        Assert.Equal(shiftId,(long)JObject.Parse(await cashier.GetStringAsync("api/workforce/shifts/current"))["data"]["id"]);
                        Assert.Equal(HttpStatusCode.Conflict,(await Send(cashier,HttpMethod.Post,"api/workforce/shifts/close",new{id=shiftId,amount="32.34",expectedScaled=0,operationKey="stale-close"},csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK,(await Send(cashier,HttpMethod.Post,"api/workforce/shifts/close",new{id=shiftId,amount="32.34",expectedScaled=323400,operationKey="required-close"},csrf)).StatusCode);
                        var next=await f.Draft();Assert.Equal(HttpStatusCode.Conflict,(await Send(cashier,HttpMethod.Post,"api/sales/"+next.Id+"/post",new{operationKey="after-close"},csrf)).StatusCode);
                        var eligibility=await new AlKhayal.Data.Sales.SqliteSalesStore(f.Db.ConnectionString).GetForEditAsync(invoiceId,cashierId,"2026-11-01",CancellationToken.None);Assert.False(eligibility.CanEditPosted);Assert.Equal("different_or_closed_shift",eligibility.EditRestrictionCode);
                    }
                }
            }
        }
    }
    public sealed class RequiredShiftTests
    {
        [Fact]
        public async Task Drawer_cash_card_wallet_refund_manual_movements_pages_and_closed_snapshots()
        {
            var ct=CancellationToken.None;
            using(var f=new SalesTreasuryTests.Fixture())
            {
                await f.Prepare(openShift:false);var workforce=new WorkforceService(new SqliteWorkforceStore(f.Db.ConnectionString));
                var id=await workforce.ExecuteAsync("shift-open",new WorkforceCommand{Amount="100",OperationKey="cash-open"},f.Db.UserId,true,ct);
                var cash=await f.Draft();Assert.True((await f.Post(cash.Id,"cash")).Succeeded);
                foreach(var method in new[]{"card","wallet"})
                {
                    long instrument;using(var c=f.Db.Open())instrument=c.ExecuteScalar<long>("INSERT INTO PaymentInstruments(Type,Name,Number,IdentityKey,IsActive,CreatedUtc,UpdatedUtc) VALUES(@method,@method,'123',@method,1,@now,@now); SELECT last_insert_rowid();",new{method,now=DateTime.UtcNow.ToString("o")});
                    var electronic=await f.Sales.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-11-01",PaymentMode="cash",ImmediateMethod=method,PaymentInstrumentId=instrument,Lines=new(){new SaleLine{VariantId=f.Variant,ProductUnitId=f.Unit,Quantity="1",UnitPrice="12.34"}}},f.Db.UserId,ct);
                    Assert.True((await f.Post(electronic.Id,method)).Succeeded);
                }
                var shift=await workforce.ShiftAsync(id,null,ct);Assert.Equal("112.34",shift.Expected);Assert.Equal(3,shift.SalesCount);
                var line=(await f.Sales.GetAsync(cash.Id,ct)).Lines.Single();
                var returned=await f.Sales.ReturnAsync(cash.Id,new SaleReturnCommand{OperationKey="cash-return",Reason="return",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="1"}}},f.Db.UserId,ct);Assert.True(returned.Succeeded,returned.ErrorMessage);
                Assert.Equal("100.00",(await workforce.ShiftAsync(id,null,ct)).Expected);
                long account;using(var c=f.Db.Open())account=c.ExecuteScalar<long>("SELECT AccountId FROM ShiftRevenueTreasury");
                var treasury=f.Db.Service;
                var deposit=new AlKhayal.Application.Treasury.TreasuryMovementCommand{AccountId=account,Amount="5",OperationKey="drawer-deposit"};
                await treasury.PostAsync(deposit,"deposit",f.Db.UserId,ct);await treasury.PostAsync(deposit,"deposit",f.Db.UserId,ct);
                await treasury.PostAsync(new AlKhayal.Application.Treasury.TreasuryMovementCommand{AccountId=account,Amount="2",OperationKey="drawer-withdraw"},"withdraw",f.Db.UserId,ct);
                Assert.Equal("103.00",(await workforce.ShiftAsync(id,null,ct)).Expected);
                var activity=await workforce.ActivityAsync(id,1,null,ct);Assert.Contains(activity.Items,x=>x.Kind=="treasury");Assert.Equal(3,activity.Items.Count(x=>x.Kind=="invoice"));
                Assert.Empty((await workforce.ActivityAsync(id,100,null,ct)).Items);
                using(var c=f.Db.Open())for(var n=0;n<110;n++)c.Execute("INSERT INTO WorkforceAudit(Action,EntityId,ActorUserId,Detail,CreatedUtc) VALUES('shift-adjust',@id,@actor,'page fixture',@now)",new{id,actor=f.Db.UserId,now=DateTime.UtcNow.ToString("o")});
                var first=await workforce.ActivityAsync(id,1,null,ct);var second=await workforce.ActivityAsync(id,2,null,ct);Assert.Equal(50,first.Items.Count);Assert.Equal(50,second.Items.Count);Assert.Equal(first.Total,second.Total);Assert.Empty(first.Items.Select(x=>x.Kind+":"+x.Id).Intersect(second.Items.Select(x=>x.Kind+":"+x.Id)));
                await workforce.ExecuteAsync("shift-close",new WorkforceCommand{Id=id,Amount="102",ExpectedScaled=1030000,OperationKey="cash-close"},f.Db.UserId,true,ct);
                var closed=await workforce.ShiftAsync(id,null,ct);Assert.Equal("-1.00",closed.Difference);Assert.Equal(1,closed.UserShiftCount);
                using(var c=f.Db.Open())Assert.Empty(c.Query("PRAGMA foreign_key_check"));
            }
        }
    }
}
