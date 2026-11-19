using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Alerts;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Inventory;
using AlKhayal.Data.Alerts;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Database;
using AlKhayal.Data.Inventory;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class AlertPersistenceTests : IDisposable
    {
        private static readonly CancellationToken Ct=CancellationToken.None;
        private static readonly DateTime AsOfUtc=new DateTime(2026,11,1,12,0,0,DateTimeKind.Utc);
        private readonly string _path=Path.Combine(Path.GetTempPath(),"alkhayal-alerts-"+Guid.NewGuid().ToString("N")+".db");
        private readonly string _cs;
        private readonly CatalogService _catalog;
        private readonly SqliteInventoryStore _inventory;

        public AlertPersistenceTests()
        {
            _cs=Db.ConnectionStringFor(_path);
            Migrator.Run(_cs);
            _catalog=new CatalogService(new SqliteCatalogStore(_cs));
            _inventory=new SqliteInventoryStore(_cs);
            using(var c=Open())c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('alert-user-1','test-only','2026-01-01'),('alert-user-2','test-only','2026-01-01')");
        }

        [Fact]
        public async Task Every_supported_category_uses_persisted_rows_balances_and_related_records()
        {
            var zero=await Product("zero-stock","5");
            var reorder=await Product("reorder-stock","10");
            await Receive(reorder.Id,1,"6","reorder-receipt");
            var expiring=await Product("expiring-batch",null);
            EnableExpiry(expiring.Id);
            await Receive(expiring.Id,1,"2","expiry-first","LOT-1","2026-11-20");
            var second=await _inventory.SaveWarehouseAsync(new WarehouseCommand{Code="ALERT-W2",Name="مخزن تنبيهات ثان",IsActive=true},true,Ct);
            Assert.True(second.Succeeded,second.ErrorMessage);
            await Receive(expiring.Id,second.Value.Id,"1","expiry-second","LOT-1","2026-11-20");
            var installment=CreateInstallments(expiring.Id);

            var rows=await Store().ActiveAsync(1,AsOfUtc,Ct);
            Assert.Equal(1,rows.Count(x=>x.Type=="low-stock"));
            Assert.Equal(zero.Id,rows.Single(x=>x.Type=="low-stock").RelatedId);
            Assert.Equal("product-edit",rows.Single(x=>x.Type=="low-stock").RelatedRoute);
            Assert.Equal(1,rows.Count(x=>x.Type=="reorder"));
            Assert.Equal("6",rows.Single(x=>x.Type=="reorder").TotalQuantity);
            var expiries=rows.Where(x=>x.Type=="expiry").OrderBy(x=>x.WarehouseId).ToList();
            Assert.Equal(2,expiries.Count);
            Assert.Equal(new[]{"2","1"},expiries.Select(x=>x.TotalQuantity).ToArray());
            Assert.All(expiries,x=>Assert.Equal("LOT-1",x.LotNumber));
            Assert.Equal(2,expiries.Select(x=>x.Id).Distinct().Count());
            Assert.All(expiries,x=>Assert.Equal(expiring.Id,x.RelatedId));
            Assert.All(expiries,x=>Assert.Equal("product-edit",x.RelatedRoute));
            var installments=rows.Where(x=>x.Type=="installment").OrderBy(x=>x.Id).ToList();
            Assert.Equal(2,installments.Count);
            Assert.Contains(installments,x=>x.Detail.Contains("100.00"));
            Assert.Contains(installments,x=>x.Detail.Contains("60.00"));
            Assert.DoesNotContain(installments,x=>x.Id.EndsWith(":3",StringComparison.Ordinal));
            Assert.All(installments,x=>Assert.Equal("installment-payment",x.RelatedRoute));
            Assert.All(installments,x=>Assert.Equal(installment.ContractId,x.RelatedId));
            Assert.All(installments,x=>Assert.Equal(installment.CustomerId,x.RelatedParentId));
            Assert.DoesNotContain(rows,x=>x.Type=="other");

            await Issue(expiring.Id,1,"2","expiry-issue-first","LOT-1");
            await Issue(expiring.Id,second.Value.Id,"1","expiry-issue-second","LOT-1");
            Assert.DoesNotContain(await Store().ActiveAsync(1,AsOfUtc,Ct),x=>x.Type=="expiry");
        }

        [Fact]
        public async Task Expiry_threshold_status_and_remaining_quantity_are_batch_specific()
        {
            var product=await Product("expiry-boundaries",null);EnableExpiry(product.Id);
            await Receive(product.Id,1,"20","expiry-near","LOT-NEAR","2026-12-01");
            await Receive(product.Id,1,"15","expiry-outside","LOT-LATER","2026-12-02");
            await Receive(product.Id,1,"4","expiry-old","LOT-OLD","2026-10-31");
            var initial=await Store().ActiveAsync(1,AsOfUtc,Ct);
            Assert.Contains(initial,x=>x.Type=="expiry"&&x.LotNumber=="LOT-NEAR"&&x.TotalQuantity=="20"&&x.Severity=="تحذير");
            Assert.DoesNotContain(initial,x=>x.Type=="expiry"&&x.LotNumber=="LOT-LATER");
            Assert.Contains(initial,x=>x.Type=="expiry"&&x.LotNumber=="LOT-OLD"&&x.TotalQuantity=="4"&&x.Severity=="خطر");
            await Issue(product.Id,1,"5","expiry-near-partial","LOT-NEAR");
            Assert.Contains(await Store().ActiveAsync(1,AsOfUtc,Ct),x=>x.LotNumber=="LOT-NEAR"&&x.TotalQuantity=="15");
            await Issue(product.Id,1,"15","expiry-near-complete","LOT-NEAR");
            Assert.DoesNotContain(await Store().ActiveAsync(1,AsOfUtc,Ct),x=>x.LotNumber=="LOT-NEAR");
        }

        [Fact]
        public async Task Read_state_is_per_user_survives_store_restart_and_mark_all_respects_stock_only_access()
        {
            await Product("read-zero","2");
            var installmentProduct=await Product("read-installment",null);
            CreateInstallments(installmentProduct.Id);
            var first=await Store().ActiveAsync(1,AsOfUtc,Ct);
            var stock=first.Single(x=>x.Type=="low-stock");
            await Store().SetReadAsync(1,stock.Id,true,AsOfUtc,Ct);

            var restarted=new SqliteAlertStore(_cs);
            Assert.True((await restarted.ActiveAsync(1,AsOfUtc,Ct)).Single(x=>x.Id==stock.Id).Read);
            Assert.False((await restarted.ActiveAsync(2,AsOfUtc,Ct)).Single(x=>x.Id==stock.Id).Read);
            await restarted.MarkAllReadAsync(2,AsOfUtc,true,Ct);
            var userTwo=await new SqliteAlertStore(_cs).ActiveAsync(2,AsOfUtc,Ct);
            Assert.All(userTwo.Where(x=>x.Type=="low-stock"||x.Type=="reorder"),x=>Assert.True(x.Read));
            Assert.All(userTwo.Where(x=>x.Type=="installment"),x=>Assert.False(x.Read));
            await restarted.MarkAllReadAsync(1,AsOfUtc,false,Ct);
            Assert.All(await new SqliteAlertStore(_cs).ActiveAsync(1,AsOfUtc,Ct),x=>Assert.True(x.Read));
        }

        [Fact]
        public void Migration_082_upgrades_an_existing_database_without_changing_existing_rows()
        {
            long userId;
            using(var c=Open())
            {
                userId=c.ExecuteScalar<long>("SELECT Id FROM Users WHERE Username='alert-user-1'");
                c.Execute("DROP TABLE AlertReadStates; DELETE FROM SchemaVersion WHERE Name LIKE '%082_alert_read_states.sql'");
            }
            Migrator.Run(_cs);
            using(var c=Open())
            {
                Assert.Equal(userId,c.ExecuteScalar<long>("SELECT Id FROM Users WHERE Username='alert-user-1'"));
                Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion WHERE Name LIKE '%082_alert_read_states.sql'"));
                Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='AlertReadStates'"));
                Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_AlertReadStates_User'"));
                Assert.Empty(c.Query<string>("PRAGMA foreign_key_check"));
                Assert.Equal("ok",c.ExecuteScalar<string>("PRAGMA integrity_check"));
            }
        }

        private AlertService Service(){return new AlertService(Store());}
        private SqliteAlertStore Store(){return new SqliteAlertStore(_cs);}
        private SQLiteConnection Open(){var c=new SQLiteConnection(_cs);c.Open();return c;}
        private async Task<CatalogProduct> Product(string key,string threshold)
        {
            var result=await _catalog.CreateProductAsync(new CreateProductCommand{OperationKey=key,Name=key,BaseUnitId=1,ProductType="stock",SalePrice="1",IsActive=true,ReorderPoint=threshold},Ct);
            Assert.True(result.Succeeded,result.ErrorMessage);return result.Value;
        }
        private void EnableExpiry(long productId)
        {
            using(var c=Open())foreach(var capability in new[]{"batch-tracking","expiry-dates"})c.Execute("INSERT INTO ProductCapabilityAssignments(ProductId,CapabilityKey,IsEnabled,AssignedUtc,UpdatedUtc,ChangedByUserId) VALUES(@productId,@capability,1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z',1)",new{productId,capability});
        }
        private async Task Receive(long productId,long warehouseId,string quantity,string key,string lot=null,string expiry=null)
        { await Post(productId,warehouseId,quantity,key,"manual_receipt",lot,expiry,"1"); }
        private async Task Issue(long productId,long warehouseId,string quantity,string key,string lot)
        { await Post(productId,warehouseId,quantity,key,"manual_issue",lot,null,null); }
        private async Task Post(long productId,long warehouseId,string quantity,string key,string type,string lot,string expiry,string cost)
        {
            long variantId,unitId;using(var c=Open()){variantId=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@productId AND IsDefault=1",new{productId});unitId=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@productId AND IsBase=1",new{productId});}
            var result=await _inventory.PostAsync(new InventoryPostCommand{OperationKey=key,Type=type,WarehouseId=warehouseId,UserId=1,Reason="اختبار تنبيهات",Lines=new[]{new InventoryLineInput{VariantId=variantId,ProductUnitId=unitId,Quantity=quantity,UnitCost=cost,SalePrice="1",LotNumber=lot,ExpirationDate=expiry}}},"alerts:"+key,Ct);
            Assert.True(result.Succeeded,result.ErrorMessage);
        }
        private (long CustomerId,long ContractId) CreateInstallments(long productId)
        {
            using(var c=Open())
            {
                c.Execute("INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES(@code,'عميل تنبيهات','01000000000','',0,0,'أساسي','نشط','2026-01-01','2026-01-01')",new{code="ALERT-"+Guid.NewGuid().ToString("N")});
                var customerId=c.ExecuteScalar<long>("SELECT last_insert_rowid()");
                c.Execute(@"INSERT INTO CustomerInstallmentContracts(CustomerId,ProductId,OperationKey,RequestHash,ContractNumber,ProductName,ProductPriceScaled,DownPaymentScaled,InterestRateBasisPoints,TotalAmountScaled,InstallmentCount,IntervalMonths,FirstDueOn,CreatedUtc)
                    VALUES(@customerId,@productId,@key,'hash',@number,'منتج تقسيط',3000000,0,0,3000000,3,1,'2026-10-01','2026-01-01T00:00:00Z')",new{customerId,productId,key="contract-"+Guid.NewGuid().ToString("N"),number="ALERT-"+Guid.NewGuid().ToString("N")});
                var contractId=c.ExecuteScalar<long>("SELECT last_insert_rowid()");
                c.Execute("INSERT INTO CustomerInstallmentSchedule(ContractId,InstallmentNumber,DueOn,AmountScaled) VALUES(@contractId,1,'2026-10-01',1000000),(@contractId,2,'2026-10-02',1000000),(@contractId,3,'2026-10-03',1000000)",new{contractId});
                c.Execute(@"INSERT INTO CustomerInstallmentPayments(ContractId,InstallmentNumber,OperationKey,RequestHash,AmountScaled,PaidOn,CreatedUtc) VALUES
                    (@contractId,2,@partial,'hash',400000,'2026-10-02','2026-10-02T00:00:00Z'),
                    (@contractId,3,@full,'hash',1000000,'2026-10-03','2026-10-03T00:00:00Z')",new{contractId,partial="partial-"+Guid.NewGuid().ToString("N"),full="full-"+Guid.NewGuid().ToString("N")});
                return(customerId,contractId);
            }
        }

        public void Dispose()
        {
            SQLiteConnection.ClearAllPools();
            foreach(var file in new[]{_path,_path+"-wal",_path+"-shm"})if(File.Exists(file))File.Delete(file);
        }
    }
}
