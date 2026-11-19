using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Printing;
using AlKhayal.Application.Purchasing;
using AlKhayal.Application.Reports;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Settings;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Database;
using AlKhayal.Data.Inventory;
using AlKhayal.Data.Purchasing;
using AlKhayal.Data.Reports;
using AlKhayal.Data.Sales;
using AlKhayal.Data.Settings;
using AlKhayal.Data.Suppliers;
using AlKhayal.Core.Models;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class ManualWeightTests
    {
        [Fact]
        public async Task Fractional_receipt_adjustment_transfer_and_count_keep_scaled_balances()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);
                using(var c=new SQLiteConnection(cs))
                {
                    c.Open();Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM Units WHERE NormalizedCode='KGM'"));
                    c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('weight-stock','test-only','2026-01-01T00:00:00Z'); INSERT INTO Warehouses(Code,NormalizedCode,Name,IsActive,IsDefault,CreatedUtc,UpdatedUtc) VALUES('SECOND','SECOND','مخزن ثان',1,0,'2026-01-01','2026-01-01')");
                }
                var product=await new CatalogService(new SqliteCatalogStore(cs)).CreateProductAsync(new CreateProductCommand{OperationKey="weight-stock-product",Name="موزون مخزون",BaseUnitId=1,SellingType="weight",SalePrice="200",IsActive=true},CancellationToken.None);
                Assert.True(product.Succeeded,product.ErrorMessage);long variant,unit;
                using(var c=new SQLiteConnection(cs)){c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=product.Value.Id});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=product.Value.Id});}
                var inventory=new SqliteInventoryStore(cs);
                async Task Post(string key,string type,string quantity,long? destination=null)
                {
                    var result=await inventory.PostAsync(new InventoryPostCommand{OperationKey=key,Type=type,WarehouseId=1,DestinationWarehouseId=destination,UserId=1,Reason="حركة وزن",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity=quantity,UnitCost=type=="manual_receipt"?"100":null,SalePrice=type=="manual_receipt"?"200":null}}},key+"-hash",CancellationToken.None);
                    Assert.True(result.Succeeded,result.ErrorMessage);
                }
                var suppliers=new SqliteSupplierRepository(cs);var supplier=suppliers.Add(new Supplier{Code="WEIGHT-SUPPLIER",Name="مورد وزن",Phone="",Status="نشط"});
                var purchases=new PurchaseService(new SqlitePurchaseRepository(cs),suppliers);
                var purchase=await purchases.CreateDraftAsync(new PurchaseDraftCommand{SupplierId=supplier,WarehouseId=1,InvoiceDate=DateTime.Now.ToString("yyyy-MM-dd"),PaymentMode="credit",Lines=new System.Collections.Generic.List<PurchaseInvoiceLine>{new PurchaseInvoiceLine{VariantId=variant,ProductUnitId=unit,Quantity="10.000",PurchaseUnitPrice="100",SaleUnitPrice="200"}}},1,CancellationToken.None);
                var purchasePost=await purchases.PostAsync(purchase.Id,"weight-purchase",1,CancellationToken.None);Assert.True(purchasePost.Succeeded,purchasePost.ErrorMessage);
                Assert.True((await purchases.PostAsync(purchase.Id,"weight-purchase",1,CancellationToken.None)).Replayed);
                var savedPurchase=await purchases.GetAsync(purchase.Id,CancellationToken.None);Assert.Equal("كجم",savedPurchase.Lines.Single().UnitName);Assert.Equal("10",savedPurchase.Lines.Single().Quantity);
                await Post("weight-stock-adjust","adjustment","-0.125");
                await Post("weight-stock-transfer","transfer","1.375",2);
                var count=await inventory.CreateCountAsync(1,1,null,CancellationToken.None);Assert.True(count.Succeeded,count.ErrorMessage);
                var countLine=count.Value.Lines.Single();
                var posted=await inventory.PostCountAsync(count.Value.Id,new InventoryCountPostCommand{OperationKey="weight-stock-count",Reason="جرد",UserId=1,Lines=new[]{new InventoryCountLine{VariantId=countLine.VariantId,BatchId=countLine.BatchId,BalanceVersion=countLine.BalanceVersion,CountedQuantity="8.250"}}},"weight-stock-count-hash",CancellationToken.None);
                Assert.True(posted.Succeeded,posted.ErrorMessage);
                using(var c=new SQLiteConnection(cs))
                {
                    c.Open();Assert.Equal(8250000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));
                    Assert.Equal(1375000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=2 AND VariantId=@Variant",new{Variant=variant}));
                    Assert.Equal(9625000,c.ExecuteScalar<long>("SELECT SUM(RemainingQuantityScaled) FROM InventoryCostLayers WHERE VariantId=@Variant",new{Variant=variant}));
                }
                Assert.True(await inventory.VerifyProjectionAsync(CancellationToken.None));
                var report=await new StockReportService(new SqliteStockReportStore(cs),TimeZoneInfo.Utc).QueryAsync("inventory-valuation",new StockReportQuery(),CancellationToken.None);
                Assert.Contains(report.Items,x=>x.WarehouseId==1 && x.Quantity=="8.25" && x.Unit=="كجم");
                Assert.Contains(report.Items,x=>x.WarehouseId==2 && x.Quantity=="1.375" && x.Unit=="كجم");
                var movements=await new StockReportService(new SqliteStockReportStore(cs),TimeZoneInfo.Utc).QueryAsync("inventory-movements",new StockReportQuery{From="2000-01-01",To="2099-12-31"},CancellationToken.None);
                Assert.Contains(movements.Items,x=>x.Unit=="كجم" && x.QuantityOut=="0.125");
                var purchaseReturn=await purchases.PostReturnAsync(purchase.Id,new PurchaseReturnCommand{WarehouseId=2,ReturnMode="supplier_credit",Lines=new System.Collections.Generic.List<PurchaseReturnLine>{new PurchaseReturnLine{PurchaseInvoiceLineId=savedPurchase.Lines.Single().Id,Quantity="0.125"}}},"weight-purchase-return",1,CancellationToken.None);
                Assert.True(purchaseReturn.Succeeded,purchaseReturn.ErrorMessage);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(1250000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=2 AND VariantId=@Variant",new{Variant=variant}));}
                Assert.True(await inventory.VerifyProjectionAsync(CancellationToken.None));
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task Weighted_product_and_fractional_sale_survive_fifo_return_and_restart()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);
                using(var c=new SQLiteConnection(cs))
                {
                    c.Open();Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM Units WHERE NormalizedCode='KGM'"));
                    c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('weight-user','test-only','2026-01-01T00:00:00Z'); INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('WEIGHT','عميل','0100','',0,10000,'أساسي','نشط','2026-01-01','2026-01-01'); INSERT INTO CashierShifts(CashierId,RegisterId,OpenedUtc,OpenedBy,OpeningScaled) VALUES(1,'standalone','2026-01-01T00:00:00Z',1,0)");
                }
                var catalog=new CatalogService(new SqliteCatalogStore(cs));
                var customUnit=await catalog.SaveUnitAsync(new SaveUnitCommand{Code="BOX-WEIGHT-TEST",Name="صندوق",Symbol="ص",IsActive=true},CancellationToken.None);
                Assert.True(customUnit.Succeeded,customUnit.ErrorMessage);
                var piece=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="weight-piece",Name="صنف بالقطعة",BaseUnitId=customUnit.Value.Id,SalePrice="10",IsActive=true},CancellationToken.None);
                Assert.True(piece.Succeeded,piece.ErrorMessage);Assert.Equal("piece",piece.Value.SellingType);Assert.Equal(customUnit.Value.Id,piece.Value.BaseUnitId);
                var reinterpret=await catalog.UpdateProductAsync(new UpdateProductCommand{ProductId=piece.Value.Id,Name=piece.Value.Name,BaseUnitId=piece.Value.BaseUnitId,SellingType="weight",SalePrice="10",Version=piece.Value.Version,IsActive=true},CancellationToken.None);
                Assert.False(reinterpret.Succeeded);Assert.Equal("piece",(await catalog.GetProductAsync(piece.Value.Id,CancellationToken.None)).Value.SellingType);
                var wrong=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="weight-invalid",Name="وحدة خاطئة",BaseUnitId=1,SellingType="invalid",SalePrice="200",IsActive=true},CancellationToken.None);
                Assert.False(wrong.Succeeded);
                var converted=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="weight-converted",Name="وزن بتحويل",BaseUnitId=1,SellingType="weight",SalePrice="200",IsActive=true,Configuration=new ProductConfigurationCommand{Units=new[]{new ProductUnitCommand{UnitId=1,Numerator=1,Denominator=1,IsBase=true,IsSaleUnit=true,IsPurchaseUnit=true,IsActive=true},new ProductUnitCommand{UnitId=customUnit.Value.Id,Numerator=2,Denominator=1,IsSaleUnit=true,IsPurchaseUnit=true,IsActive=true}}}},CancellationToken.None);
                Assert.False(converted.Succeeded);
                var weighted=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="weight-product",Name="منتج موزون",BaseUnitId=1,SellingType="weight",SalePrice="200",PurchasePrice="100",IsActive=true},CancellationToken.None);
                Assert.True(weighted.Succeeded,weighted.ErrorMessage);Assert.Equal("weight",weighted.Value.SellingType);
                Assert.Equal(1,weighted.Value.BaseUnitId);
                Assert.Contains(await catalog.ListUnitsAsync(true,CancellationToken.None),x=>x.Id==customUnit.Value.Id && x.Code=="BOX-WEIGHT-TEST");
                Assert.Equal("weight",(await catalog.GetProductDetailAsync(weighted.Value.Id,CancellationToken.None)).Value.SellingType);
                var edited=await catalog.UpdateProductAsync(new UpdateProductCommand{ProductId=weighted.Value.Id,Name="منتج موزون معدل",BaseUnitId=1,SellingType="weight",SalePrice="200",PurchasePrice="100",Version=weighted.Value.Version,IsActive=true},CancellationToken.None);
                Assert.True(edited.Succeeded,edited.ErrorMessage);
                long variant,unit,customer;
                using(var c=new SQLiteConnection(cs)){c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=weighted.Value.Id});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=weighted.Value.Id});customer=c.ExecuteScalar<long>("SELECT Id FROM Customers WHERE Code='WEIGHT'");}
                var inventory=new SqliteInventoryStore(cs);
                var receipt=await inventory.PostAsync(new InventoryPostCommand{OperationKey="weight-receipt",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity="10.000",UnitCost="100",SalePrice="200"}}},"weight-receipt-hash",CancellationToken.None);
                Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                var sales=new SalesService(new SqliteSalesStore(cs));
                foreach(var invalid in new[]{"0","-0.25","abc","0.0000001"})
                {
                    await Assert.ThrowsAsync<ArgumentException>(()=>sales.CreateDraftAsync(new SaleDraftCommand{CustomerId=customer,WarehouseId=1,InvoiceDate=DateTime.Now.ToString("yyyy-MM-dd"),PaymentMode="credit",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity=invalid,UnitPrice="200"}}},1,CancellationToken.None));
                }
                var quantities=new[]{"0.250","0.500","1.250"};var amounts=new long[]{500000,1000000,2500000};long secondInvoice=0,thirdInvoice=0;
                for(var i=0;i<quantities.Length;i++)
                {
                    var draft=await sales.CreateDraftAsync(new SaleDraftCommand{CustomerId=customer,WarehouseId=1,InvoiceDate=DateTime.Now.ToString("yyyy-MM-dd"),PaymentMode="credit",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity=quantities[i],UnitPrice="200"}}},1,CancellationToken.None);
                    var posted=await sales.PostAsync(draft.Id,"weight-sale-"+i,1,CancellationToken.None);
                    if(i==1)secondInvoice=posted.Invoice.Id;if(i==2)thirdInvoice=posted.Invoice.Id;
                    Assert.True(posted.Succeeded,posted.ErrorMessage);Assert.Equal(amounts[i],posted.Invoice.TotalScaled);Assert.Equal(quantities[i].TrimEnd('0').TrimEnd('.'),posted.Invoice.Lines.Single().Quantity);Assert.Equal("weight",posted.Invoice.Lines.Single().SellingType);Assert.Equal("كجم",posted.Invoice.Lines.Single().UnitName);
                    if(i==1)
                    {
                        var settingsStore=new SqliteSettingsStore(cs,path);
                        var document=await new PrintDocumentProvider(sales,null,null,null,new SettingsService(settingsStore,settingsStore),null).GetAsync("sale",posted.Invoice.Id,1,true,CancellationToken.None);
                        Assert.Equal("0.5",document.Items.Single().Quantity);Assert.Contains("كجم",document.Items.Single().Variant);Assert.Equal("200.00",document.Items.Single().UnitPrice);Assert.Equal("100.00",document.Items.Single().Total);
                    }
                    var replay=await sales.PostAsync(draft.Id,"weight-sale-"+i,1,CancellationToken.None);Assert.True(replay.Succeeded);Assert.Equal(posted.Invoice.Id,replay.Invoice.Id);
                    if(i==0)
                    {
                        var returned=await sales.ReturnAsync(draft.Id,new SaleReturnCommand{OperationKey="weight-return",Reason="مرتجع",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=posted.Invoice.Lines.Single().Id,Quantity="0.250"}}},1,CancellationToken.None);
                        Assert.True(returned.Succeeded,returned.ErrorMessage);
                    }
                }
                var editable=await sales.GetForEditAsync(secondInvoice,1,CancellationToken.None);Assert.True(editable.CanEditPosted,editable.EditRestrictionMessage);
                var correction=new SaleCorrectionCommand{ExpectedVersion=editable.Version,OperationKey="weight-correction",SettlementKind="customer_credit",Invoice=new SaleDraftCommand{CustomerId=customer,WarehouseId=1,InvoiceDate=DateTime.Now.ToString("yyyy-MM-dd"),PaymentMode="credit",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="0.350",UnitPrice="200"}}}};
                var corrected=await sales.CorrectAsync(secondInvoice,correction,1,CancellationToken.None);Assert.True(corrected.Succeeded,corrected.ErrorMessage);Assert.Equal(700000,corrected.Invoice.TotalScaled);
                Assert.True((await sales.CorrectAsync(secondInvoice,correction,1,CancellationToken.None)).Replayed);
                var canceled=await sales.CancelAsync(thirdInvoice,"weight-cancel","إلغاء فاتورة موزونة",1,CancellationToken.None);Assert.True(canceled.Succeeded,canceled.ErrorMessage);
                Assert.True((await sales.CancelAsync(thirdInvoice,"weight-cancel","إلغاء فاتورة موزونة",1,CancellationToken.None)).Replayed);
                var shortDraft=await sales.CreateDraftAsync(new SaleDraftCommand{CustomerId=customer,WarehouseId=1,InvoiceDate=DateTime.Now.ToString("yyyy-MM-dd"),PaymentMode="credit",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="10.000",UnitPrice="200"}}},1,CancellationToken.None);
                var shortSale=await sales.PostAsync(shortDraft.Id,"weight-short-sale",1,CancellationToken.None);Assert.True(shortSale.Succeeded,shortSale.ErrorMessage);Assert.Equal(20000000,shortSale.Invoice.TotalScaled);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(0,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));Assert.Equal(-9650000,c.ExecuteScalar<long>("SELECT COALESCE(SUM(QuantityBaseScaled),0) FROM StockMovements WHERE DocumentId=@Id",new{Id=shortSale.Invoice.InventoryDocumentId}));}
                var shortReturn=await sales.ReturnAsync(shortDraft.Id,new SaleReturnCommand{OperationKey="weight-short-return",Reason="إرجاع وزن",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=shortSale.Invoice.Lines.Single().Id,Quantity="10.000"}}},1,CancellationToken.None);Assert.True(shortReturn.Succeeded,shortReturn.ErrorMessage);
                using(var c=new SQLiteConnection(cs))
                {
                    c.Open();Assert.Equal(9650000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));
                    Assert.Equal(9650000,c.ExecuteScalar<long>("SELECT SUM(RemainingQuantityScaled) FROM InventoryCostLayers WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
                Assert.True(await inventory.VerifyProjectionAsync(CancellationToken.None));
                var today=DateTime.Now.ToString("yyyy-MM-dd");
                // Invoice dates and 'today' above use the local business day, including
                // the interval after local midnight but before UTC midnight.
                var productReport=await new StockReportService(new SqliteStockReportStore(cs),TimeZoneInfo.Local).QueryAsync("product-profit",new StockReportQuery{From=today,To=today},CancellationToken.None);
                Assert.Contains(productReport.Items,x=>x.ProductId==weighted.Value.Id && x.Quantity=="0.35" && x.Unit=="كجم");
                Migrator.Run(cs);Assert.Equal("weight",(await new CatalogService(new SqliteCatalogStore(cs)).GetProductAsync(weighted.Value.Id,CancellationToken.None)).Value.SellingType);
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
    }
}
