using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Reports;
using AlKhayal.Application.Sales;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Database;
using AlKhayal.Data.Inventory;
using AlKhayal.Data.Reports;
using AlKhayal.Data.Sales;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class SalesStockFloorTests
    {
        [Fact]
        public async Task Product_can_disable_sale_when_stock_is_insufficient()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);
                using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('floor-disabled','test-only','2026-01-01T00:00:00Z'); INSERT INTO CashierShifts(CashierId,RegisterId,OpenedUtc,OpenedBy,OpeningScaled) VALUES(1,'standalone','2026-01-01T00:00:00Z',1,0)");}
                var catalog=new CatalogService(new SqliteCatalogStore(cs));
                var product=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="floor-disabled-product",Name="بيع بحد مخزون",BaseUnitId=1,ProductType="stock",SalePrice="15",IsActive=true,AllowSaleWhenOutOfStock=false},CancellationToken.None);
                Assert.True(product.Succeeded,product.ErrorMessage);Assert.False(product.Value.AllowSaleWhenOutOfStock);
                long variant,unit;using(var c=new SQLiteConnection(cs)){c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=product.Value.Id});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=product.Value.Id});Assert.Equal(0,c.ExecuteScalar<long>("SELECT AllowSaleWhenOutOfStock FROM ProductDetails WHERE ProductId=@Id",new{Id=product.Value.Id}));}
                var sales=new SalesService(new SqliteSalesStore(cs));var draft=await sales.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitPrice="15"}}},1,CancellationToken.None);
                var posted=await sales.PostAsync(draft.Id,"floor-disabled-sale",1,CancellationToken.None);
                Assert.False(posted.Succeeded);Assert.Equal("insufficient_stock",posted.ErrorCode);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal("draft",c.ExecuteScalar<string>("SELECT Status FROM SalesInvoices WHERE Id=@Id",new{Id=draft.Id}));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryDocuments WHERE OperationKey='sale:floor-disabled-sale'"));}
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Theory]
        [InlineData(0,5,0,0)]
        [InlineData(3,5,0,300000)]
        [InlineData(10,4,6,400000)]
        public async Task Sale_consumes_only_available_stock_and_fifo_cost(int starting,int sold,int remaining,long cost)
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);
                using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('floor-user','test-only','2026-01-01T00:00:00Z'); INSERT INTO CashierShifts(CashierId,RegisterId,OpenedUtc,OpenedBy,OpeningScaled) VALUES(1,'standalone','2026-01-01T00:00:00Z',1,0)");}
                var catalog=new CatalogService(new SqliteCatalogStore(cs));
                var product=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="floor-product",Name="اختبار نفاد",BaseUnitId=1,ProductType="stock",SalePrice="15",PurchasePrice="10",IsActive=true},CancellationToken.None);
                Assert.True(product.Succeeded,product.ErrorMessage);
                long variant,unit;using(var c=new SQLiteConnection(cs)){c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=product.Value.Id});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=product.Value.Id});}
                if(starting>0)
                {
                    var receipt=await new SqliteInventoryStore(cs).PostAsync(new InventoryPostCommand{OperationKey="floor-receipt",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity=starting.ToString(),UnitCost="10",SalePrice="15"}}},"floor-receipt-hash",CancellationToken.None);
                    Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                }
                var sales=new SalesService(new SqliteSalesStore(cs));
                var draft=await sales.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity=sold.ToString(),UnitPrice="15"}}},1,CancellationToken.None);
                var posted=await sales.PostAsync(draft.Id,"floor-sale",1,CancellationToken.None);
                Assert.True(posted.Succeeded,posted.ErrorMessage);
                Assert.Equal(sold.ToString(),posted.Invoice.Lines.Single().Quantity);
                using(var c=new SQLiteConnection(cs))
                {
                    c.Open();
                    Assert.Equal((long)remaining*1000000,c.ExecuteScalar<long>("SELECT COALESCE((SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant),0)",new{Variant=variant}));
                    Assert.Equal(cost,c.ExecuteScalar<long>("SELECT COALESCE(SUM(a.ValueScaled),0) FROM InventoryCostAllocations a JOIN StockMovements m ON m.Id=a.MovementId WHERE m.DocumentId=@Id",new{Id=posted.Invoice.InventoryDocumentId}));
                    Assert.Equal((long)-Math.Min(starting,sold)*1000000,c.ExecuteScalar<long>("SELECT COALESCE(SUM(QuantityBaseScaled),0) FROM StockMovements WHERE DocumentId=@Id",new{Id=posted.Invoice.InventoryDocumentId}));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
                Assert.True(await new SqliteInventoryStore(cs).VerifyProjectionAsync(CancellationToken.None));
                var profit=await new NetProfitService(new SqliteNetProfitStore(cs)).QueryAsync(new NetProfitQuery{From="2026-01-01",To="2026-12-31",Breakdown="monthly"},CancellationToken.None);
                 Assert.Equal(0,profit.Summary.MissingCostCount);
                 Assert.True(profit.Summary.Complete);
                 Assert.Equal(((sold-Math.Min(starting,sold))*10m).ToString("0.0000",System.Globalization.CultureInfo.InvariantCulture),profit.Summary.EstimatedCogs);
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        public async Task Return_of_shortfall_sale_refunds_full_quantity_but_restores_only_consumed_stock(int starting)
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);
                using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('floor-return','test-only','2026-01-01T00:00:00Z'); INSERT INTO CashierShifts(CashierId,RegisterId,OpenedUtc,OpenedBy,OpeningScaled) VALUES(1,'standalone','2026-01-01T00:00:00Z',1,0); INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('FLOOR','عميل','0100','',0,1000,'أساسي','نشط','2026-01-01','2026-01-01')");}
                var catalog=new CatalogService(new SqliteCatalogStore(cs));var product=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="floor-return-product",Name="اختبار إرجاع",BaseUnitId=1,ProductType="stock",SalePrice="15",PurchasePrice="10",IsActive=true},CancellationToken.None);Assert.True(product.Succeeded,product.ErrorMessage);
                long variant,unit,customer;using(var c=new SQLiteConnection(cs)){c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=product.Value.Id});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=product.Value.Id});customer=c.ExecuteScalar<long>("SELECT Id FROM Customers WHERE Code='FLOOR'");}
                if(starting>0){var receipt=await new SqliteInventoryStore(cs).PostAsync(new InventoryPostCommand{OperationKey="floor-return-receipt",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity=starting.ToString(),UnitCost="10",SalePrice="15"}}},"floor-return-receipt-hash",CancellationToken.None);Assert.True(receipt.Succeeded,receipt.ErrorMessage);}
                var sales=new SalesService(new SqliteSalesStore(cs));var draft=await sales.CreateDraftAsync(new SaleDraftCommand{CustomerId=customer,WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="credit",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="5",UnitPrice="15"}}},1,CancellationToken.None);
                var posted=await sales.PostAsync(draft.Id,"floor-return-sale",1,CancellationToken.None);Assert.True(posted.Succeeded,posted.ErrorMessage);
                var returned=await sales.ReturnAsync(draft.Id,new SaleReturnCommand{OperationKey="floor-return",Reason="إرجاع كامل",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=posted.Invoice.Lines.Single().Id,Quantity="5"}}},1,CancellationToken.None);
                Assert.True(returned.Succeeded,returned.ErrorMessage);Assert.Equal(posted.Invoice.TotalScaled,returned.TotalScaled);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal((long)starting*1000000,c.ExecuteScalar<long>("SELECT COALESCE((SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant),0)",new{Variant=variant}));Assert.Equal((long)starting*100000,c.ExecuteScalar<long>("SELECT COALESCE(SUM(ValueScaled),0) FROM SalesReturnCostAllocations"));Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=customer}));}
                Assert.True(await new SqliteInventoryStore(cs).VerifyProjectionAsync(CancellationToken.None));
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Theory]
        [InlineData("lot",0)] [InlineData("lot",1)]
        [InlineData("expiry",0)] [InlineData("expiry",1)]
        [InlineData("serial",0)] [InlineData("serial",1)]
        [InlineData("imei",0)] [InlineData("imei",1)]
        public async Task Tracked_sale_and_return_use_only_real_stock_and_identities(string tracking,int starting)
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);
                using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('tracked-floor','test-only','2026-01-01T00:00:00Z'); INSERT INTO CashierShifts(CashierId,RegisterId,OpenedUtc,OpenedBy,OpeningScaled) VALUES(1,'standalone','2026-01-01T00:00:00Z',1,0); INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('TRACK','عميل','0100','',0,1000,'أساسي','نشط','2026-01-01','2026-01-01')");}
                var catalog=new CatalogService(new SqliteCatalogStore(cs));var product=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="tracked-floor-product",Name="صنف متتبع",BaseUnitId=1,ProductType="stock",SalePrice="15",PurchasePrice="10",IsActive=true},CancellationToken.None);Assert.True(product.Succeeded,product.ErrorMessage);
                long variant,unit,customer;using(var c=new SQLiteConnection(cs))
                {
                    c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=product.Value.Id});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=product.Value.Id});customer=c.ExecuteScalar<long>("SELECT Id FROM Customers WHERE Code='TRACK'");
                    var keys=tracking=="lot"?new[]{"batch-tracking"}:tracking=="expiry"?new[]{"batch-tracking","expiry-dates"}:tracking=="serial"?new[]{"serial-numbers"}:new[]{"serial-numbers","imei-tracking"};
                    foreach(var key in keys)c.Execute("INSERT INTO ProductCapabilityAssignments(ProductId,CapabilityKey,IsEnabled,AssignedUtc,UpdatedUtc,ChangedByUserId) VALUES(@Product,'"+key+"',1,'2026-01-01','2026-01-01',1)",new{Product=product.Value.Id});
                }
                var expiry=DateTime.Today.AddDays(30).ToString("yyyy-MM-dd");var batch=tracking=="lot"||tracking=="expiry";
                var serial=tracking=="serial"||tracking=="imei";var imei=tracking=="imei";
                if(starting>0)
                {
                    var receipt=await new SqliteInventoryStore(cs).PostAsync(new InventoryPostCommand{OperationKey="tracked-floor-receipt",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitCost="10",SalePrice="15",LotNumber=batch?"LOT-TRACK":null,ExpirationDate=tracking=="expiry"?expiry:null,PhysicalUnits=serial?new[]{new InventoryUnitInput{Serial="TRACK-SN-1",Imeis=imei?new[]{"490154203237518"}:new string[0]}}:null}}},"tracked-floor-receipt-hash",CancellationToken.None);
                    Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                }
                var units=serial&&starting>0?new(){new SaleTrackingUnit{Serial="TRACK-SN-1",Imeis=imei?new(){"490154203237518"}:new()}}:new System.Collections.Generic.List<SaleTrackingUnit>();
                var sales=new SalesService(new SqliteSalesStore(cs));
                if(serial&&starting>0)
                {
                    var bad=await sales.CreateDraftAsync(new SaleDraftCommand{CustomerId=customer,WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="credit",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="2",UnitPrice="15",PhysicalUnits=new(){new SaleTrackingUnit{Serial="NOT-IN-STOCK",Imeis=imei?new(){"490154203237518"}:new()}}}}},1,CancellationToken.None);
                    var rejected=await sales.PostAsync(bad.Id,"tracked-invalid-serial",1,CancellationToken.None);
                    Assert.False(rejected.Succeeded);Assert.Equal("identifier_not_in_stock",rejected.ErrorCode);
                }
                var draft=await sales.CreateDraftAsync(new SaleDraftCommand{CustomerId=customer,WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="credit",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="2",UnitPrice="15",BatchLotNumber=batch?(starting>0?"LOT-TRACK":"UNSTOCKED-LOT"):null,ExpirationDate=tracking=="expiry"?expiry:null,PhysicalUnits=units}}},1,CancellationToken.None);
                var posted=await sales.PostAsync(draft.Id,"tracked-floor-sale",1,CancellationToken.None);Assert.True(posted.Succeeded,posted.ErrorMessage);
                Assert.Equal(serial?starting:0,posted.Invoice.Lines.Single().AvailableTrackingUnits.Count);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(0,c.ExecuteScalar<long>("SELECT COALESCE((SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant),0)",new{Variant=variant}));Assert.Equal(starting,c.ExecuteScalar<long>("SELECT COUNT(*) FROM StockMovements WHERE DocumentId=@Id",new{Id=posted.Invoice.InventoryDocumentId}));Assert.Equal(serial?starting:0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM StockMovementUnits smu JOIN StockMovements m ON m.Id=smu.MovementId WHERE m.DocumentId=@Id",new{Id=posted.Invoice.InventoryDocumentId}));if(starting==0&&batch)Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM ProductBatches WHERE VariantId=@Variant",new{Variant=variant}));}
                 var profit=await new NetProfitService(new SqliteNetProfitStore(cs)).QueryAsync(new NetProfitQuery{From="2026-01-01",To="2026-12-31",Breakdown="monthly"},CancellationToken.None);Assert.True(profit.Summary.Complete);Assert.Equal(((2-starting)*10m).ToString("0.0000",System.Globalization.CultureInfo.InvariantCulture),profit.Summary.EstimatedCogs);
                var returned=await sales.ReturnAsync(draft.Id,new SaleReturnCommand{OperationKey="tracked-floor-return",Reason="إرجاع كامل",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=posted.Invoice.Lines.Single().Id,Quantity="2",Serials=serial&&starting>0?new(){"TRACK-SN-1"}:new()}}},1,CancellationToken.None);Assert.True(returned.Succeeded,returned.ErrorMessage);Assert.Equal(posted.Invoice.TotalScaled,returned.TotalScaled);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal((long)starting*1000000,c.ExecuteScalar<long>("SELECT COALESCE((SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant),0)",new{Variant=variant}));Assert.Equal((long)starting*100000,c.ExecuteScalar<long>("SELECT COALESCE(SUM(ValueScaled),0) FROM SalesReturnCostAllocations"));Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=customer}));}
                if(serial&&starting==0)
                {
                    var cancelDraft=await sales.CreateDraftAsync(new SaleDraftCommand{CustomerId=customer,WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="credit",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitPrice="15"}}},1,CancellationToken.None);
                    var cancelPosted=await sales.PostAsync(cancelDraft.Id,"tracked-floor-cancel-sale",1,CancellationToken.None);Assert.True(cancelPosted.Succeeded,cancelPosted.ErrorMessage);
                    var canceled=await sales.CancelAsync(cancelDraft.Id,"tracked-floor-cancel","إلغاء بيع بلا مخزون",1,CancellationToken.None);Assert.True(canceled.Succeeded,canceled.ErrorMessage);
                    using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM StockMovements WHERE DocumentId=@Id",new{Id=cancelPosted.Invoice.InventoryDocumentId}));Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=customer}));}
                }
                Assert.True(await new SqliteInventoryStore(cs).VerifyProjectionAsync(CancellationToken.None));
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
    }
}
