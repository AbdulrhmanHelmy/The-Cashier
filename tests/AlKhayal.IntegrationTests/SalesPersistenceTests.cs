using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Features;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Workforce;
using AlKhayal.Data.BusinessActivities;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Customers;
using AlKhayal.Data.Database;
using AlKhayal.Data.Features;
using AlKhayal.Data.Inventory;
using AlKhayal.Data.Sales;
using AlKhayal.Data.Workforce;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class SalesPersistenceTests
    {
        private const string NoCancellation = "";
        [Fact]
        public async Task Expiry_receipts_sell_fifo_and_partial_returns_restore_latest_invoice_expiry()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);
                using(var c=new SQLiteConnection(cs))
                {
                    c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('expiry-sale-admin','test-only','2026-01-01T00:00:00Z')");
                    c.Execute("INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('EXP-RET','عميل الصلاحية','0100','',0,10000,'أساسي','نشط','2026-01-01','2026-01-01')");
                }
                await OpenShift(cs);
                var catalog=new CatalogService(new SqliteCatalogStore(cs));
                var created=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="expiry-sale-product",Name="صنف صلاحية تلقائية",BaseUnitId=1,ProductType="stock",SalePrice="12",PurchasePrice="3",IsActive=true},CancellationToken.None);
                Assert.True(created.Succeeded,created.ErrorMessage);
                long variant,unit,customer;
                using(var c=new SQLiteConnection(cs))
                {
                    c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=created.Value.Id});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=created.Value.Id});customer=c.ExecuteScalar<long>("SELECT Id FROM Customers WHERE Code='EXP-RET'");
                    c.Execute("INSERT INTO ProductCapabilityAssignments(ProductId,CapabilityKey,IsEnabled,AssignedUtc,UpdatedUtc,ChangedByUserId) VALUES(@Id,'expiry-dates',1,'2026-01-01','2026-01-01',1)",new{Id=created.Value.Id});
                }
                var first=DateTime.Today.AddDays(30).ToString("yyyy-MM-dd");var second=DateTime.Today.AddDays(60).ToString("yyyy-MM-dd");
                var receipt=new InventoryPostCommand{OperationKey="expiry-two-receipts",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد",Lines=new[]{
                    new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity="10",UnitCost="3",SalePrice="12",ExpirationDate=first},
                    new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity="20",UnitCost="5",SalePrice="12",ExpirationDate=second}}};
                var inventory=new SqliteInventoryStore(cs);
                var received=await inventory.PostAsync(receipt,InventoryService.CanonicalHashPost(receipt),CancellationToken.None);Assert.True(received.Succeeded,received.ErrorMessage);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM ProductBatches WHERE VariantId=@Variant",new{Variant=variant}));}
                var sales=new SalesService(new SqliteSalesStore(cs));
                var draft=await sales.CreateDraftAsync(new SaleDraftCommand{CustomerId=customer,WarehouseId=1,InvoiceDate=DateTime.Today.ToString("yyyy-MM-dd"),PaymentMode="credit",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="15",UnitPrice="12"}}},1,CancellationToken.None);
                var posted=await sales.PostAsync(draft.Id,"expiry-sale",1,CancellationToken.None);Assert.True(posted.Succeeded,posted.ErrorMessage);
                var invoiceLine=posted.Invoice.Lines.Single();
                using(var c=new SQLiteConnection(cs))
                {
                    c.Open();var issues=c.Query<(string ExpirationDate,long QuantityBaseScaled)>(@"SELECT b.ExpirationDate,s.QuantityBaseScaled FROM SalesInvoiceStockEntries s JOIN InventoryDocumentLines l ON l.Id=s.InventoryDocumentLineId JOIN ProductBatches b ON b.Id=l.BatchId WHERE s.SalesInvoiceLineId=@Id ORDER BY b.ExpirationDate",new{Id=invoiceLine.Id}).ToList();
                    Assert.Equal(new long[]{10000000,5000000},issues.Select(x=>x.QuantityBaseScaled).ToArray());
                    Assert.Equal(new[]{first,second},issues.Select(x=>x.ExpirationDate).ToArray());
                }
                var r1=await sales.ReturnAsync(draft.Id,new SaleReturnCommand{OperationKey="expiry-return-1",Reason="إرجاع جزئي",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=invoiceLine.Id,Quantity="3"}}},1,CancellationToken.None);Assert.True(r1.Succeeded,r1.ErrorMessage);
                var r2=await sales.ReturnAsync(draft.Id,new SaleReturnCommand{OperationKey="expiry-return-2",Reason="إرجاع جزئي",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=invoiceLine.Id,Quantity="7"}}},1,CancellationToken.None);Assert.True(r2.Succeeded,r2.ErrorMessage);
                using(var c=new SQLiteConnection(cs))
                {
                    c.Open();var balances=c.Query<(string ExpirationDate,long QuantityBaseScaled)>("SELECT b.ExpirationDate,i.QuantityBaseScaled FROM InventoryBalances i JOIN ProductBatches b ON b.Id=i.BatchId WHERE i.VariantId=@Variant ORDER BY b.ExpirationDate",new{Variant=variant}).ToList();
                    Assert.Equal(new long[]{5000000,20000000},balances.Select(x=>x.QuantityBaseScaled).ToArray());
                    Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(DISTINCT SourceMovementId) FROM SalesReturnCostAllocations WHERE SalesReturnLineId IN (SELECT Id FROM SalesReturnLines WHERE SalesInvoiceLineId=@Id)",new{Id=invoiceLine.Id}));
                }
                var replay=await sales.ReturnAsync(draft.Id,new SaleReturnCommand{OperationKey="expiry-return-2",Reason="إرجاع جزئي",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=invoiceLine.Id,Quantity="7"}}},1,CancellationToken.None);Assert.True(replay.Replayed);
                var excess=await sales.ReturnAsync(draft.Id,new SaleReturnCommand{OperationKey="expiry-return-over",Reason="زيادة",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=invoiceLine.Id,Quantity="6"}}},1,CancellationToken.None);Assert.Equal("return_quantity_exceeded",excess.ErrorCode);
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
        [Fact]
        public async Task Installment_stock_policy_rolls_back_shortage_or_consumes_only_real_fifo_stock()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('inst-stock-admin','test-only','2026-01-01T00:00:00Z'); INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('INST-STOCK','عميل','0100','',0,10000,'أساسي','نشط','2026-01-01','2026-01-01')");}await OpenShift(cs);
                var catalog=new CatalogService(new SqliteCatalogStore(cs));var service=new SalesService(new SqliteSalesStore(cs));
                foreach(var allow in new[]{false,true})
                {
                    var product=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="inst-stock-"+allow,Name=allow?"مسموح":"ممنوع",BaseUnitId=1,ProductType="stock",SalePrice="15",IsActive=true,AllowSaleWhenOutOfStock=allow},CancellationToken.None);Assert.True(product.Succeeded,product.ErrorMessage);
                    long variant,unit,customer;using(var c=new SQLiteConnection(cs)){c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=product.Value.Id});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=product.Value.Id});customer=c.ExecuteScalar<long>("SELECT Id FROM Customers WHERE Code='INST-STOCK'");}
                    var receipt=await new SqliteInventoryStore(cs).PostAsync(new InventoryPostCommand{OperationKey="inst-stock-receipt-"+allow,Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity="5",UnitCost="3",SalePrice="15"}}},"receipt-"+allow,CancellationToken.None);Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                    var command=new InstallmentSaleCommand{Invoice=new SaleDraftCommand{CustomerId=customer,WarehouseId=1,InvoiceDate="2026-11-09",PaymentMode="credit",ImmediateMethod="credit",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="12",UnitPrice="15.00"}}},DownPayment="0.00",InstallmentCount=3,IntervalMonths=1,FirstDueOn="2026-12-09",OperationKey="inst-short-"+allow};
                    var result=await service.CreateInstallmentAsync(command,1,CancellationToken.None);
                    using(var c=new SQLiteConnection(cs))
                    {
                        c.Open();Assert.Equal(allow,result.Succeeded);Assert.Equal(allow?0:5000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));Assert.Equal(allow?1:0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerInstallmentContracts WHERE OperationKey=@Key",new{Key=command.OperationKey}));
                        if(allow){Assert.Equal(150000,c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM InventoryCostAllocations WHERE MovementId IN (SELECT Id FROM StockMovements WHERE DocumentId=@Id)",new{Id=result.Invoice.InventoryDocumentId}));Assert.Equal(180m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=customer}));}
                        else {Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesInvoices WHERE Status='posted'"));Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=customer}));}
                    }
                }
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
        [Fact]
        public async Task Installment_cartons_post_one_invoice_contract_debt_and_fifo_then_collect_without_stock_change()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);
                using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('installment-admin','test-only','2026-01-01T00:00:00Z'); INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('INST-A','عميل تقسيط','0100','',0,1000,'أساسي','نشط','2026-01-01','2026-01-01')");}
                await OpenShift(cs);
                var catalog=new SqliteCatalogStore(cs);var carton=await catalog.SaveUnitAsync(new SaveUnitCommand{Code="INST-CARTON",Name="كرتونة",Symbol="كرتونة",IsActive=true},"INST-CARTON",CancellationToken.None);
                var product=await catalog.CreateProductAsync(new CreateProductRecord{OperationKey="inst-product",RequestHash="inst-product-hash",Name="مياه",BaseUnitId=1,ProductType="stock",SellPriceScaled=80000,IsActive=true,UserId=1,Configuration=new ProductConfigurationRecord{CapabilityKeys=new[]{"multiple-units"},Units=new[]{new ProductUnitConfigRecord{UnitId=1,ToBaseNumerator=1,ToBaseDenominator=1,IsBase=true,IsSaleUnit=true,IsPurchaseUnit=true,IsActive=true},new ProductUnitConfigRecord{UnitId=carton.Id,ToBaseNumerator=12,ToBaseDenominator=1,SellPriceScaled=900000,IsSaleUnit=true,IsPurchaseUnit=true,IsActive=true}}}},CancellationToken.None);
                long variant,baseUnit,cartonUnit,customer;
                using(var c=new SQLiteConnection(cs)){c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=product.Id});baseUnit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=product.Id});cartonUnit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=0",new{Id=product.Id});customer=c.ExecuteScalar<long>("SELECT Id FROM Customers WHERE Code='INST-A'");}
                var inventory=new SqliteInventoryStore(cs);
                var receipt=await inventory.PostAsync(new InventoryPostCommand{OperationKey="inst-receipt-a",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=baseUnit,Quantity="20",UnitCost="5",SalePrice="8"}}},"inst-receipt-a-hash",CancellationToken.None);Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                receipt=await inventory.PostAsync(new InventoryPostCommand{OperationKey="inst-receipt-b",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=baseUnit,Quantity="10",UnitCost="6",SalePrice="8"}}},"inst-receipt-b-hash",CancellationToken.None);Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                var account=CollectionTestAccounts.Account(cs,"cash");
                var command=new InstallmentSaleCommand{Invoice=new SaleDraftCommand{CustomerId=customer,WarehouseId=1,InvoiceDate="2026-11-09",PaymentMode="credit",ImmediateMethod="credit",Discount="0.00",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=cartonUnit,Quantity="2",UnitPrice="90.00",Discount="0.00"}}},DownPayment="20.00",InstallmentCount=4,IntervalMonths=1,FirstDueOn="2026-12-09",TreasuryAccountId=account,OperationKey="inst-post-1"};
                var service=new SalesService(new SqliteSalesStore(cs));var posted=await service.CreateInstallmentAsync(command,1,CancellationToken.None);Assert.True(posted.Succeeded,posted.ErrorMessage);Assert.Equal(1800000,posted.Invoice.TotalScaled);Assert.Equal(200000,posted.Invoice.PaidScaled);
                Assert.Equal(12,posted.Invoice.Lines.Single().ToBaseNumerator);Assert.Equal(1,posted.Invoice.Lines.Single().ToBaseDenominator);Assert.Equal("90.00",posted.Invoice.Lines.Single().UnitPrice);
                var replay=await service.CreateInstallmentAsync(command,1,CancellationToken.None);Assert.True(replay.Succeeded,replay.ErrorMessage);Assert.True(replay.Replayed);Assert.Equal(posted.Invoice.Id,replay.Invoice.Id);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(6000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));Assert.Equal(160m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=customer}));Assert.Equal(4,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerInstallmentSchedule WHERE ContractId=@Id",new{Id=posted.ContractId}));Assert.Equal(1600000,c.ExecuteScalar<long>("SELECT SUM(AmountScaled) FROM CustomerInstallmentSchedule WHERE ContractId=@Id",new{Id=posted.ContractId}));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE SalesInvoiceId=@Id AND TransactionType='increase'",new{Id=posted.Invoice.Id}));Assert.Equal(1240000,c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM InventoryCostAllocations WHERE MovementId IN (SELECT Id FROM StockMovements WHERE DocumentId=@Id)",new{Id=posted.Invoice.InventoryDocumentId}));Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));}
                var paid=new SqliteCustomerInstallmentRepository(cs).Pay(customer,posted.ContractId,1,"2026-12-09","inst-pay-1","pay-hash","cash",account,1);Assert.Equal("مسدد",paid.Schedule[0].Status);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(120m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=customer}));Assert.Equal(6000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));Assert.Equal(600000,c.ExecuteScalar<long>("SELECT PaidScaled FROM SalesInvoices WHERE Id=@Id",new{Id=posted.Invoice.Id}));}
                var installments=new SqliteCustomerInstallmentRepository(cs);
                var partial=installments.Pay(customer,posted.ContractId,2,"2027-01-09","inst-pay-partial","partial-hash","cash",account,1,100000);Assert.Equal("جزئي",partial.Schedule[1].Status);Assert.Equal(30m,partial.Schedule[1].RemainingAmount);
                var partialReplay=installments.Pay(customer,posted.ContractId,2,"2027-01-09","inst-pay-partial","partial-hash","cash",account,1,100000);Assert.Equal(30m,partialReplay.Schedule[1].RemainingAmount);
                var completed=installments.Pay(customer,posted.ContractId,2,"2027-01-10","inst-pay-rest","rest-hash","cash",account,1);Assert.Equal("مسدد",completed.Schedule[1].Status);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(80m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=customer}));Assert.Equal(1000000,c.ExecuteScalar<long>("SELECT PaidScaled FROM SalesInvoices WHERE Id=@Id",new{Id=posted.Invoice.Id}));Assert.Equal(6000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerInstallmentPartialPayments WHERE ContractId=@Id",new{Id=posted.ContractId}));}
                var report=await OperationalReportTests.Service(cs).QueryAsync("installments",new AlKhayal.Application.Reports.OperationalReportQuery{From="2026-11-01",To="2027-02-28"},CancellationToken.None);
                Assert.Equal("80.0000",report.Summary["balanceScaled"]);
                Assert.Throws<ArgumentException>(()=>installments.Pay(customer,posted.ContractId,3,"2027-02-09","inst-overpay","overpay-hash","cash",account,1,410000));
                installments.Pay(customer,posted.ContractId,3,"2027-02-09","inst-pay-3","pay-3-hash","cash",account,1);
                var settled=installments.Pay(customer,posted.ContractId,4,"2027-03-09","inst-pay-4","pay-4-hash","cash",account,1);Assert.Equal("مكتمل",settled.Status);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=customer}));Assert.Equal(1800000,c.ExecuteScalar<long>("SELECT PaidScaled FROM SalesInvoices WHERE Id=@Id",new{Id=posted.Invoice.Id}));Assert.Equal(6000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));}
                var failed=new InstallmentSaleCommand{Invoice=new SaleDraftCommand{CustomerId=customer,WarehouseId=1,InvoiceDate="2026-11-09",PaymentMode="credit",ImmediateMethod="credit",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=baseUnit,Quantity="1",UnitPrice="8.00"}}},DownPayment="1.00",InstallmentCount=1,IntervalMonths=1,FirstDueOn="2026-12-09",TreasuryAccountId=999999,OperationKey="inst-invalid-account"};
                var failure=await service.CreateInstallmentAsync(failed,1,CancellationToken.None);Assert.False(failure.Succeeded);Assert.StartsWith("treasury_",failure.ErrorCode);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesInvoices WHERE OperationKey='inst-invalid-account'"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerInstallmentContracts WHERE OperationKey='inst-invalid-account'"));Assert.Equal(6000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));}
                using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("CREATE TRIGGER FailInstallmentSchedule BEFORE INSERT ON CustomerInstallmentSchedule BEGIN SELECT RAISE(ABORT,'forced_schedule_failure'); END;");}
                failed.OperationKey="inst-failed-schedule";failed.TreasuryAccountId=account;
                var scheduleFailure=await service.CreateInstallmentAsync(failed,1,CancellationToken.None);Assert.False(scheduleFailure.Succeeded);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesInvoices WHERE OperationKey='inst-failed-schedule'"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerInstallmentContracts WHERE OperationKey='inst-failed-schedule'"));Assert.Equal(6000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));}
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
        [Fact]
        public async Task Saved_draft_can_be_resubmitted_with_a_changed_quantity_before_posting()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('draft-admin','test-only','2026-01-01T00:00:00Z')");}await OpenShift(cs);
                var catalog=new CatalogService(new SqliteCatalogStore(cs));var product=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="draft-product",Name="مسودة قابلة للتعديل",BaseUnitId=1,ProductType="stock",SalePrice="12",PurchasePrice="9",IsActive=true},CancellationToken.None);Assert.True(product.Succeeded,product.ErrorMessage);
                long variant,unit;using(var c=new SQLiteConnection(cs)){c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=product.Value.Id});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=product.Value.Id});}
                var inventory=new SqliteInventoryStore(cs);var receipt=await inventory.PostAsync(new InventoryPostCommand{OperationKey="draft-receipt",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity="5",UnitCost="9",SalePrice="12"}}},"draft-receipt-hash",CancellationToken.None);Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                var service=new SalesService(new SqliteSalesStore(cs));var draft=await service.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Discount="0.00",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitPrice="12",Discount="0.00"}}},1,CancellationToken.None);
                Assert.Equal(1,await service.CountSuspendedAsync(CancellationToken.None));
                Assert.Equal(1,await new SalesService(new SqliteSalesStore(cs)).CountSuspendedAsync(CancellationToken.None));
                draft.Lines.Single().Quantity="2";
                var updated=await service.UpdateDraftAsync(draft.Id,new SaleDraftCommand{WarehouseId=draft.WarehouseId,InvoiceDate=draft.InvoiceDate,PaymentMode=draft.PaymentMode,Discount="0.00",Lines=draft.Lines},1,CancellationToken.None);
                Assert.Equal("2",updated.Lines.Single().Quantity);Assert.Equal(240000,updated.TotalScaled);Assert.Equal(1,await service.CountSuspendedAsync(CancellationToken.None));
                var posted=await service.PostAsync(updated.Id,"updated-draft-post",1,CancellationToken.None);Assert.True(posted.Succeeded,posted.ErrorMessage);Assert.Equal("2",posted.Invoice.Lines.Single().Quantity);Assert.Equal(0,await service.CountSuspendedAsync(CancellationToken.None));
                var later=await service.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Discount="0.00",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitPrice="12",Discount="0.00"}}},1,CancellationToken.None);
                Assert.Equal(1,await service.CountSuspendedAsync(CancellationToken.None));
                var deleted=await service.DeleteDraftAsync(later.Id,"delete-suspended-count",1,CancellationToken.None);Assert.True(deleted.Succeeded,deleted.ErrorMessage);
                Assert.Equal(0,await service.CountSuspendedAsync(CancellationToken.None));
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task Product_unit_prices_and_quantity_tiers_are_validated_and_saved_on_sales_lines()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('unit-price-admin','test-only','2026-01-01T00:00:00Z')");}await OpenShift(cs);
                var catalog=new SqliteCatalogStore(cs);var pack=await catalog.SaveUnitAsync(new SaveUnitCommand{Code="CARTON",Name="كرتونة",Symbol="كرتونة",IsActive=true},"CARTON",CancellationToken.None);
                var created=await catalog.CreateProductAsync(new CreateProductRecord{OperationKey="unit-price-product",RequestHash="unit-price-hash",Name="زيت طعام",BaseUnitId=1,ProductType="stock",SellPriceScaled=300000,IsActive=true,UserId=1,Configuration=new ProductConfigurationRecord{CapabilityKeys=new[]{"multiple-units"},Units=new[]{new ProductUnitConfigRecord{UnitId=1,ToBaseNumerator=1,ToBaseDenominator=1,IsBase=true,IsSaleUnit=true,IsPurchaseUnit=true,IsActive=true},new ProductUnitConfigRecord{UnitId=pack.Id,ToBaseNumerator=12,ToBaseDenominator=1,SellPriceScaled=3300000,IsSaleUnit=true,IsPurchaseUnit=true,IsActive=true}}}},CancellationToken.None);
                long variant,baseUnit,packProductUnit;using(var c=new SQLiteConnection(cs)){c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id AND IsDefault=1",new{Id=created.Id});baseUnit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=created.Id});packProductUnit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND UnitId=@Unit",new{Id=created.Id,Unit=pack.Id});}
                var sales=new SalesService(new SqliteSalesStore(cs));var draft=await sales.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=baseUnit,Quantity="13",UnitPrice="27.50"},new SaleLine{VariantId=variant,ProductUnitId=packProductUnit,Quantity="2",UnitPrice="330.00"}}},1,CancellationToken.None);
                Assert.Equal(10175000,draft.TotalScaled);Assert.Equal("27.50",draft.Lines[0].UnitPrice);Assert.Equal("330.00",draft.Lines[1].UnitPrice);
                var manual=await sales.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=baseUnit,Quantity="13",UnitPrice="26.00",ManualPriceOverride=true}}},1,CancellationToken.None);
                Assert.Equal(3380000,manual.TotalScaled);Assert.True(manual.Lines[0].ManualPriceOverride);
                var detail=await catalog.GetProductDetailAsync(created.Id,CancellationToken.None);Assert.Equal(3300000,detail.Units.Single(x=>x.Id==packProductUnit).SellPriceScaled);Assert.Equal(300000,detail.Product.SellPriceScaled);
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task Fractional_pack_rate_posts_exact_total_and_partial_returns_reconcile()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('fractional-pack','test-only','2026-01-01T00:00:00Z'); INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('FRACTIONAL','عميل','0100','',0,1000,'أساسي','نشط','2026-01-01','2026-01-01')");}await OpenShift(cs);
                var catalog=new SqliteCatalogStore(cs);var pack=await catalog.SaveUnitAsync(new SaveUnitCommand{Code="THREE",Name="كرتونة",Symbol="كرتونة",IsActive=true},"THREE",CancellationToken.None);
                var product=await catalog.CreateProductAsync(new CreateProductRecord{OperationKey="fractional-product",RequestHash="fractional-hash",Name="ثلاث زجاجات",BaseUnitId=1,ProductType="stock",SellPriceScaled=400000,IsActive=true,UserId=1,Configuration=new ProductConfigurationRecord{CapabilityKeys=new[]{"multiple-units"},Units=new[]{new ProductUnitConfigRecord{UnitId=1,ToBaseNumerator=1,ToBaseDenominator=1,IsBase=true,IsSaleUnit=true,IsPurchaseUnit=true,IsActive=true},new ProductUnitConfigRecord{UnitId=pack.Id,ToBaseNumerator=3,ToBaseDenominator=1,SellPriceScaled=1000000,IsSaleUnit=true,IsPurchaseUnit=true,IsActive=true}}}},CancellationToken.None);
                long variant,unit,customer;using(var c=new SQLiteConnection(cs)){c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=product.Id});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=product.Id});customer=c.ExecuteScalar<long>("SELECT Id FROM Customers WHERE Code='FRACTIONAL'");}
                var receipt=await new SqliteInventoryStore(cs).PostAsync(new InventoryPostCommand{OperationKey="fractional-receipt",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity="3",UnitCost="10",SalePrice="40"}}},"fractional-receipt-hash",CancellationToken.None);Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                var sales=new SalesService(new SqliteSalesStore(cs));var draft=await sales.CreateDraftAsync(new SaleDraftCommand{CustomerId=customer,WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="credit",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="3",UnitPrice="33.33"}}},1,CancellationToken.None);
                Assert.Equal(1000000,draft.TotalScaled);Assert.Equal(1000000,draft.Lines.Single().LineTotalScaled);
                var posted=await sales.PostAsync(draft.Id,"fractional-sale",1,CancellationToken.None);Assert.True(posted.Succeeded,posted.ErrorMessage);Assert.Equal(1000000,posted.Invoice.TotalScaled);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(0,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));}
                var lineId=posted.Invoice.Lines.Single().Id;var first=await sales.ReturnAsync(draft.Id,new SaleReturnCommand{OperationKey="fractional-return-1",Reason="إرجاع",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=lineId,Quantity="1"}}},1,CancellationToken.None);Assert.True(first.Succeeded,first.ErrorMessage);
                var second=await sales.ReturnAsync(draft.Id,new SaleReturnCommand{OperationKey="fractional-return-2",Reason="إرجاع",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=lineId,Quantity="2"}}},1,CancellationToken.None);Assert.True(second.Succeeded,second.ErrorMessage);Assert.Equal(1000000,first.TotalScaled+second.TotalScaled);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(3000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=customer}));}
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task Historical_carton_return_uses_posted_twelve_unit_snapshot_after_catalog_ratio_changes()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('historical-unit','test-only','2026-01-01T00:00:00Z'); INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('HIST-UNIT','عميل','0100','',0,1000,'أساسي','نشط','2026-01-01','2026-01-01')");}await OpenShift(cs);
                var catalog=new SqliteCatalogStore(cs);var carton=await catalog.SaveUnitAsync(new SaveUnitCommand{Code="HIST-CARTON",Name="كرتونة",Symbol="كرتونة",IsActive=true},"HIST-CARTON",CancellationToken.None);
                var product=await catalog.CreateProductAsync(new CreateProductRecord{OperationKey="historical-unit-product",RequestHash="historical-unit-hash",Name="زيت تاريخي",BaseUnitId=1,ProductType="stock",SellPriceScaled=300000,IsActive=true,UserId=1,Configuration=new ProductConfigurationRecord{CapabilityKeys=new[]{"multiple-units"},Units=new[]{new ProductUnitConfigRecord{UnitId=1,ToBaseNumerator=1,ToBaseDenominator=1,IsBase=true,IsSaleUnit=true,IsPurchaseUnit=true,IsActive=true},new ProductUnitConfigRecord{UnitId=carton.Id,ToBaseNumerator=12,ToBaseDenominator=1,SellPriceScaled=3300000,IsSaleUnit=true,IsPurchaseUnit=true,IsActive=true}}}},CancellationToken.None);
                long variant,baseUnit,cartonUnit,customer;using(var c=new SQLiteConnection(cs)){c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=product.Id});baseUnit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=product.Id});cartonUnit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=0",new{Id=product.Id});customer=c.ExecuteScalar<long>("SELECT Id FROM Customers WHERE Code='HIST-UNIT'");}
                var receipt=await new SqliteInventoryStore(cs).PostAsync(new InventoryPostCommand{OperationKey="historical-unit-receipt",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=baseUnit,Quantity="12",UnitCost="10",SalePrice="30"}}},"historical-unit-receipt-hash",CancellationToken.None);Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                var sales=new SalesService(new SqliteSalesStore(cs));var draft=await sales.CreateDraftAsync(new SaleDraftCommand{CustomerId=customer,WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="credit",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=cartonUnit,Quantity="1",UnitPrice="330.00"}}},1,CancellationToken.None);
                var posted=await sales.PostAsync(draft.Id,"historical-unit-sale",1,CancellationToken.None);Assert.True(posted.Succeeded,posted.ErrorMessage);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(0,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));Assert.Equal(12,c.ExecuteScalar<long>("SELECT ToBaseNumerator FROM InventoryDocumentLines WHERE Id=@Id",new{Id=posted.Invoice.Lines.Single().InventoryDocumentLineId}));c.Execute("UPDATE ProductUnits SET ToBaseNumerator=24 WHERE Id=@Id",new{Id=cartonUnit});}
                var returned=await sales.ReturnAsync(draft.Id,new SaleReturnCommand{OperationKey="historical-unit-return",Reason="إرجاع",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=posted.Invoice.Lines.Single().Id,Quantity="1"}}},1,CancellationToken.None);Assert.True(returned.Succeeded,returned.ErrorMessage);Assert.Equal(3300000,returned.TotalScaled);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(12000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));Assert.Equal(24,c.ExecuteScalar<long>("SELECT ToBaseNumerator FROM ProductUnits WHERE Id=@Id",new{Id=cartonUnit}));}
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task Credit_sale_payment_and_fifo_return_update_customer_and_stock_atomically()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('return-admin','test-only','2026-01-01T00:00:00Z')");c.Execute("INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('C-RET','عميل مرتجعات','0100','',0,1000,'أساسي','نشط','2026-01-01','2026-01-01')");}await OpenShift(cs);
                var catalog=new CatalogService(new SqliteCatalogStore(cs));var created=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="return-product",Name="صنف إرجاع",BaseUnitId=1,ProductType="stock",SalePrice="12",PurchasePrice="9",IsActive=true},CancellationToken.None);Assert.True(created.Succeeded,created.ErrorMessage);
                long variant,unit,customer;using(var c=new SQLiteConnection(cs)){c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=created.Value.Id});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=created.Value.Id});customer=c.ExecuteScalar<long>("SELECT Id FROM Customers WHERE Code='C-RET'");}
                var inventory=new SqliteInventoryStore(cs);var receipt=await inventory.PostAsync(new InventoryPostCommand{OperationKey="return-receipt",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity="5",UnitCost="9",SalePrice="12"}}},"return-receipt-hash",CancellationToken.None);Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                var service=new SalesService(new SqliteSalesStore(cs));var invoice=await service.CreateDraftAsync(new SaleDraftCommand{CustomerId=customer,WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="credit",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitPrice="12"}}},1,CancellationToken.None);
                var posted=await service.PostAsync(invoice.Id,"credit-sale-key",1,CancellationToken.None);Assert.True(posted.Succeeded,posted.ErrorMessage);Assert.Equal(120000,posted.Invoice.TotalScaled);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(120000,c.ExecuteScalar<long>("SELECT SUM(AmountScaled) FROM CustomerTransactions WHERE SalesInvoiceId=@Id AND TransactionType='increase'",new{Id=invoice.Id}));Assert.Equal(12m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=customer}));}
                var paid=await service.AddPaymentAsync(invoice.Id,new SalePaymentCommand{Amount="5.00",Method="cash",OperationKey="collection-1",OccurredOn="2026-10-25",TreasuryAccountId=CollectionTestAccounts.Account(cs,"cash")},1,CancellationToken.None);Assert.True(paid.Succeeded,paid.ErrorMessage);Assert.Equal(50000,paid.Invoice.PaidScaled);
                var paymentReplay=await service.AddPaymentAsync(invoice.Id,new SalePaymentCommand{Amount="5.00",Method="cash",OperationKey="collection-1",OccurredOn="2026-10-25",TreasuryAccountId=CollectionTestAccounts.Account(cs,"cash")},1,CancellationToken.None);Assert.True(paymentReplay.Succeeded);Assert.True(paymentReplay.Replayed);
                // This legacy invoice-collection path records customer money only; fund the sales-returns cashbox explicitly.
                var treasury=new AlKhayal.Application.Treasury.TreasuryService(new AlKhayal.Data.Treasury.SqliteTreasuryStore(cs));
                var refundSource=await treasury.SaveAsync(null,new AlKhayal.Application.Treasury.TreasuryAccountCommand{Code="REFUND-SOURCE",Name="مرتجعات البيع",Type="cash"},1,CancellationToken.None);
                await treasury.PostAsync(new AlKhayal.Application.Treasury.TreasuryMovementCommand{AccountId=refundSource.Id,Amount="5.00",OperationKey="legacy-collection-funding"},"deposit",1,CancellationToken.None);
                var line=paid.Invoice.Lines.Single();var returned=await service.ReturnAsync(invoice.Id,new SaleReturnCommand{OperationKey="return-1",Reason="نصف الكمية",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="0.5"}}},1,CancellationToken.None);Assert.True(returned.Succeeded,returned.ErrorMessage);Assert.Equal(60000,returned.TotalScaled);Assert.Equal(0,returned.CashRefundScaled);Assert.Equal(60000,returned.CreditReductionScaled);
                var returnReplay=await service.ReturnAsync(invoice.Id,new SaleReturnCommand{OperationKey="return-1",Reason="نصف الكمية",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="0.5"}}},1,CancellationToken.None);Assert.True(returnReplay.Succeeded);Assert.True(returnReplay.Replayed);
                var over=await service.ReturnAsync(invoice.Id,new SaleReturnCommand{OperationKey="return-over",Reason="زيادة",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="0.6"}}},1,CancellationToken.None);Assert.False(over.Succeeded);Assert.Equal("return_quantity_exceeded",over.ErrorCode);
                var finalCollection=await service.AddPaymentAsync(invoice.Id,new SalePaymentCommand{Amount="1.00",Method="transfer",OperationKey="collection-final",OccurredOn="2026-10-25",TreasuryAccountId=CollectionTestAccounts.Account(cs,"transfer")},1,CancellationToken.None);Assert.True(finalCollection.Succeeded,finalCollection.ErrorMessage);Assert.Equal(60000,finalCollection.Invoice.PaidScaled);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(4500000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE VariantId=@Variant AND WarehouseId=1",new{Variant=variant}));Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{Id=customer}));Assert.Equal(60000,c.ExecuteScalar<long>("SELECT PaidScaled FROM SalesInvoices WHERE Id=@Id",new{Id=invoice.Id}));Assert.Equal(45000,c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM SalesReturnCostAllocations"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesReturns"));}
                var statement=new SqliteCustomerAccountRepository(cs).GetStatement(customer,null,null);Assert.Contains(statement.Movements,x=>x.Reference==invoice.InvoiceNumber&&x.Description=="فاتورة بيع");Assert.Contains(statement.Movements,x=>x.Reference==returned.ReturnNumber&&x.Description=="إرجاع فاتورة بيع");
                Migrator.Run(cs);using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion WHERE Name LIKE '%083_delivery_service.sql'"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SalesInvoiceDeliveries'"));Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));}
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task Concurrent_sales_both_post_but_consume_the_last_unit_only_once()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('concurrent-sales','test-only','2026-01-01T00:00:00Z')");}await OpenShift(cs);
                var catalog=new CatalogService(new SqliteCatalogStore(cs));var created=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="concurrent-product",Name="آخر وحدة",BaseUnitId=1,ProductType="stock",SalePrice="12",PurchasePrice="9",IsActive=true},CancellationToken.None);Assert.True(created.Succeeded,created.ErrorMessage);
                long variant,unit;using(var c=new SQLiteConnection(cs)){c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=created.Value.Id});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=created.Value.Id});}
                var inventory=new SqliteInventoryStore(cs);var receipt=await inventory.PostAsync(new InventoryPostCommand{OperationKey="concurrent-receipt",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد وحدة",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitCost="9",SalePrice="12"}}},"concurrent-receipt-hash",CancellationToken.None);Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                var service=new SalesService(new SqliteSalesStore(cs));var first=await service.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitPrice="12"}}},1,CancellationToken.None);var second=await service.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitPrice="12"}}},1,CancellationToken.None);
                var results=await Task.WhenAll(service.PostAsync(first.Id,"concurrent-sale-a",1,CancellationToken.None),service.PostAsync(second.Id,"concurrent-sale-b",1,CancellationToken.None));Assert.All(results,x=>Assert.True(x.Succeeded,x.ErrorMessage));
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(0,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE VariantId=@Variant AND WarehouseId=1",new{Variant=variant}));Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesInvoices WHERE Status='posted'"));Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments WHERE EventType='cash_sale'"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM StockMovements m JOIN SalesInvoices si ON si.InventoryDocumentId=m.DocumentId WHERE si.Id IN (@First,@Second)",new{First=first.Id,Second=second.Id}));}
                await FundRefundCashbox(cs);
                long stockedInvoiceId;using(var c=new SQLiteConnection(cs)){c.Open();stockedInvoiceId=c.ExecuteScalar<long>("SELECT si.Id FROM SalesInvoices si JOIN StockMovements m ON m.DocumentId=si.InventoryDocumentId WHERE si.Id IN (@First,@Second)",new{First=first.Id,Second=second.Id});}
                var posted=results.Single(x=>x.Invoice.Id==stockedInvoiceId).Invoice;var line=posted.Lines.Single();var returns=await Task.WhenAll(
                    service.ReturnAsync(posted.Id,new SaleReturnCommand{OperationKey="concurrent-return-a",Reason="إرجاع متزامن",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="1"}}},1,CancellationToken.None),
                    service.ReturnAsync(posted.Id,new SaleReturnCommand{OperationKey="concurrent-return-b",Reason="إرجاع متزامن",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="1"}}},1,CancellationToken.None));Assert.Single(returns,x=>x.Succeeded);Assert.Single(returns,x=>!x.Succeeded);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(1000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE VariantId=@Variant AND WarehouseId=1",new{Variant=variant}));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesReturns"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments WHERE EventType='refund'"));}
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task Serial_tracked_sale_and_return_restore_the_original_physical_unit()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('serial-admin','test-only','2026-01-01T00:00:00Z')");}await OpenShift(cs);
                var catalog=new CatalogService(new SqliteCatalogStore(cs));var created=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="serial-return-product",Name="جهاز متسلسل",BaseUnitId=1,ProductType="stock",SalePrice="12",PurchasePrice="9",IsActive=true},CancellationToken.None);Assert.True(created.Succeeded,created.ErrorMessage);
                long variant,unit,productId;using(var c=new SQLiteConnection(cs)){c.Open();productId=created.Value.Id;variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=productId});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=productId});var now="2026-10-25T00:00:00Z";c.Execute("INSERT INTO ProductCapabilityAssignments(ProductId,CapabilityKey,IsEnabled,AssignedUtc,UpdatedUtc,ChangedByUserId) VALUES(@Id,'serial-numbers',1,@Now,@Now,1),(@Id,'imei-tracking',1,@Now,@Now,1),(@Id,'batch-tracking',1,@Now,@Now,1),(@Id,'expiry-dates',1,@Now,@Now,1)",new{Id=productId,Now=now});}
                var expiry=DateTime.Today.AddDays(30).ToString("yyyy-MM-dd");var inventory=new SqliteInventoryStore(cs);var receipt=await inventory.PostAsync(new InventoryPostCommand{OperationKey="serial-return-receipt",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد أجهزة",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitCost="9",SalePrice="12",LotNumber="LOT-SER-RET",ExpirationDate=expiry,PhysicalUnits=new[]{new InventoryUnitInput{Serial="SER-RET-001",Imeis=new[]{"490154203237518","356938035643809"}}}}}},"serial-return-receipt-hash",CancellationToken.None);Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                var service=new SalesService(new SqliteSalesStore(cs));var invoice=await service.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitPrice="12",BatchLotNumber="LOT-SER-RET",ExpirationDate=expiry,PhysicalUnits=new(){new SaleTrackingUnit{Serial="SER-RET-001",Imeis=new(){"490154203237518","356938035643809"}}}}}},1,CancellationToken.None);
                var posted=await service.PostAsync(invoice.Id,"serial-sale-key",1,CancellationToken.None);Assert.True(posted.Succeeded,posted.ErrorMessage);Assert.Single(posted.Invoice.Lines.Single().AvailableTrackingUnits);
                await FundRefundCashbox(cs);
                var line=posted.Invoice.Lines.Single();var returned=await service.ReturnAsync(invoice.Id,new SaleReturnCommand{OperationKey="serial-return-key",Reason="استبدال جهاز",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="1",Serials=new(){"SER-RET-001"}}}},1,CancellationToken.None);Assert.True(returned.Succeeded,returned.ErrorMessage);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryPhysicalUnits WHERE WarehouseId=1 AND Id=(SELECT PhysicalUnitId FROM InventoryIdentifiers WHERE NormalizedValue='SER-RET-001')"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesReturnPhysicalUnits WHERE Serial='SER-RET-001'"));Assert.Equal(1000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));}
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task Sale_draft_posting_is_atomic_fifo_costed_and_idempotent()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('sales-admin','test-only','2026-01-01T00:00:00Z')");}await OpenShift(cs);
                var catalog=new CatalogService(new SqliteCatalogStore(cs));var product=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="sales-product",Name="اختبار",BaseUnitId=1,ProductType="stock",SalePrice="12",PurchasePrice="9",IsActive=true},CancellationToken.None);Assert.True(product.Succeeded,product.ErrorMessage);
                long variant,unit;using(var c=new SQLiteConnection(cs)){c.Open();variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=product.Value.Id});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=product.Value.Id});}
                var inv=new SqliteInventoryStore(cs);var receipt=await inv.PostAsync(new InventoryPostCommand{OperationKey="sales-receipt",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد اختبار",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity="10",UnitCost="9",SalePrice="12"}}},"v2:sales-receipt",CancellationToken.None);Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("UPDATE ApplicationSettings SET TaxEnabled=1,DefaultTaxRate=15,PricesIncludeTax=0 WHERE Id=1");}
                var svc=new SalesService(new SqliteSalesStore(cs));var draft=await svc.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Discount="1",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="2.5",UnitPrice="12",Discount="0"}}},1,CancellationToken.None);
                Assert.Equal(333500,draft.TotalScaled);Assert.Equal(43500,draft.TaxScaled);Assert.Equal("draft",draft.Status);
                var posted=await svc.PostAsync(draft.Id,"sales-op-1",1,CancellationToken.None);Assert.True(posted.Succeeded,posted.ErrorMessage);Assert.Equal("posted",posted.Invoice.Status);Assert.Equal(333500,posted.Invoice.PaidScaled);Assert.Equal(333500,posted.Invoice.Lines.Single().LineTotalScaled);Assert.Equal(43500,posted.Invoice.Lines.Single().TaxScaled);
                var replay=await svc.PostAsync(draft.Id,"sales-op-1",1,CancellationToken.None);Assert.True(replay.Succeeded);Assert.Equal(draft.InvoiceNumber,replay.Invoice.InvoiceNumber);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(7500000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));Assert.Equal(225000,c.ExecuteScalar<long>("SELECT SUM(a.ValueScaled) FROM InventoryCostAllocations a JOIN StockMovements m ON m.Id=a.MovementId JOIN InventoryDocumentLines dl ON dl.Id=m.DocumentLineId JOIN InventoryDocuments d ON d.Id=dl.DocumentId WHERE d.DocumentType='manual_issue'"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesInvoices WHERE Status='posted'"));}
                var oversell=await svc.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="100",UnitPrice="12",Discount="0"}}},1,CancellationToken.None);
                // Sales floor stock at zero: the oversell posts, consumes only the 7.5 units
                // actually on hand at their real FIFO cost, and charges the full sold quantity.
                var floored=await svc.PostAsync(oversell.Id,"sales-op-oversell",1,CancellationToken.None);Assert.True(floored.Succeeded,floored.ErrorMessage);Assert.Equal("posted",floored.Invoice.Status);Assert.Equal("100",floored.Invoice.Lines.Single().Quantity);Assert.Equal(13800000,floored.Invoice.TotalScaled);
                using(var c=new SQLiteConnection(cs)){c.Open();
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT ValueScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));
                    Assert.Equal("-7500000|-675000",string.Join("|",c.Query<string>("SELECT CAST(m.QuantityBaseScaled AS TEXT)||'|'||CAST(m.ValueScaled AS TEXT) FROM StockMovements m JOIN SalesInvoices si ON si.InventoryDocumentId=m.DocumentId WHERE si.Id=@Id",new{Id=oversell.Id})));
                    Assert.Equal(900000,c.ExecuteScalar<long>("SELECT SUM(a.ValueScaled) FROM InventoryCostAllocations a JOIN StockMovements m ON m.Id=a.MovementId JOIN InventoryDocumentLines dl ON dl.Id=m.DocumentLineId JOIN InventoryDocuments d ON d.Id=dl.DocumentId WHERE d.DocumentType='manual_issue'"));
                }
                Assert.True(await new SqliteInventoryStore(cs).VerifyProjectionAsync(CancellationToken.None));
                var rollback=await svc.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitPrice="12",Discount="0"}}},1,CancellationToken.None);long before;
                using(var c=new SQLiteConnection(cs)){c.Open();before=c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant});c.Execute("CREATE TRIGGER test_fail_sale_audit BEFORE INSERT ON SalesInvoiceAudit WHEN NEW.EventType='posted' BEGIN SELECT RAISE(ABORT,'test_failure'); END;");}
                var rolledBack=await svc.PostAsync(rollback.Id,"sales-op-rollback",1,CancellationToken.None);Assert.False(rolledBack.Succeeded);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(before,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));Assert.Null(c.ExecuteScalar<long?>("SELECT InventoryDocumentId FROM SalesInvoices WHERE Id=@Id",new{Id=rollback.Id}));c.Execute("DROP TRIGGER test_fail_sale_audit;");}
                var recovered=await svc.PostAsync(rollback.Id,"sales-op-rollback",1,CancellationToken.None);Assert.True(recovered.Succeeded,recovered.ErrorMessage);
                using(var c=new SQLiteConnection(cs)){c.Open();
                    // No stock was consumed, so the posted sale has no stock movement.
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM StockMovements m JOIN SalesInvoices si ON si.InventoryDocumentId=m.DocumentId WHERE si.Id=@Id",new{Id=rollback.Id}));
                    Assert.Equal(before,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant",new{Variant=variant}));}
                var conflict=await svc.CreateDraftAsync(new SaleDraftCommand{WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode="cash",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitPrice="12",Discount="0"}}},1,CancellationToken.None);
                var conflictingReplay=await svc.PostAsync(conflict.Id,"sales-op-rollback",1,CancellationToken.None);Assert.False(conflictingReplay.Succeeded);Assert.Equal("operation_key_conflict",conflictingReplay.ErrorCode);
                Migrator.Run(cs);var reopened=new SalesService(new SqliteSalesStore(cs));var history=await reopened.ListAsync(null,null,null,CancellationToken.None);Assert.Equal(4,history.Count);Assert.Contains(history,x=>x.InvoiceNumber==draft.InvoiceNumber&&x.Status=="posted");
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
        private static async Task FundRefundCashbox(string cs)
        {
            var treasury = new AlKhayal.Application.Treasury.TreasuryService(new AlKhayal.Data.Treasury.SqliteTreasuryStore(cs));
            var account = await treasury.SaveAsync(null,new AlKhayal.Application.Treasury.TreasuryAccountCommand{Code="SALES-RETURNS",Name="مرتجعات البيع",Type="cash"},1,CancellationToken.None);
            await treasury.PostAsync(new AlKhayal.Application.Treasury.TreasuryMovementCommand{AccountId=account.Id,Amount="12.00",OperationKey="refund-funding"},"deposit",1,CancellationToken.None);
        }
        private static async Task OpenShift(string cs)
        {
            // Legacy fixture inserts a user without a role; mandatory shifts require an active cashier.
            using (var c = new SQLiteConnection(cs)) { c.Open(); c.Execute("UPDATE Users SET Role='cashier',IsActive=1 WHERE Id=1"); }
            var service = new WorkforceService(new SqliteWorkforceStore(cs));
            await service.ExecuteAsync("shift-open", new WorkforceCommand { Amount = "0", OperationKey = "sales-persistence-open" }, 1, true, CancellationToken.None);
        }
    }
}
