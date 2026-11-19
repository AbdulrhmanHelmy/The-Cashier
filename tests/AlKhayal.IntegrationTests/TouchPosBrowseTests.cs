using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Sales;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Customers;
using AlKhayal.Data.Database;
using AlKhayal.Data.Inventory;
using AlKhayal.Data.Sales;
using Dapper;
using Xunit;
using Xunit.Abstractions;

namespace AlKhayal.IntegrationTests
{
    public sealed class TouchPosBrowseTests
    {
        private readonly ITestOutputHelper _output;
        public TouchPosBrowseTests(ITestOutputHelper output){_output=output;}

        private static string DisposablePath()
        {
            var path=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"alkhayal-touchpos-"+Guid.NewGuid().ToString("N")+".db"));
            var real=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TheCashier","thecashier.db"));
            Assert.False(string.Equals(path,real,StringComparison.OrdinalIgnoreCase));
            Assert.False(path.StartsWith(Path.GetDirectoryName(real)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
            Assert.False(File.Exists(path));
            return path;
        }

        private static async Task<(long product,long variant,long unit,long customer)> Seed(string cs)
        {
            Migrator.Run(cs);
            using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('touchpos-admin','test-only','2026-01-01T00:00:00Z'); INSERT INTO CashierShifts(CashierId,RegisterId,OpenedUtc,OpenedBy,OpeningScaled) VALUES(1,'standalone','2026-01-01T00:00:00Z',1,0)");c.Execute("INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('C-TP','عميل لمس','0100','',0,10000,'أساسي','نشط','2026-01-01','2026-01-01')");}
            var catalog=new CatalogService(new SqliteCatalogStore(cs));var product=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="touchpos-product",Name="صنف لمس",BaseUnitId=1,ProductType="stock",SalePrice="12",PurchasePrice="9",IsActive=true},CancellationToken.None);Assert.True(product.Succeeded,product.ErrorMessage);
            using(var c=new SQLiteConnection(cs)){c.Open();return(product.Value.Id,c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=product.Value.Id}),c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=product.Value.Id}),c.ExecuteScalar<long>("SELECT Id FROM Customers WHERE Code='C-TP'"));}
        }

        private static async Task Receipt(SqliteInventoryStore inventory,long variant,long unit,string key,string quantity,string cost,long warehouse=1)
        {var result=await inventory.PostAsync(new InventoryPostCommand{OperationKey=key,Type="manual_receipt",WarehouseId=warehouse,UserId=1,Reason="توريد لمس",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity=quantity,UnitCost=cost,SalePrice="12"}}},"v2:"+key,CancellationToken.None);Assert.True(result.Succeeded,result.ErrorMessage);}

        private static async Task<SaleInvoice> Draft(SalesService sales,long variant,long unit,long? customer,string mode,string quantity="1",string price="12")
        {return await sales.CreateDraftAsync(new SaleDraftCommand{CustomerId=customer,WarehouseId=1,InvoiceDate="2026-10-25",PaymentMode=mode,Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity=quantity,UnitPrice=price}}},1,CancellationToken.None);}

        private static async Task<SalePostResult> Post(SalesService sales,long draftId,string key)
        {var r=await sales.PostAsync(draftId,key,1,CancellationToken.None);Assert.True(r.Succeeded,r.ErrorMessage);return r;}

        private static async Task<SaleReturnResult> Return(SalesService sales,long invoiceId,long lineId,string key,string quantity)
        {var r=await sales.ReturnAsync(invoiceId,new SaleReturnCommand{OperationKey=key,Reason="مرتجع لمس",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=lineId,Quantity=quantity}}},1,CancellationToken.None);Assert.True(r.Succeeded,r.ErrorMessage);return r;}

        private static async Task<SaleCancellationResult> Cancel(SalesService sales,long invoiceId,string key,string reason="إلغاء لمس")
        {var r=await sales.CancelAsync(invoiceId,key,reason,1,CancellationToken.None);Assert.True(r.Succeeded,r.ErrorMessage);return r;}

        private static long Popularity(string cs,long productId)
        {
            using(var c=new SQLiteConnection(cs)){c.Open();return c.ExecuteScalar<long>("SELECT COALESCE(NetQuantityBaseScaled,0) FROM ProductPopularity WHERE ProductId=@Id",new{Id=productId});}
        }

        [Fact]
        public async Task Backfill_counts_historical_posted_sales_and_converted_units_excluding_canceled()
        {
            var path=DisposablePath();var cs=Db.ConnectionStringFor(path);
            try
            {
                var ids=await Seed(cs);var catalog=new CatalogService(new SqliteCatalogStore(cs));
                var inventory=new SqliteInventoryStore(cs);var sales=new SalesService(new SqliteSalesStore(cs));
                var carton=await catalog.SaveUnitAsync(new SaveUnitCommand{Code="CARTON",Name="كرتونة",Symbol="ك",IsActive=true},CancellationToken.None);
                Assert.True(carton.Succeeded);
                long cartonUnit;
                using(var c=new SQLiteConnection(cs))
                {
                    c.Open();c.Execute(@"INSERT INTO ProductUnits(ProductId,UnitId,ToBaseNumerator,ToBaseDenominator,IsBase,IsSaleUnit,IsPurchaseUnit,IsActive,Version,CreatedUtc,UpdatedUtc)
                        VALUES(@Product,@Unit,12,1,0,1,1,1,1,'2026-01-01','2026-01-01')",new{Product=ids.product,Unit=carton.Value.Id});
                    cartonUnit=c.ExecuteScalar<long>("SELECT last_insert_rowid()");
                }
                await Receipt(inventory,ids.variant,ids.unit,"backfill-stock","100","9");
                var draft=await Draft(sales,ids.variant,cartonUnit,ids.customer,"credit","2","144");
                var posted=await Post(sales,draft.Id,"backfill-post");
                await Return(sales,draft.Id,posted.Invoice.Lines.Single().Id,"backfill-return","0.5"); // 24 - 6 = 18 pieces
                var canceled=await Draft(sales,ids.variant,ids.unit,ids.customer,"credit","3");
                await Post(sales,canceled.Id,"backfill-canceled-post");await Cancel(sales,canceled.Id,"backfill-cancel");
                using(var c=new SQLiteConnection(cs))
                {
                    c.Open();c.Execute(@"DROP TRIGGER TR_ProductPopularity_Posted; DROP TRIGGER TR_ProductPopularity_Returned;
                        DROP TRIGGER TR_ProductPopularity_Canceled; DROP TABLE ProductPopularity;
                        DELETE FROM SchemaVersion WHERE Name LIKE '%078_pos_product_popularity.sql'");
                }
                Migrator.Run(cs);Assert.Equal(18_000_000L,Popularity(cs,ids.product));
                Migrator.Run(cs);Assert.Equal(18_000_000L,Popularity(cs,ids.product));
                // Original saved conversion survives subsequent catalog conversion edits.
                using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("UPDATE ProductUnits SET ToBaseNumerator=24 WHERE Id=@Id",new{Id=cartonUnit});}
                await Return(sales,draft.Id,posted.Invoice.Lines.Single().Id,"after-backfill-return","0.5");
                Assert.Equal(12_000_000L,Popularity(cs,ids.product));
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task Late_post_failure_rolls_back_projection_and_retry_counts_once()
        {
            var path=DisposablePath();var cs=Db.ConnectionStringFor(path);
            try
            {
                var ids=await Seed(cs);var sales=new SalesService(new SqliteSalesStore(cs));
                var draft=await Draft(sales,ids.variant,ids.unit,ids.customer,"credit","2");
                using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("CREATE TRIGGER FailPopularityAudit BEFORE INSERT ON SalesInvoiceAudit WHEN NEW.EventType='posted' BEGIN SELECT RAISE(ABORT,'injected'); END");}
                Assert.False((await sales.PostAsync(draft.Id,"rollback-post",1,CancellationToken.None)).Succeeded);
                Assert.Equal(0,Popularity(cs,ids.product));Assert.Equal("draft",(await sales.GetAsync(draft.Id,CancellationToken.None)).Status);
                using(var c=new SQLiteConnection(cs)){c.Open();Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE SalesInvoiceId=@Id",new{Id=draft.Id}));c.Execute("DROP TRIGGER FailPopularityAudit");}
                var posted=await Post(sales,draft.Id,"rollback-post");Assert.Equal(2_000_000,Popularity(cs,ids.product));
                // No physical stock was restored, but the entire financial return reduces sold quantity.
                await Return(sales,draft.Id,posted.Invoice.Lines.Single().Id,"shortfall-return","2");
                Assert.Equal(0,Popularity(cs,ids.product));
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task Triggers_maintain_projection_on_post_return_cancel_and_replays()
        {
            var path=DisposablePath();var cs=Db.ConnectionStringFor(path);
            try
            {
                var ids=await Seed(cs);
                var inventory=new SqliteInventoryStore(cs);
                var sales=new SalesService(new SqliteSalesStore(cs));
                await Receipt(inventory,ids.variant,ids.unit,"trigger-stock","10","9");

                Assert.Equal(0,Popularity(cs,ids.product));

                var draft=await Draft(sales,ids.variant,ids.unit,ids.customer,"credit","5");
                var posted=await Post(sales,draft.Id,"trigger-post");
                Assert.Equal(5_000_000L,Popularity(cs,ids.product));

                // Replay post with same key must not change popularity.
                var replayPost=await sales.PostAsync(draft.Id,"trigger-post",1,CancellationToken.None);
                Assert.True(replayPost.Succeeded);Assert.Equal(draft.Id,replayPost.Invoice.Id);
                Assert.Equal(5_000_000L,Popularity(cs,ids.product));

                // Return 2 base units.
                await Return(sales,draft.Id,posted.Invoice.Lines.Single().Id,"trigger-return","2");
                Assert.Equal(3_000_000L,Popularity(cs,ids.product));

                // Replay return with same key must not change popularity.
                var replayReturn=await sales.ReturnAsync(draft.Id,new SaleReturnCommand{OperationKey="trigger-return",Reason="مرتجع لمس",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=posted.Invoice.Lines.Single().Id,Quantity="2"}}},1,CancellationToken.None);
                Assert.True(replayReturn.Succeeded);Assert.True(replayReturn.Replayed);
                Assert.Equal(3_000_000L,Popularity(cs,ids.product));

                // New sale after return to bring popularity back up.
                var draft2=await Draft(sales,ids.variant,ids.unit,ids.customer,"credit","2");
                await Post(sales,draft2.Id,"trigger-post-2");
                Assert.Equal(5_000_000L,Popularity(cs,ids.product));

                // Cancel the second sale: subtract full sold quantity once.
                await Cancel(sales,draft2.Id,"trigger-cancel-2");
                Assert.Equal(3_000_000L,Popularity(cs,ids.product));

                // Replay cancel with same key must not change popularity.
                var replayCancel=await sales.CancelAsync(draft2.Id,"trigger-cancel-2","إلغاء لمس",1,CancellationToken.None);
                Assert.True(replayCancel.Succeeded);Assert.True(replayCancel.Replayed);
                Assert.Equal(3_000_000L,Popularity(cs,ids.product));
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task No_sales_products_order_by_name_then_id_with_stable_ties()
        {
            var path=DisposablePath();var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);var catalog=new CatalogService(new SqliteCatalogStore(cs));var ids=new List<long>();
                foreach(var name in new[]{"ب","أ","أ"})
                {
                    var result=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey=Guid.NewGuid().ToString(),Name=name,BaseUnitId=1,ProductType="stock",SalePrice="1",IsActive=true},CancellationToken.None);
                    Assert.True(result.Succeeded,result.ErrorMessage);ids.Add(result.Value.Id);
                }
                var expected=new[]{ids[1],ids[2],ids[0]};
                for(var repeat=0;repeat<2;repeat++)for(var page=1;page<=3;page++)
                {
                    var result=await catalog.ListProductsAsync(new ProductQuery{PosWarehouseId=1,Page=page,PageSize=1},CancellationToken.None);
                    Assert.Equal(3,result.Value.TotalCount);Assert.Equal(expected[page-1],Assert.Single(result.Value.Items).Id);
                }
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task Pos_categories_exclude_empty_inactive_archived_or_ineligible_products()
        {
            var path=DisposablePath();var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);var catalog=new CatalogService(new SqliteCatalogStore(cs));long eligibleCategory=0;
                foreach(var kind in new[]{"eligible","empty","inactive-category","inactive-product","archived","review","variant","unit","service"})
                {
                    var category=await catalog.SaveCategoryAsync(new SaveCategoryCommand{Name=kind,IsActive=true},CancellationToken.None);
                    Assert.True(category.Succeeded);if(kind=="eligible")eligibleCategory=category.Value.Id;
                    if(kind=="empty")continue;
                    var product=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey=kind,Name=kind,CategoryId=category.Value.Id,BaseUnitId=1,ProductType="stock",SalePrice="1",IsActive=true},CancellationToken.None);
                    Assert.True(product.Succeeded);
                    using(var c=new SQLiteConnection(cs))
                    {
                        c.Open();var id=new{Id=product.Value.Id};
                        if(kind=="inactive-category")c.Execute("UPDATE ProductCategories SET IsActive=0 WHERE Id=@Id",new{Id=category.Value.Id});
                        if(kind=="inactive-product")c.Execute("UPDATE ProductDetails SET IsActive=0 WHERE ProductId=@Id",id);
                        if(kind=="archived")c.Execute("UPDATE ProductDetails SET ArchivedUtc='2026-01-01' WHERE ProductId=@Id",id);
                        if(kind=="review")c.Execute("UPDATE ProductDetails SET MigrationState='review_required' WHERE ProductId=@Id",id);
                        if(kind=="variant")c.Execute("UPDATE ProductVariants SET IsActive=0 WHERE ProductId=@Id",id);
                        if(kind=="unit")c.Execute("UPDATE ProductUnits SET IsSaleUnit=0 WHERE ProductId=@Id",id);
                        if(kind=="service")c.Execute("UPDATE ProductDetails SET ProductType='service' WHERE ProductId=@Id",id);
                    }
                }
                var categories=await catalog.ListPosCategoriesAsync(CancellationToken.None);
                Assert.Equal(eligibleCategory,Assert.Single(categories).Id);
                var all=await catalog.ListProductsAsync(new ProductQuery{PosWarehouseId=1,PageSize=100},CancellationToken.None);
                Assert.Equal(2,all.Value.TotalCount);Assert.All(all.Value.Items,x=>Assert.Equal(0,x.AvailableQuantityScaled));
                // General catalog semantics remain broader than POS.
                var general=await catalog.ListProductsAsync(new ProductQuery{PageSize=100},CancellationToken.None);Assert.Equal(7,general.Value.TotalCount);
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task Zero_stock_product_is_visible_and_sellable()
        {
            var path=DisposablePath();var cs=Db.ConnectionStringFor(path);
            try
            {
                var ids=await Seed(cs);
                var catalog=new CatalogService(new SqliteCatalogStore(cs));
                var inventory=new SqliteInventoryStore(cs);
                var sales=new SalesService(new SqliteSalesStore(cs));
                // Do not receipt any stock.
                var page=await catalog.ListProductsAsync(new ProductQuery{PosWarehouseId=1,IsActive=true,Page=1,PageSize=10},CancellationToken.None);
                Assert.Single(page.Value.Items);
                Assert.Equal(ids.product,page.Value.Items[0].Id);
                Assert.Equal(0,page.Value.Items[0].AvailableQuantityScaled);

                // Verify zero-stock sale is allowed (SalesStockFloor policy).
                var draft=await Draft(sales,ids.variant,ids.unit,ids.customer,"credit","1");
                var posted=await Post(sales,draft.Id,"zero-stock-post");
                Assert.True(posted.Succeeded);
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact]
        public async Task Pos_available_quantity_tracks_selected_warehouse_sales_returns_and_inventory_changes()
        {
            var path=DisposablePath();var cs=Db.ConnectionStringFor(path);
            try
            {
                var ids=await Seed(cs);var catalog=new CatalogService(new SqliteCatalogStore(cs));
                var inventory=new SqliteInventoryStore(cs);var sales=new SalesService(new SqliteSalesStore(cs));
                var secondWarehouse=await inventory.SaveWarehouseAsync(new WarehouseCommand{Code="TOUCHPOS-W2",Name="مخزن لمس ثان",IsActive=true},true,CancellationToken.None);
                Assert.True(secondWarehouse.Succeeded,secondWarehouse.ErrorMessage);
                var receipt2=await inventory.PostAsync(new InventoryPostCommand{OperationKey="touchpos-receipt-warehouse-2",Type="manual_receipt",WarehouseId=secondWarehouse.Value.Id,UserId=1,Reason="توريد للمخزن الثاني",Lines=new[]{new InventoryLineInput{VariantId=ids.variant,ProductUnitId=ids.unit,Quantity="7",UnitCost="9",SalePrice="12"}}},"v2:touchpos-receipt-warehouse-2",CancellationToken.None);
                Assert.True(receipt2.Succeeded,receipt2.ErrorMessage);
                await Receipt(inventory,ids.variant,ids.unit,"touchpos-receipt-warehouse-1","10","9");
                async Task<long> Quantity(long warehouse)
                {
                    var page=await catalog.ListProductsAsync(new ProductQuery{PosWarehouseId=warehouse,IsActive=true,Page=1,PageSize=10},CancellationToken.None);
                    return Assert.Single(page.Value.Items).AvailableQuantityScaled.Value;
                }
                Assert.Equal(10_000_000L,await Quantity(1));Assert.Equal(7_000_000L,await Quantity(secondWarehouse.Value.Id));
                var invoice=await Draft(sales,ids.variant,ids.unit,ids.customer,"credit","3");var posted=await Post(sales,invoice.Id,"touchpos-stock-post");
                Assert.Equal(7_000_000L,await Quantity(1));Assert.Equal(7_000_000L,await Quantity(2));
                await Return(sales,posted.Invoice.Id,posted.Invoice.Lines[0].Id,"touchpos-stock-return","1");
                Assert.Equal(8_000_000L,await Quantity(1));
                await Receipt(inventory,ids.variant,ids.unit,"touchpos-stock-adjustment","2","9");
                Assert.Equal(10_000_000L,await Quantity(1));
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        private sealed class PlanRow { public string Detail { get; set; } }

        [Fact]
        public async Task Performance_browses_15k_products_with_30k_sale_lines_without_ledger_scan()
        {
            var path=DisposablePath();var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);
                using(var c=new SQLiteConnection(cs))
                {
                    c.Open();using(var tx=c.BeginTransaction())
                    {
                        c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('perf-admin','test-only','2026-01-01')",transaction:tx);
                        c.Execute("INSERT INTO ProductCategories(Name,NormalizedName,IsActive,CreatedUtc,UpdatedUtc) VALUES('perf','perf',1,'2026-01-01','2026-01-01')",transaction:tx);
                        c.Execute(@"WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<15000)
                            INSERT INTO Products(Name,Price) SELECT 'PERF-'||printf('%05d',x),1 FROM n",transaction:tx);
                        c.Execute(@"INSERT INTO ProductDetails(ProductId,BaseUnitId,CategoryId,ProductType,SellPriceScaled,IsActive,CreatedUtc,UpdatedUtc)
                            SELECT Id,1,1,'stock',10000,1,'2026-01-01','2026-01-01' FROM Products",transaction:tx);
                        c.Execute(@"INSERT INTO ProductVariants(ProductId,Name,NormalizedName,IsDefault,IsActive,CreatedUtc,UpdatedUtc)
                            SELECT Id,Name,Name,1,1,'2026-01-01','2026-01-01' FROM Products",transaction:tx);
                        c.Execute(@"INSERT INTO ProductUnits(ProductId,UnitId,ToBaseNumerator,ToBaseDenominator,IsBase,IsSaleUnit,IsPurchaseUnit,IsActive,Version,CreatedUtc,UpdatedUtc)
                            SELECT Id,1,1,1,1,1,1,1,1,'2026-01-01','2026-01-01' FROM Products",transaction:tx);
                        // Synthetic lifecycle snapshots in this disposable fixture; no FIFO/accounting benchmark claim.
                        c.Execute(@"WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<30000)
                            INSERT INTO SalesInvoices(InvoiceNumber,InvoiceDate,WarehouseId,CashierUserId,Status,PaymentMode,SubtotalScaled,DiscountScaled,TaxScaled,TotalScaled,CreatedUtc)
                            SELECT 'PERF-S-'||x,'2026-01-01',1,1,'draft','cash',10000,0,0,10000,'2026-01-01' FROM n",transaction:tx);
                        c.Execute(@"INSERT INTO InventoryDocuments(DocumentType,Status,SourceWarehouseId,Reason,CreatedByUserId,CreatedUtc)
                            VALUES('manual_issue','draft',1,'fixture',1,'2026-01-01')",transaction:tx);
                        c.Execute(@"INSERT INTO InventoryDocumentLines(DocumentId,LineNumber,VariantId,EnteredQuantityScaled,ProductUnitId,ToBaseNumerator,ToBaseDenominator,BaseQuantityScaled)
                            SELECT 1,si.Id,v.Id,1000000,pu.Id,1,1,0 FROM SalesInvoices si JOIN ProductVariants v ON v.ProductId=((si.Id-1)%15000)+1 JOIN ProductUnits pu ON pu.ProductId=v.ProductId",transaction:tx);
                        c.Execute(@"INSERT INTO SalesInvoiceLines(SalesInvoiceId,LineNumber,VariantId,ProductUnitId,QuantityScaled,UnitPriceScaled,DiscountScaled,InvoiceDiscountScaled,TaxScaled,LineTotalScaled,InventoryDocumentLineId)
                            SELECT LineNumber,1,VariantId,ProductUnitId,EnteredQuantityScaled,10000,0,0,0,10000,Id FROM InventoryDocumentLines",transaction:tx);
                        c.Execute("UPDATE SalesInvoices SET Status='posted'",transaction:tx);tx.Commit();
                    }
                    Assert.Equal(30000,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesInvoiceLines"));
                    Assert.Equal(15000,c.ExecuteScalar<long>("SELECT COUNT(*) FROM ProductPopularity WHERE NetQuantityBaseScaled=2000000"));
                }
                var catalog=new CatalogService(new SqliteCatalogStore(cs));
                foreach(var query in new[]{new ProductQuery{Page=1},new ProductQuery{Page=375},new ProductQuery{Search="PERF-15000"},new ProductQuery{CategoryId=1}})
                {
                    query.PosWarehouseId=1;query.PageSize=40;var watch=Stopwatch.StartNew();
                    var result=await catalog.ListProductsAsync(query,CancellationToken.None);watch.Stop();
                    _output.WriteLine("15k catalog / 30k sales: page={0}, search={1}, category={2}: {3:0.000} ms",query.Page,query.Search,query.CategoryId,watch.Elapsed.TotalMilliseconds);
                    Assert.True(result.Succeeded);Assert.Equal(query.Search==null?40:1,result.Value.Items.Count);
                    Assert.Equal(query.Search==null?15000:1,result.Value.TotalCount);
                }
                using(var c=new SQLiteConnection(cs))
                {
                    c.Open();var plan=c.Query<PlanRow>(@"EXPLAIN QUERY PLAN SELECT p.Id FROM Products p JOIN ProductDetails d ON d.ProductId=p.Id
                        LEFT JOIN ProductPopularity pop ON pop.ProductId=p.Id
                        WHERE d.ProductType='stock' AND d.MigrationState='ready' AND d.IsActive=1 AND d.ArchivedUtc IS NULL
                        AND EXISTS(SELECT 1 FROM ProductVariants v WHERE v.ProductId=p.Id AND v.IsActive=1 AND v.ArchivedUtc IS NULL)
                        AND EXISTS(SELECT 1 FROM ProductUnits u WHERE u.ProductId=p.Id AND u.IsActive=1 AND u.IsSaleUnit=1)
                        ORDER BY COALESCE(pop.NetQuantityBaseScaled,0) DESC,p.Name,p.Id LIMIT 40").Select(x=>x.Detail).ToArray();
                    _output.WriteLine(string.Join("; ",plan));Assert.Contains(plan,x=>x.Contains("SEARCH pop USING INTEGER PRIMARY KEY"));
                    Assert.DoesNotContain(plan,x=>x.Contains("SalesInvoice")||x.Contains("SalesReturn"));
                }
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
    }
}
