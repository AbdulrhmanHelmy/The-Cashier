using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Treasury;
using AlKhayal.Application.Users;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Database;
using AlKhayal.Data.Inventory;
using AlKhayal.Data.Sales;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    // FR-004 migration: the additive SalesReturns/SalesReturnLines rebuild preserves every
    // historical row, foreign key, trigger and index on a representative pre-migration
    // database, rolls back completely on late failure, and then enables zero-value returns.
    public sealed class ZeroValueReturnMigrationTests
    {
        private const string FreeReturnKey = "fr4-upgrade-zero";

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Upgrade_preserves_return_history_and_enables_zero_value_returns(bool failFirst)
        {
            var root = Path.Combine(Path.GetTempPath(), "fr4-upgrade-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var cs = Db.ConnectionStringFor(Path.Combine(root, "test.db"));
            try
            {
                using (var c = new SQLiteConnection(cs))
                {
                    c.Open();
                    c.Execute("CREATE TABLE SchemaVersion(Name TEXT PRIMARY KEY,AppliedAt TEXT NOT NULL); PRAGMA foreign_keys=ON;");
                    var assembly = typeof(Migrator).Assembly;
                    // Representative pre-085 operator database: every older migration applied in order.
                    foreach (var script in assembly.GetManifestResourceNames()
                        .Where(x => x.EndsWith(".sql", StringComparison.Ordinal) && !x.EndsWith("085_zero_value_sales_returns.sql", StringComparison.Ordinal))
                        .OrderBy(x => x, StringComparer.Ordinal))
                    {
                        string sql;
                        using (var stream = assembly.GetManifestResourceStream(script))
                        using (var reader = new StreamReader(stream)) sql = reader.ReadToEnd();
                        using (var tx = c.BeginTransaction())
                        {
                            c.Execute(sql, transaction: tx);
                            c.Execute("INSERT INTO SchemaVersion VALUES(@script,'2026-11-01T00:00:00Z')", new { script }, transaction: tx);
                            tx.Commit();
                        }
                    }
                }

                var user = await new SqliteUserStore(cs).CreateAsync("fr4-upgrade", "اختبار الترقية", new PasswordHasher().Hash(Guid.NewGuid().ToString()), UserRoles.Administrator, CancellationToken.None);
                var sales = new SalesService(new SqliteSalesStore(cs));
                var catalog = new CatalogService(new SqliteCatalogStore(cs));
                long paidVariant = 0, paidUnit = 0, freeVariant = 0, freeUnit = 0, freeProduct = 0;
                foreach (var product in new[] { ("fr4-paid", "صنف مدفوع", "12.34", "9"), ("fr4-free", "صنف مجاني", "0.01", "2") })
                {
                    var created = await catalog.CreateProductAsync(new CreateProductCommand { OperationKey = product.Item1, Name = product.Item2, BaseUnitId = 1, ProductType = "stock", SalePrice = product.Item3, PurchasePrice = product.Item4, IsActive = true }, CancellationToken.None);
                    Assert.True(created.Succeeded, created.ErrorMessage);
                    using (var c = new SQLiteConnection(cs))
                    {
                        c.Open();
                        var variant = c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id", new { Id = created.Value.Id });
                        var unit = c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1", new { Id = created.Value.Id });
                        var receipt = new InventoryPostCommand { OperationKey = product.Item1 + "-stock", Type = "manual_receipt", WarehouseId = 1, UserId = user, Reason = "توريد", Lines = new[] { new InventoryLineInput { VariantId = variant, ProductUnitId = unit, Quantity = "1", UnitCost = product.Item4, SalePrice = product.Item3 } } };
                        Assert.True((await new SqliteInventoryStore(cs).PostAsync(receipt, InventoryService.CanonicalHashPost(receipt), CancellationToken.None)).Succeeded);
                        if (paidVariant == 0) { paidVariant = variant; paidUnit = unit; }
                        else { freeVariant = variant; freeUnit = unit; freeProduct = created.Value.Id; }
                    }
                }
                using (var c = new SQLiteConnection(cs)) { c.Open(); c.Execute("INSERT INTO CashierShifts(CashierId,RegisterId,OpenedUtc,OpenedBy,OpeningScaled) VALUES(@User,'standalone','2026-11-01T00:00:00Z',@User,0)", new { User = user }); }
                var draft = await sales.CreateDraftAsync(new SaleDraftCommand
                {
                    WarehouseId = 1, InvoiceDate = "2026-11-01", PaymentMode = "cash", Discount = "0",
                    Lines = new System.Collections.Generic.List<SaleLine> {
                        new SaleLine { VariantId = paidVariant, ProductUnitId = paidUnit, Quantity = "1", UnitPrice = "12.34" },
                        new SaleLine { VariantId = freeVariant, ProductUnitId = freeUnit, Quantity = "1", UnitPrice = "0.01", Discount = "0.01" } }
                }, user, CancellationToken.None);
                var posted = await sales.PostAsync(draft.Id, "fr4-upgrade-post", user, CancellationToken.None);
                Assert.True(posted.Succeeded, posted.ErrorMessage);
                Assert.Equal(0, posted.Invoice.Lines[1].LineTotalScaled);

                // A historical positive return exercises every preserved child table and link.
                var treasury = new TreasuryService(new AlKhayal.Data.Treasury.SqliteTreasuryStore(cs));
                var returnsCashbox = await treasury.SaveAsync(null, new TreasuryAccountCommand { Code = "SALES-RETURNS", Name = "مرتجعات البيع", Type = "cash" }, user, CancellationToken.None);
                await treasury.PostAsync(new TreasuryMovementCommand { AccountId = returnsCashbox.Id, Amount = "12.34", OperationKey = "fr4-upgrade-fund" }, "deposit", user, CancellationToken.None);
                var paidReturn = await sales.ReturnAsync(draft.Id, new SaleReturnCommand { OperationKey = "fr4-upgrade-paid", Reason = "مرتجع مدفوع تاريخي", Lines = new System.Collections.Generic.List<SaleReturnLineCommand> { new SaleReturnLineCommand { InvoiceLineId = posted.Invoice.Lines[0].Id, Quantity = "1" } } }, user, CancellationToken.None);
                Assert.True(paidReturn.Succeeded, paidReturn.ErrorMessage);
                Assert.Equal(123400, paidReturn.TotalScaled);

                // Old schema reproduction: the valid zero-value line cannot be returned yet,
                // and the failed attempt rolls back without consuming or restoring stock.
                var blocked = await sales.ReturnAsync(draft.Id, new SaleReturnCommand { OperationKey = FreeReturnKey, Reason = "مرتجع مجاني تاريخي", Lines = new System.Collections.Generic.List<SaleReturnLineCommand> { new SaleReturnLineCommand { InvoiceLineId = posted.Invoice.Lines[1].Id, Quantity = "1" } } }, user, CancellationToken.None);
                Assert.False(blocked.Succeeded);
                Assert.Equal("sales_conflict", blocked.ErrorCode);
                using (var c = new SQLiteConnection(cs)) { c.Open(); Assert.Equal(0, c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant", new { Variant = freeVariant })); Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesReturns")); }

                string before; long beforeMigrationCount;
                using (var c = new SQLiteConnection(cs))
                {
                    c.Open();
                    before = Snapshot(c);
                    beforeMigrationCount = c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion");
                    if (failFirst) c.Execute("CREATE TRIGGER FailFr4Migration BEFORE INSERT ON SchemaVersion WHEN NEW.Name LIKE '%085_zero_value_sales_returns.sql' BEGIN SELECT RAISE(ABORT,'injected'); END;");
                }
                if (failFirst)
                {
                    Assert.Throws<SQLiteException>(() => Migrator.Run(cs));
                    using (var c = new SQLiteConnection(cs))
                    {
                        c.Open();
                        Assert.Equal(before, Snapshot(c));
                        Assert.Equal(beforeMigrationCount, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));
                        Assert.Contains("TotalScaled>0", c.ExecuteScalar<string>("SELECT sql FROM sqlite_master WHERE name='SalesReturns'"));
                        Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE name IN ('SalesReturns_085','SalesReturnLines_085')"));
                        c.Execute("DROP TRIGGER FailFr4Migration");
                    }
                }
                Migrator.Run(cs); Migrator.Run(cs);
                using (var c = new SQLiteConnection(cs))
                {
                    c.Open();
                    Assert.Equal(MigrationTestExpectations.Count, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersion"));
                    Assert.Equal(before, Snapshot(c));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                    Assert.Equal("ok", c.ExecuteScalar<string>("PRAGMA integrity_check"));
                    Assert.Contains("TotalScaled>=0", c.ExecuteScalar<string>("SELECT sql FROM sqlite_master WHERE name='SalesReturns'"));
                    Assert.Contains("AmountScaled>=0", c.ExecuteScalar<string>("SELECT sql FROM sqlite_master WHERE name='SalesReturnLines'"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE name IN ('SalesReturns_085','SalesReturnLines_085')"));
                    foreach (var name in new[] { "TR_SalesReturns_NoUpdate", "TR_SalesReturns_NoDelete", "TR_SalesReturnLines_NoUpdate", "TR_SalesReturnLines_NoDelete", "TR_ProductPopularity_Returned", "IX_SalesReturns_Event", "IX_SalesReturnLines_InvoiceLine", "IX_SalesReturnLines_Original" })
                        Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE name=@Name", new { Name = name }));
                    Assert.Throws<SQLiteException>(() => c.Execute("UPDATE SalesReturns SET Reason='tampered' WHERE Id=@Id", new { Id = paidReturn.ReturnId }));
                    Assert.Throws<SQLiteException>(() => c.Execute("UPDATE SalesReturnLines SET AmountScaled=1 WHERE SalesReturnId=@Id", new { Id = paidReturn.ReturnId }));
                }

                // The same zero-value return now succeeds with exact restoration and no money movement.
                var zeroReturn = await sales.ReturnAsync(draft.Id, new SaleReturnCommand { OperationKey = FreeReturnKey, Reason = "مرتجع مجاني تاريخي", Lines = new System.Collections.Generic.List<SaleReturnLineCommand> { new SaleReturnLineCommand { InvoiceLineId = posted.Invoice.Lines[1].Id, Quantity = "1" } } }, user, CancellationToken.None);
                Assert.True(zeroReturn.Succeeded, zeroReturn.ErrorMessage);
                Assert.Equal(0, zeroReturn.TotalScaled);
                Assert.Equal(0, zeroReturn.CashRefundScaled);
                Assert.Equal(0, zeroReturn.CreditReductionScaled);
                Assert.True(zeroReturn.ReturnId > paidReturn.ReturnId);
                using (var c = new SQLiteConnection(cs))
                {
                    c.Open();
                    Assert.Equal(1000000, c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant", new { Variant = freeVariant }));
                    Assert.Equal(20000, c.ExecuteScalar<long>("SELECT ValueScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant", new { Variant = freeVariant }));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT AmountScaled FROM SalesReturnLines WHERE SalesReturnId=@Id", new { Id = zeroReturn.ReturnId }));
                    Assert.Equal(20000, c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM SalesReturnCostAllocations WHERE SalesReturnLineId IN (SELECT Id FROM SalesReturnLines WHERE SalesReturnId=@Id)", new { Id = zeroReturn.ReturnId }));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COALESCE(NetQuantityBaseScaled,-1) FROM ProductPopularity WHERE ProductId=@Id", new { Id = freeProduct }));
                    // Sale deposit + cashbox funding + historical refund withdrawal: exactly three
                    // movements and one immutable link; the zero-value return adds neither.
                    Assert.Equal(3, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM ReturnTreasuryLinks"));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
                Assert.True((await sales.ReturnAsync(draft.Id, new SaleReturnCommand { OperationKey = FreeReturnKey, Reason = "مرتجع مجاني تاريخي", Lines = new System.Collections.Generic.List<SaleReturnLineCommand> { new SaleReturnLineCommand { InvoiceLineId = posted.Invoice.Lines[1].Id, Quantity = "1" } } }, user, CancellationToken.None)).Replayed);
                var detail = await new AlKhayal.Application.Returns.ReturnService(new AlKhayal.Data.Returns.SqliteReturnReadStore(cs), sales, new AlKhayal.Application.Purchasing.PurchaseService(new AlKhayal.Data.Purchasing.SqlitePurchaseRepository(cs), new AlKhayal.Data.Suppliers.SqliteSupplierRepository(cs))).GetAsync("sales", paidReturn.ReturnId, CancellationToken.None);
                Assert.Equal("12.34", detail.Total);
                Assert.Single(detail.Movements);
            }
            finally { SQLiteConnection.ClearAllPools(); Directory.Delete(root, true); }
        }

        // Every historical return fact, link and projection that migration 085 must copy unchanged.
        private static string Snapshot(SQLiteConnection c) =>
            string.Join("|", c.Query<string>("SELECT Id||':'||ReturnNumber||':'||SalesInvoiceId||':'||InventoryDocumentId||':'||OperationKey||':'||RequestHash||':'||TotalScaled||':'||CashRefundScaled||':'||CreditReductionScaled||':'||Reason||':'||CreatedByUserId||':'||CreatedUtc FROM SalesReturns ORDER BY Id")) + ";"
            + string.Join("|", c.Query<string>("SELECT Id||':'||SalesReturnId||':'||SalesInvoiceLineId||':'||QuantityScaled||':'||AmountScaled FROM SalesReturnLines ORDER BY Id")) + ";"
            + string.Join("|", c.Query<string>("SELECT Id||':'||SalesReturnLineId||':'||CostLayerId||':'||QuantityScaled||':'||ValueScaled FROM SalesReturnCostAllocations ORDER BY Id")) + ";"
            + string.Join("|", c.Query<string>("SELECT SalesReturnLineId||':'||PhysicalUnitId||':'||Serial FROM SalesReturnPhysicalUnits ORDER BY SalesReturnLineId,PhysicalUnitId")) + ";"
            + string.Join("|", c.Query<string>("SELECT Id||':'||COALESCE(SalesReturnId,0)||':'||COALESCE(PurchaseReturnId,0)||':'||Role||':'||MovementId FROM ReturnTreasuryLinks ORDER BY Id")) + ";"
            + string.Join("|", c.Query<string>("SELECT Id||':'||AccountId||':'||Type||':'||AmountScaled||':'||BalanceAfterScaled||':'||OperationKey FROM TreasuryMovements ORDER BY Id")) + ";"
            + string.Join("|", c.Query<string>("SELECT Id||':'||SalesInvoiceId||':'||COALESCE(SalesReturnId,0)||':'||EventType||':'||PaymentMethod||':'||AmountScaled||':'||OperationKey FROM SalesPayments ORDER BY Id")) + ";"
            + string.Join("|", c.Query<string>("SELECT Id||':'||CustomerId||':'||COALESCE(SalesReturnId,0)||':'||TransactionType||':'||AmountScaled||':'||OperationKey FROM CustomerTransactions ORDER BY Id")) + ";"
            + string.Join("|", c.Query<string>("SELECT ProductId||':'||NetQuantityBaseScaled FROM ProductPopularity ORDER BY ProductId")) + ";"
            + string.Join("|", c.Query<string>("SELECT Id||':'||PaidScaled FROM SalesInvoices ORDER BY Id"));
    }
}
