using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Sales;
using AlKhayal.Core.Models;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Customers;
using AlKhayal.Data.Inventory;
using AlKhayal.Data.Sales;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    // FR-004 regressions: a valid zero-value sold line is returnable with exact stock and
    // FIFO original-cost restoration, no payment/refund/treasury movement, preserved
    // quantity caps and idempotency, and unchanged paid/credit return behavior.
    public sealed class ZeroValueReturnTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;

        private sealed class Setup : IDisposable
        {
            public SalesTreasuryTests.Fixture F { get; } = new SalesTreasuryTests.Fixture();
            public long FreeVariant { get; private set; }
            public long FreeUnit { get; private set; }
            public long LayerId { get; private set; }
            public long FreeProduct { get; private set; }

            public async Task Prepare(string freeQuantity = "1", string freePrice = "0.01", string freeCost = "2", string sellingType = "piece")
            {
                await F.Prepare();
                var catalog = new CatalogService(new SqliteCatalogStore(F.Db.ConnectionString));
                var product = await catalog.CreateProductAsync(new CreateProductCommand { OperationKey = "fr4-free-product", Name = "صنف بند مجاني", BaseUnitId = 1, SellingType = sellingType, SalePrice = freePrice, PurchasePrice = freeCost, IsActive = true }, Ct);
                Assert.True(product.Succeeded, product.ErrorMessage);
                FreeProduct = product.Value.Id;
                using (var c = F.Db.Open())
                {
                    FreeVariant = c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id", new { Id = FreeProduct });
                    FreeUnit = c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1", new { Id = FreeProduct });
                }
                var receipt = new InventoryPostCommand { OperationKey = "fr4-free-stock", Type = "manual_receipt", WarehouseId = 1, UserId = F.Db.UserId, Reason = "توريد", Lines = new[] { new InventoryLineInput { VariantId = FreeVariant, ProductUnitId = FreeUnit, Quantity = freeQuantity, UnitCost = freeCost, SalePrice = freePrice } } };
                Assert.True((await new SqliteInventoryStore(F.Db.ConnectionString).PostAsync(receipt, InventoryService.CanonicalHashPost(receipt), Ct)).Succeeded);
                using (var c = F.Db.Open()) LayerId = c.ExecuteScalar<long>("SELECT Id FROM InventoryCostLayers WHERE VariantId=@Variant ORDER BY Id", new { Variant = FreeVariant });
            }

            // A paid line on the fixture product plus a fully discounted zero-value line.
            public async Task<SaleInvoice> Invoice(string freeQuantity = "1", string freeLineDiscount = "0.01", string paymentMode = "cash", long? customer = null, string freePrice = "0.01")
            {
                var draft = await F.Sales.CreateDraftAsync(new SaleDraftCommand
                {
                    WarehouseId = 1, InvoiceDate = "2026-11-03", PaymentMode = paymentMode, CustomerId = customer, Discount = "0",
                    Lines = new List<SaleLine> {
                        new SaleLine { VariantId = F.Variant, ProductUnitId = F.Unit, Quantity = "1", UnitPrice = "12.34" },
                        new SaleLine { VariantId = FreeVariant, ProductUnitId = FreeUnit, Quantity = freeQuantity, UnitPrice = freePrice, Discount = freeLineDiscount } }
                }, F.Db.UserId, Ct);
                var posted = await F.Post(draft.Id, "fr4-post-" + Guid.NewGuid().ToString("N"));
                Assert.True(posted.Succeeded, posted.ErrorMessage);
                Assert.Equal(0, posted.Invoice.Lines[1].LineTotalScaled);
                Assert.Equal(posted.Invoice.Lines[0].LineTotalScaled, posted.Invoice.TotalScaled);
                return posted.Invoice;
            }

            public Task<SaleReturnResult> Return(long invoiceId, long lineId, string quantity, string key) =>
                F.Sales.ReturnAsync(invoiceId, new SaleReturnCommand { OperationKey = key, Reason = "إرجاع بند مجاني", Lines = new List<SaleReturnLineCommand> { new SaleReturnLineCommand { InvoiceLineId = lineId, Quantity = quantity } } }, F.Db.UserId, Ct);

            public void Dispose() { F.Dispose(); }
        }

        [Fact]
        public async Task Entirely_zero_value_return_restores_stock_and_cost_without_any_money_movement()
        {
            using (var s = new Setup())
            {
                await s.Prepare();
                var invoice = await s.Invoice();
                var freeLine = invoice.Lines[1];
                var result = await s.Return(invoice.Id, freeLine.Id, "1", "fr4-all-zero");
                Assert.True(result.Succeeded, result.ErrorMessage);
                Assert.Equal(0, result.TotalScaled);
                Assert.Equal(0, result.CashRefundScaled);
                Assert.Equal(0, result.CreditReductionScaled);
                using (var c = s.F.Db.Open())
                {
                    var header = c.QuerySingle("SELECT TotalScaled,CashRefundScaled,CreditReductionScaled FROM SalesReturns");
                    Assert.Equal(0L, (long)header.TotalScaled); Assert.Equal(0L, (long)header.CashRefundScaled); Assert.Equal(0L, (long)header.CreditReductionScaled);
                    var line = c.QuerySingle("SELECT QuantityScaled,AmountScaled FROM SalesReturnLines");
                    Assert.Equal(1000000L, (long)line.QuantityScaled); Assert.Equal(0L, (long)line.AmountScaled);
                    // Exact stock and original FIFO cost restoration into the original warehouse.
                    var balance = c.QuerySingle("SELECT QuantityBaseScaled,ValueScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant", new { Variant = s.FreeVariant });
                    Assert.Equal(1000000L, (long)balance.QuantityBaseScaled); Assert.Equal(20000L, (long)balance.ValueScaled);
                    // The return inserts a replacement layer parented to the consumed original layer,
                    // carrying the original acquisition movement, quantity and exact original value.
                    var originMovement = c.ExecuteScalar<long>("SELECT OriginMovementId FROM InventoryCostLayers WHERE Id=@Layer", new { Layer = s.LayerId });
                    var restoredLayer = c.QuerySingle("SELECT RemainingQuantityScaled,RemainingValueScaled,OriginMovementId,Basis FROM InventoryCostLayers WHERE ParentLayerId=@Layer", new { Layer = s.LayerId });
                    Assert.Equal(1000000L, (long)restoredLayer.RemainingQuantityScaled); Assert.Equal(20000L, (long)restoredLayer.RemainingValueScaled);
                    Assert.Equal(originMovement, (long)restoredLayer.OriginMovementId); Assert.Equal("receipt", (string)restoredLayer.Basis);
                    Assert.Equal(1000000L, c.ExecuteScalar<long>("SELECT SUM(RemainingQuantityScaled) FROM InventoryCostLayers WHERE VariantId=@Variant", new { Variant = s.FreeVariant }));
                    var allocation = c.QuerySingle("SELECT QuantityScaled,ValueScaled,CostLayerId FROM SalesReturnCostAllocations");
                    Assert.Equal(1000000L, (long)allocation.QuantityScaled); Assert.Equal(20000L, (long)allocation.ValueScaled); Assert.Equal(s.LayerId, (long)allocation.CostLayerId);
                    // No refund payment, no treasury movement or cashbox, no customer ledger entry, invoice paid amount untouched.
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments WHERE EventType='refund'"));
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM ReturnTreasuryLinks"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM ReturnTreasuryAccounts"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions"));
                    Assert.Equal(123400, c.ExecuteScalar<long>("SELECT PaidScaled FROM SalesInvoices WHERE Id=@Id", new { Id = invoice.Id }));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COALESCE(NetQuantityBaseScaled,-1) FROM ProductPopularity WHERE ProductId=@Id", new { Id = s.FreeProduct }));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
                // The zero-value return is still visible in the returns management read model.
                var list = await ReturnManagementTests.Service(s.F).ListAsync(null, Ct);
                var record = Assert.Single(list);
                Assert.Equal("0.00", record.Total);
                var detail = await ReturnManagementTests.Service(s.F).GetAsync("sales", result.ReturnId, Ct);
                Assert.Equal("0.00", detail.Total); Assert.Equal("0.00", detail.Cash); Assert.Empty(detail.Movements);
                Assert.Equal("0.00", Assert.Single(detail.Lines).Amount);
                // Same-key replay after a restart posts nothing new; a different payload is rejected.
                AlKhayal.Data.Database.Migrator.Run(s.F.Db.ConnectionString);
                var replay = await s.Return(invoice.Id, freeLine.Id, "1", "fr4-all-zero");
                Assert.True(replay.Replayed); Assert.Equal(result.ReturnId, replay.ReturnId);
                using (var c = s.F.Db.Open()) Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesReturns"));
                var conflict = await s.F.Sales.ReturnAsync(invoice.Id, new SaleReturnCommand { OperationKey = "fr4-all-zero", Reason = "إرجاع بند مجاني", Lines = new List<SaleReturnLineCommand> { new SaleReturnLineCommand { InvoiceLineId = invoice.Lines[0].Id, Quantity = "1" } } }, s.F.Db.UserId, Ct);
                Assert.Equal("operation_key_conflict", conflict.ErrorCode);
                // The free quantity cannot be returned twice.
                var over = await s.Return(invoice.Id, freeLine.Id, "1", "fr4-all-zero-again");
                Assert.Equal("return_quantity_exceeded", over.ErrorCode);
            }
        }

        [Fact]
        public async Task Mixed_paid_and_zero_value_lines_return_in_one_document()
        {
            using (var s = new Setup())
            {
                await s.Prepare();
                var invoice = await s.Invoice();
                await s.F.FundRefund("12.34");
                var result = await s.F.Sales.ReturnAsync(invoice.Id, new SaleReturnCommand
                {
                    OperationKey = "fr4-mixed", Reason = "إرجاع مختلط",
                    Lines = new List<SaleReturnLineCommand> {
                        new SaleReturnLineCommand { InvoiceLineId = invoice.Lines[0].Id, Quantity = "1" },
                        new SaleReturnLineCommand { InvoiceLineId = invoice.Lines[1].Id, Quantity = "1" } }
                }, s.F.Db.UserId, Ct);
                Assert.True(result.Succeeded, result.ErrorMessage);
                Assert.Equal(123400, result.TotalScaled);
                Assert.Equal(123400, result.CashRefundScaled);
                Assert.Equal(0, result.CreditReductionScaled);
                using (var c = s.F.Db.Open())
                {
                    var amounts = c.Query<long>("SELECT AmountScaled FROM SalesReturnLines ORDER BY AmountScaled").ToArray();
                    Assert.Equal(new long[] { 0, 123400 }, amounts);
                    Assert.Equal(20000000, c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant", new { Variant = s.F.Variant }));
                    Assert.Equal(1000000, c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant", new { Variant = s.FreeVariant }));
                    // Sale deposit + cashbox funding deposit + refund withdrawal.
                    Assert.Equal(3, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
                Assert.Equal("0.00", (await s.F.Db.Service.ListAsync(Ct)).Single(x => x.Name == "مرتجعات البيع").Balance);
                var detail = await ReturnManagementTests.Service(s.F).GetAsync("sales", result.ReturnId, Ct);
                Assert.Equal("12.34", detail.Total); Assert.Equal("12.34", detail.Cash);
                Assert.Equal(2, detail.Lines.Count);
                Assert.Contains(detail.Lines, x => x.Amount == "0.00");
                Assert.Single(detail.Movements);
                var refreshed = await s.F.Sales.GetAsync(invoice.Id, Ct);
                Assert.Equal("full", refreshed.ReturnStatus); Assert.True(refreshed.IsFullyReturned);
            }
        }

        [Fact]
        public async Task Zero_value_credit_return_keeps_customer_debt_ledger_and_treasury_unchanged()
        {
            using (var s = new Setup())
            {
                await s.Prepare();
                var customer = new AlKhayal.Application.Customers.CustomerService(new SqliteCustomerRepository(s.F.Db.ConnectionString)).Create(new Customer { Code = "FR4-CREDIT", Name = "عميل مرتجع مجاني", Phone = "01000000000", Status = "نشط" });
                var invoice = await s.Invoice(paymentMode: "credit", customer: customer.Id);
                var result = await s.Return(invoice.Id, invoice.Lines[1].Id, "1", "fr4-credit-zero");
                Assert.True(result.Succeeded, result.ErrorMessage);
                Assert.Equal(0, result.TotalScaled);
                Assert.Equal(0, result.CashRefundScaled);
                Assert.Equal(0, result.CreditReductionScaled);
                using (var c = s.F.Db.Open())
                {
                    Assert.Equal(123400, c.ExecuteScalar<long>("SELECT CAST(ROUND(Balance*10000) AS INTEGER) FROM Customers WHERE Id=@Id", new { Id = customer.Id }));
                    // The credit sale opened the debt; the zero-value return adds no ledger entry.
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE TransactionType='reduction'"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT PaidScaled FROM SalesInvoices WHERE Id=@Id", new { Id = invoice.Id }));
                }
                Assert.Empty(await s.F.Db.Service.ListAsync(Ct));
                var refreshed = await s.F.Sales.GetAsync(invoice.Id, Ct);
                Assert.Equal("partial", refreshed.ReturnStatus);
            }
        }

        [Fact]
        public async Task Repeated_partial_zero_value_returns_restore_each_unit_once_and_cap_quantities()
        {
            using (var s = new Setup())
            {
                await s.Prepare(freeQuantity: "3");
                var invoice = await s.Invoice(freeQuantity: "3", freeLineDiscount: "0.03");
                var freeLine = invoice.Lines[1];
                long balance = 0;
                for (var n = 1; n <= 3; n++)
                {
                    var part = await s.Return(invoice.Id, freeLine.Id, "1", "fr4-part-" + n);
                    Assert.True(part.Succeeded, part.ErrorMessage);
                    Assert.Equal(0, part.TotalScaled);
                    balance = n;
                    using (var c = s.F.Db.Open())
                    {
                        Assert.Equal(balance * 1000000, c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant", new { Variant = s.FreeVariant }));
                        Assert.Equal(balance * 20000, c.ExecuteScalar<long>("SELECT ValueScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant", new { Variant = s.FreeVariant }));
                        Assert.Equal(0, c.ExecuteScalar<long>("SELECT SUM(AmountScaled) FROM SalesReturnLines WHERE SalesInvoiceLineId=@Line", new { Line = freeLine.Id }));
                    }
                }
                using (var c = s.F.Db.Open())
                {
                    // Three replacement layers parented to the original layer restore one unit each.
                    Assert.Equal(3, c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryCostLayers WHERE ParentLayerId=@Layer", new { Layer = s.LayerId }));
                    Assert.Equal(3000000, c.ExecuteScalar<long>("SELECT SUM(RemainingQuantityScaled) FROM InventoryCostLayers WHERE VariantId=@Variant", new { Variant = s.FreeVariant }));
                    Assert.Equal(60000, c.ExecuteScalar<long>("SELECT SUM(RemainingValueScaled) FROM InventoryCostLayers WHERE VariantId=@Variant", new { Variant = s.FreeVariant }));
                    Assert.Equal(3, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesReturnCostAllocations"));
                    Assert.Equal(60000, c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM SalesReturnCostAllocations"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COALESCE(NetQuantityBaseScaled,-1) FROM ProductPopularity WHERE ProductId=@Id", new { Id = s.FreeProduct }));
                }
                var over = await s.Return(invoice.Id, freeLine.Id, "1", "fr4-part-over");
                Assert.Equal("return_quantity_exceeded", over.ErrorCode);
                using (var c = s.F.Db.Open()) Assert.Equal(3, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesReturns"));
            }
        }

        [Fact]
        public async Task Fully_discounted_weighted_line_returns_zero_value_with_scale6_quantity_and_original_cost()
        {
            using (var s = new Setup())
            {
                await s.Prepare(freeQuantity: "10.000", freePrice: "200", freeCost: "100", sellingType: "weight");
                var invoice = await s.Invoice(freeQuantity: "2.500", freeLineDiscount: "500.00", freePrice: "200");
                var result = await s.Return(invoice.Id, invoice.Lines[1].Id, "0.250", "fr4-weight-zero");
                Assert.True(result.Succeeded, result.ErrorMessage);
                Assert.Equal(0, result.TotalScaled);
                Assert.Equal(0, result.CashRefundScaled);
                Assert.Equal(0, result.CreditReductionScaled);
                using (var c = s.F.Db.Open())
                {
                    var line = c.QuerySingle("SELECT QuantityScaled,AmountScaled FROM SalesReturnLines");
                    Assert.Equal(250000L, (long)line.QuantityScaled); Assert.Equal(0L, (long)line.AmountScaled);
                    // 10 kg received at 100/kg, 2.5 kg sold, 0.25 kg returned at the original batch cost.
                    var balanceSheet = c.QuerySingle("SELECT QuantityBaseScaled,ValueScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant", new { Variant = s.FreeVariant });
                    Assert.Equal(7750000L, (long)balanceSheet.QuantityBaseScaled); Assert.Equal(7750000L, (long)balanceSheet.ValueScaled);
                    var allocation = c.QuerySingle("SELECT QuantityScaled,ValueScaled FROM SalesReturnCostAllocations");
                    Assert.Equal(250000L, (long)allocation.QuantityScaled); Assert.Equal(250000L, (long)allocation.ValueScaled);
                    // Only the cash sale deposit exists; the zero-value return posts no treasury movement.
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
                Assert.True(await new SqliteInventoryStore(s.F.Db.ConnectionString).VerifyProjectionAsync(Ct));
            }
        }

        [Fact]
        public async Task Injected_failure_rolls_back_the_whole_zero_value_return_and_retry_succeeds()
        {
            using (var s = new Setup())
            {
                await s.Prepare();
                var invoice = await s.Invoice();
                using (var c = s.F.Db.Open()) c.Execute("CREATE TRIGGER Fr4ReturnFailure BEFORE INSERT ON SalesReturnLines BEGIN SELECT RAISE(ABORT,'injected'); END");
                var failed = await s.Return(invoice.Id, invoice.Lines[1].Id, "1", "fr4-rollback");
                Assert.False(failed.Succeeded);
                using (var c = s.F.Db.Open())
                {
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesReturns"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesReturnLines"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesReturnCostAllocations"));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant", new { Variant = s.FreeVariant }));
                    Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryDocuments WHERE Notes='salesReturn=fr4-rollback'"));
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments"));
                    c.Execute("DROP TRIGGER Fr4ReturnFailure");
                }
                var retry = await s.Return(invoice.Id, invoice.Lines[1].Id, "1", "fr4-rollback");
                Assert.True(retry.Succeeded, retry.ErrorMessage);
                Assert.Equal(0, retry.TotalScaled);
                using (var c = s.F.Db.Open())
                {
                    Assert.Equal(1000000, c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1 AND VariantId=@Variant", new { Variant = s.FreeVariant }));
                    Assert.Equal(1, c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesReturns"));
                }
                Assert.True((await s.Return(invoice.Id, invoice.Lines[1].Id, "1", "fr4-rollback")).Replayed);
            }
        }
    }
}
