using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Reports;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Database;
using AlKhayal.Data.Inventory;
using AlKhayal.Data.Sales;
using AlKhayal.Data.Reports;
using AlKhayal.Data.Returns;
using AlKhayal.Data.Customers;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class TouchPosInvoiceEditingTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;

        [Fact]
        public async Task Installment_returns_reduce_latest_unpaid_rows_then_refund_collected_excess_once()
        {
            using(var f=await Fixture.Create())
            {
                var account=CollectionTestAccounts.Account(f.ConnectionString,"cash");
                var sale=await f.Sales.CreateInstallmentAsync(new InstallmentSaleCommand{Invoice=f.Command(),DownPayment="2.00",InstallmentCount=2,IntervalMonths=1,FirstDueOn="2026-12-01",TreasuryAccountId=account,OperationKey="return-installment-sale"},1,Ct);
                Assert.True(sale.Succeeded,sale.ErrorMessage);
                var line=sale.Invoice.Lines.Single();
                // A half-unit return is exactly 6.00 and reduces the newest 5.00 row first.
                var first=new SaleReturnCommand{OperationKey="return-installment-half",Reason="مرتجع تقسيط جزئي",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="0.5"}}};
                var returned=await f.Sales.ReturnAsync(sale.Invoice.Id,first,1,Ct);Assert.True(returned.Succeeded,returned.ErrorMessage);
                Assert.Equal(60000,returned.CreditReductionScaled);Assert.Equal(0,returned.CashRefundScaled);
                var contracts=new SqliteCustomerInstallmentRepository(f.ConnectionString).GetByCustomer(1);
                var contract=Assert.Single(contracts);Assert.Equal(4m,contract.RemainingAmount);Assert.Equal(4m,contract.Schedule[0].RemainingAmount);Assert.Equal(0m,contract.Schedule[1].RemainingAmount);
                Assert.Equal(6m,contract.AdjustedTotalAmount);
                var report=await new OperationalReportService(new SqliteOperationalReportStore(f.ConnectionString)).QueryAsync("installments",new OperationalReportQuery{PersonId=1,From="2026-01-01",To="2027-12-31"},Ct);
                Assert.Equal("4.0000",report.Summary["balanceScaled"]);
                Assert.True((await f.Sales.ReturnAsync(sale.Invoice.Id,first,1,Ct)).Replayed);
                var paid=new SqliteCustomerInstallmentRepository(f.ConnectionString).Pay(1,sale.ContractId,1,"2026-12-01","return-installment-pay","return-installment-pay-hash","cash",account,1);
                Assert.Equal(0m,paid.RemainingAmount);
                var second=await f.Sales.ReturnAsync(sale.Invoice.Id,new SaleReturnCommand{OperationKey="return-installment-rest",Reason="مرتجع تقسيط كامل",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="0.5"}}},1,Ct);
                Assert.True(second.Succeeded,second.ErrorMessage);Assert.Equal(0,second.CreditReductionScaled);Assert.Equal(60000,second.CashRefundScaled);
                using(var c=f.Open())
                {
                    Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=1"));
                    Assert.Equal(10000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances"));
                    Assert.Equal(900000,c.ExecuteScalar<long>("SELECT ValueScaled FROM InventoryBalances"));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM ReturnTreasuryLinks WHERE SalesReturnId=@Id",new{Id=second.ReturnId}));
                    Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerInstallmentReturnAllocations WHERE SalesReturnId=@Id",new{Id=returned.ReturnId}));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerInstallmentPayments WHERE ContractId=@Id",new{Id=sale.ContractId}));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
            }
        }

        [Fact]
        public async Task Overdue_installment_return_reallocates_repeated_partials_and_rolls_back_on_failure()
        {
            using(var f=await Fixture.Create())
            {
                var sale=await f.Sales.CreateInstallmentAsync(new InstallmentSaleCommand{Invoice=f.Command(),DownPayment="0.00",InstallmentCount=3,IntervalMonths=1,FirstDueOn="2026-01-01",OperationKey="overdue-return-sale"},1,Ct);
                Assert.True(sale.Succeeded,sale.ErrorMessage);var line=sale.Invoice.Lines.Single();
                var command=new SaleReturnCommand{OperationKey="overdue-return-first",Reason="مرتجع تقسيط متأخر",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="0.25"}}};
                var snapshot=f.Snapshot(true);
                using(var c=f.Open())c.Execute("CREATE TRIGGER fail_installment_return BEFORE INSERT ON CustomerInstallmentReturnAllocations BEGIN SELECT RAISE(ABORT,'injected'); END;");
                Assert.False((await f.Sales.ReturnAsync(sale.Invoice.Id,command,1,Ct)).Succeeded);
                Assert.Equal(snapshot,f.Snapshot(true));
                using(var c=f.Open())c.Execute("DROP TRIGGER fail_installment_return");
                var first=await f.Sales.ReturnAsync(sale.Invoice.Id,command,1,Ct);Assert.True(first.Succeeded,first.ErrorMessage);
                command.OperationKey="overdue-return-second";var second=await f.Sales.ReturnAsync(sale.Invoice.Id,command,1,Ct);Assert.True(second.Succeeded,second.ErrorMessage);
                var contract=Assert.Single(new SqliteCustomerInstallmentRepository(f.ConnectionString).GetByCustomer(1));
                Assert.Equal(6m,contract.RemainingAmount);
                Assert.Equal(new[]{4m,2m,0m},contract.Schedule.Select(x=>x.RemainingAmount).ToArray());
                command.OperationKey="overdue-return-final";command.Lines[0].Quantity="0.5";
                var final=await f.Sales.ReturnAsync(sale.Invoice.Id,command,1,Ct);Assert.True(final.Succeeded,final.ErrorMessage);
                Assert.Equal(0m,Assert.Single(new SqliteCustomerInstallmentRepository(f.ConnectionString).GetByCustomer(1)).RemainingAmount);
                using(var c=f.Open()){Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=1"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));Assert.Equal(3,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesReturns"));}
            }
        }

        [Fact]
        public async Task Fully_paid_installment_return_refunds_collected_amount_without_rewriting_payments()
        {
            using(var f=await Fixture.Create())
            {
                var account=CollectionTestAccounts.Account(f.ConnectionString,"cash");
                var sale=await f.Sales.CreateInstallmentAsync(new InstallmentSaleCommand{Invoice=f.Command(),DownPayment="0.00",InstallmentCount=2,IntervalMonths=1,FirstDueOn="2026-12-01",OperationKey="paid-return-sale"},1,Ct);
                Assert.True(sale.Succeeded,sale.ErrorMessage);
                var repo=new SqliteCustomerInstallmentRepository(f.ConnectionString);
                repo.Pay(1,sale.ContractId,1,"2026-12-01","paid-return-row-one","paid-return-hash-one","cash",account,1);
                repo.Pay(1,sale.ContractId,2,"2027-01-01","paid-return-row-two","paid-return-hash-two","cash",account,1);
                var returned=await f.Sales.ReturnAsync(sale.Invoice.Id,new SaleReturnCommand{OperationKey="paid-installment-return",Reason="مرتجع عقد مسدد",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=sale.Invoice.Lines.Single().Id,Quantity="1"}}},1,Ct);
                Assert.True(returned.Succeeded,returned.ErrorMessage);Assert.Equal(0,returned.CreditReductionScaled);Assert.Equal(120000,returned.CashRefundScaled);
                using(var c=f.Open())
                {
                    Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerInstallmentPayments WHERE ContractId=@Id",new{Id=sale.ContractId}));
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerInstallmentReturnAllocations WHERE ContractId=@Id",new{Id=sale.ContractId}));
                    Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=1"));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM ReturnTreasuryLinks WHERE SalesReturnId=@Id",new{Id=returned.ReturnId}));
                }
            }
        }

        [Theory]
        [InlineData("card")]
        [InlineData("wallet")]
        public async Task Electronic_return_remains_pending_until_externally_confirmed_without_treasury_movement(string method)
        {
            using(var f=await Fixture.Create())
            {
                long instrument;
                using(var c=f.Open())instrument=c.ExecuteScalar<long>(@"INSERT INTO PaymentInstruments(Type,Name,Number,Provider,IdentityKey,IsActive,CreatedUtc,UpdatedUtc)
                    VALUES(@Method,'وسيلة رد','12345','اختبار',@Key,1,'2026-01-01','2026-01-01'); SELECT last_insert_rowid();",new{Method=method,Key=method+":12345"});
                var command=f.Command();command.PaymentMode="cash";command.ImmediateMethod=method;command.PaymentInstrumentId=instrument;
                var draft=await f.Sales.CreateDraftAsync(command,1,Ct);var posted=await f.Sales.PostAsync(draft.Id,"electronic-original-"+method,1,Ct);Assert.True(posted.Succeeded,posted.ErrorMessage);
                var result=await f.Sales.ReturnAsync(posted.Invoice.Id,new SaleReturnCommand{OperationKey="electronic-return-"+method,Reason="مرتجع دفع إلكتروني",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=posted.Invoice.Lines.Single().Id,Quantity="1"}}},1,Ct);
                Assert.True(result.Succeeded,result.ErrorMessage);
                using(var c=f.Open())
                {
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments WHERE EventType='refund'"));
                    Assert.Equal(120000,c.ExecuteScalar<long>("SELECT PaidScaled FROM SalesInvoices WHERE Id=@Id",new{Id=posted.Invoice.Id}));
                    Assert.Equal(method,c.ExecuteScalar<string>("SELECT Method FROM SalesElectronicRefunds WHERE SalesReturnId=@Id",new{Id=result.ReturnId}));
                }
                var read=new SqliteReturnReadStore(f.ConnectionString);Assert.Equal("pending",(await read.GetAsync("sales",result.ReturnId,Ct)).ElectronicRefundStatus);
                var confirmation=new ElectronicRefundConfirmation{OperationKey="electronic-confirm-"+method,ExternalReference="PROVIDER-REF-"+method};
                var confirmed=await f.Sales.ConfirmElectronicRefundAsync(result.ReturnId,confirmation,1,Ct);Assert.True(confirmed.Succeeded,confirmed.ErrorMessage);
                Assert.True((await f.Sales.ConfirmElectronicRefundAsync(result.ReturnId,confirmation,1,Ct)).Replayed);
                Assert.Equal("refund_already_confirmed",(await f.Sales.ConfirmElectronicRefundAsync(result.ReturnId,new ElectronicRefundConfirmation{OperationKey="other-"+method,ExternalReference="OTHER-REF"},1,Ct)).ErrorCode);
                Assert.Equal("confirmed",(await read.GetAsync("sales",result.ReturnId,Ct)).ElectronicRefundStatus);
                using(var c=f.Open())
                {
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments WHERE EventType='refund' AND SalesReturnId=@Id",new{Id=result.ReturnId}));
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT PaidScaled FROM SalesInvoices WHERE Id=@Id",new{Id=posted.Invoice.Id}));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
            }
        }

        [Fact]
        public async Task Corrected_card_invoice_returns_against_current_revision_and_original_payment()
        {
            using(var f=await Fixture.Create())
            {
                long instrument;using(var c=f.Open())instrument=c.ExecuteScalar<long>(@"INSERT INTO PaymentInstruments(Type,Name,Number,Provider,IdentityKey,IsActive,CreatedUtc,UpdatedUtc)
                    VALUES('card','بطاقة أصلية','12345','اختبار','card:correction',1,'2026-01-01','2026-01-01'); SELECT last_insert_rowid();");
                var command=f.Command();command.PaymentMode="cash";command.ImmediateMethod="card";command.PaymentInstrumentId=instrument;
                var draft=await f.Sales.CreateDraftAsync(command,1,Ct);var posted=await f.Sales.PostAsync(draft.Id,"corrected-card-original",1,Ct);Assert.True(posted.Succeeded,posted.ErrorMessage);
                var replacement=f.Command();replacement.PaymentMode="cash";replacement.ImmediateMethod="card";replacement.PaymentInstrumentId=instrument;
                var corrected=await f.Sales.CorrectAsync(posted.Invoice.Id,new SaleCorrectionCommand{ExpectedVersion=posted.Invoice.Version,OperationKey="corrected-card",SettlementKind="none",Invoice=replacement},1,Ct);
                Assert.True(corrected.Succeeded,corrected.ErrorMessage);
                var line=corrected.Invoice.Lines.Single();var returned=await f.Sales.ReturnAsync(corrected.Invoice.Id,new SaleReturnCommand{OperationKey="corrected-card-return",Reason="مرتجع بطاقة مصححة",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="1"}}},1,Ct);
                Assert.True(returned.Succeeded,returned.ErrorMessage);
                using(var c=f.Open())
                {
                    Assert.Equal(corrected.Invoice.Id,c.ExecuteScalar<long>("SELECT SalesInvoiceId FROM SalesElectronicRefunds WHERE SalesReturnId=@Id",new{Id=returned.ReturnId}));
                    Assert.Equal("cash-sale:corrected-card-original",c.ExecuteScalar<string>("SELECT OriginalPaymentKey FROM SalesElectronicRefunds WHERE SalesReturnId=@Id",new{Id=returned.ReturnId}));
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));
                }
            }
        }

        [Fact]
        public async Task Repeated_wallet_partials_and_confirmation_failure_never_duplicate_external_refunds()
        {
            using(var f=await Fixture.Create())
            {
                long instrument;using(var c=f.Open())instrument=c.ExecuteScalar<long>(@"INSERT INTO PaymentInstruments(Type,Name,Number,Provider,IdentityKey,IsActive,CreatedUtc,UpdatedUtc)
                    VALUES('wallet','محفظة أصلية','12345','اختبار','wallet:partial',1,'2026-01-01','2026-01-01'); SELECT last_insert_rowid();");
                var command=f.Command();command.PaymentMode="cash";command.ImmediateMethod="wallet";command.PaymentInstrumentId=instrument;command.Lines.Single().Quantity="2";
                var draft=await f.Sales.CreateDraftAsync(command,1,Ct);var posted=await f.Sales.PostAsync(draft.Id,"wallet-partial-original",1,Ct);Assert.True(posted.Succeeded,posted.ErrorMessage);
                var firstCommand=new SaleReturnCommand{OperationKey="wallet-partial-one",Reason="مرتجع محفظة أول",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=posted.Invoice.Lines.Single().Id,Quantity="1"}}};
                var first=await f.Sales.ReturnAsync(posted.Invoice.Id,firstCommand,1,Ct);Assert.True(first.Succeeded,first.ErrorMessage);Assert.True((await f.Sales.ReturnAsync(posted.Invoice.Id,firstCommand,1,Ct)).Replayed);
                firstCommand.OperationKey="wallet-partial-two";var second=await f.Sales.ReturnAsync(posted.Invoice.Id,firstCommand,1,Ct);Assert.True(second.Succeeded,second.ErrorMessage);
                Assert.Equal("return_quantity_exceeded",(await f.Sales.ReturnAsync(posted.Invoice.Id,new SaleReturnCommand{OperationKey="wallet-partial-three",Reason="مرتجع ثالث",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=posted.Invoice.Lines.Single().Id,Quantity="1"}}},1,Ct)).ErrorCode);
                var confirmation=new ElectronicRefundConfirmation{OperationKey="wallet-confirm-one",ExternalReference="WALLET-REF-ONE"};
                using(var c=f.Open())c.Execute("CREATE TRIGGER fail_wallet_confirmation BEFORE INSERT ON SalesPayments WHEN NEW.EventType='refund' BEGIN SELECT RAISE(ABORT,'injected'); END;");
                Assert.False((await f.Sales.ConfirmElectronicRefundAsync(first.ReturnId,confirmation,1,Ct)).Succeeded);
                using(var c=f.Open()){Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesElectronicRefundConfirmations"));Assert.Equal(240000,c.ExecuteScalar<long>("SELECT PaidScaled FROM SalesInvoices WHERE Id=@Id",new{Id=posted.Invoice.Id}));c.Execute("DROP TRIGGER fail_wallet_confirmation");}
                Assert.True((await f.Sales.ConfirmElectronicRefundAsync(first.ReturnId,confirmation,1,Ct)).Succeeded);
                Assert.True((await f.Sales.ConfirmElectronicRefundAsync(second.ReturnId,new ElectronicRefundConfirmation{OperationKey="wallet-confirm-two",ExternalReference="WALLET-REF-TWO"},1,Ct)).Succeeded);
                using(var c=f.Open()){Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesElectronicRefundConfirmations"));Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments WHERE EventType='refund'"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT PaidScaled FROM SalesInvoices WHERE Id=@Id",new{Id=posted.Invoice.Id}));}
            }
        }

        [Fact]
        public async Task Return_after_draft_edit_uses_only_posted_lines_and_quantities()
        {
            using(var f=await Fixture.Create())
            {
                var draft=await f.Sales.CreateDraftAsync(f.Command(),1,Ct);
                var oldLine=draft.Lines.Single().Id;
                var revised=f.Command();revised.ExpectedVersion=draft.Version;revised.Lines.Single().Quantity="2";revised.Lines.Single().UnitPrice="10.00";revised.Lines.Single().ManualPriceOverride=true;
                await f.Sales.UpdateDraftAsync(draft.Id,revised,1,Ct);
                var posted=await f.Sales.PostAsync(draft.Id,"edited-before-return",1,Ct);
                Assert.True(posted.Succeeded,posted.ErrorMessage);
                var line=posted.Invoice.Lines.Single();Assert.NotEqual(oldLine,line.Id);
                Assert.Equal("2",line.Quantity);Assert.Equal("10.00",line.UnitPrice);
                var stale=await f.Sales.ReturnAsync(posted.Invoice.Id,new SaleReturnCommand{OperationKey="return-edited-stale",Reason="مرتجع مسودة معدلة",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=oldLine,Quantity="1"}}},1,Ct);
                Assert.False(stale.Succeeded);
                var current=await f.Sales.ReturnAsync(posted.Invoice.Id,new SaleReturnCommand{OperationKey="return-edited-current",Reason="مرتجع مسودة معدلة",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="1"}}},1,Ct);
                Assert.True(current.Succeeded,current.ErrorMessage);Assert.Equal(100000,current.TotalScaled);
                using(var c=f.Open())
                {
                    Assert.Equal(line.Id,c.ExecuteScalar<long>("SELECT SalesInvoiceLineId FROM SalesReturnLines WHERE SalesReturnId=@Id",new{Id=current.ReturnId}));
                    Assert.Equal(9000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances"));
                    Assert.Equal(10m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=1"));
                }
            }
        }

        [Fact]
        public async Task Return_lookup_uses_latest_corrected_lines_and_rejects_superseded_invoice()
        {
            using(var f=await Fixture.Create())
            {
                var draft=await f.Sales.CreateDraftAsync(f.Command(),1,Ct);
                var posted=await f.Sales.PostAsync(draft.Id,"return-correction-original",1,Ct);
                Assert.True(posted.Succeeded,posted.ErrorMessage);
                var replacement=f.Command();replacement.Lines.Single().Quantity="2";
                var corrected=await f.Sales.CorrectAsync(posted.Invoice.Id,new SaleCorrectionCommand{ExpectedVersion=posted.Invoice.Version,OperationKey="return-correction",SettlementKind="receivable",Invoice=replacement},1,Ct);
                Assert.True(corrected.Succeeded,corrected.ErrorMessage);
                var read=new SqliteReturnReadStore(f.ConnectionString);
                Assert.Equal(corrected.Invoice.Id,await read.FindInvoiceAsync("sales",posted.Invoice.InvoiceNumber,Ct));
                Assert.Equal(corrected.Invoice.Id,await read.FindInvoiceAsync("sales",posted.Invoice.InvoiceNumber+"-R1",Ct));
                var oldLine=posted.Invoice.Lines.Single();
                var rejected=await f.Sales.ReturnAsync(posted.Invoice.Id,new SaleReturnCommand{OperationKey="return-old-revision",Reason="مرتجع تصحيح",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=oldLine.Id,Quantity="1"}}},1,Ct);
                Assert.False(rejected.Succeeded);
                var current=await f.Sales.GetAsync(corrected.Invoice.Id,Ct);
                var result=await f.Sales.ReturnAsync(current.Id,new SaleReturnCommand{OperationKey="return-current-revision",Reason="مرتجع تصحيح",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=current.Lines.Single().Id,Quantity="1"}}},1,Ct);
                Assert.True(result.Succeeded,result.ErrorMessage);
                Assert.Equal(posted.Invoice.InvoiceNumber,Assert.Single(await read.ListAsync(posted.Invoice.InvoiceNumber,Ct)).InvoiceNumber);
                using(var c=f.Open())
                {
                    Assert.Equal(current.Id,c.ExecuteScalar<long>("SELECT SalesInvoiceId FROM SalesReturns WHERE Id=@Id",new{Id=result.ReturnId}));
                    Assert.Equal(current.Lines.Single().Id,c.ExecuteScalar<long>("SELECT SalesInvoiceLineId FROM SalesReturnLines WHERE SalesReturnId=@Id",new{Id=result.ReturnId}));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
            }
        }

        [Fact]
        public async Task Return_of_out_of_stock_sale_restores_only_original_fifo_stock()
        {
            using(var f=await Fixture.Create())
            {
                using(var c=f.Open())c.Execute("UPDATE ProductDetails SET AllowSaleWhenOutOfStock=1");
                var command=f.Command();command.Lines.Single().Quantity="12";
                var draft=await f.Sales.CreateDraftAsync(command,1,Ct);
                var posted=await f.Sales.PostAsync(draft.Id,"return-short-stock-sale",1,Ct);
                Assert.True(posted.Succeeded,posted.ErrorMessage);
                var line=posted.Invoice.Lines.Single();
                var returned=await f.Sales.ReturnAsync(posted.Invoice.Id,new SaleReturnCommand{OperationKey="return-short-stock",Reason="مرتجع مخزون ناقص",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="12"}}},1,Ct);
                Assert.True(returned.Succeeded,returned.ErrorMessage);
                Assert.True((await f.Sales.ReturnAsync(posted.Invoice.Id,new SaleReturnCommand{OperationKey="return-short-stock",Reason="مرتجع مخزون ناقص",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=line.Id,Quantity="12"}}},1,Ct)).Replayed);
                using(var c=f.Open())
                {
                    Assert.Equal(10000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances"));
                    Assert.Equal(900000,c.ExecuteScalar<long>("SELECT ValueScaled FROM InventoryBalances"));
                    Assert.Equal(10000000,c.ExecuteScalar<long>("SELECT COALESCE(SUM(QuantityScaled),0) FROM SalesReturnCostAllocations"));
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryCostLayers WHERE SourceMovementId IN (SELECT Id FROM StockMovements WHERE DocumentId=@Id) AND ValueScaled=0",new{Id=returned.InventoryDocumentId}));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesReturns"));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
                Assert.True(await new SqliteInventoryStore(f.ConnectionString).VerifyProjectionAsync(Ct));
            }
        }

        [Theory]
        [InlineData("cash")]
        [InlineData("credit")]
        public async Task Resumed_draft_survives_restart_failed_post_and_replay_without_duplicate_effects(string method)
        {
            using(var f=await Fixture.Create())
            {
                var command=f.Command();command.PaymentMode=method;command.ImmediateMethod=method;
                var draft=await f.Sales.CreateDraftAsync(command,1,Ct);
                var untouched=f.Snapshot();
                command.ExpectedVersion=draft.Version;command.Lines.Single().Quantity="2";
                var saved=await f.Sales.UpdateDraftAsync(draft.Id,command,1,Ct);
                Assert.Equal(draft.Id,saved.Id);Assert.Equal(draft.InvoiceNumber,saved.InvoiceNumber);
                Assert.Equal(untouched,f.Snapshot());
                // Recreate the service/store: no in-memory draft identity is required.
                var restarted=new SalesService(new SqliteSalesStore(f.ConnectionString));
                var reopened=await restarted.GetAsync(draft.Id,Ct);
                Assert.Equal("draft",reopened.Status);Assert.Equal("2",reopened.Lines.Single().Quantity);
                Assert.Equal(saved.Version,reopened.Version);Assert.Equal(method,reopened.ImmediateMethod);
                var beforeFailure=f.Snapshot(true);
                using(var c=f.Open())c.Execute("CREATE TRIGGER fail_resumed_post BEFORE INSERT ON SalesInvoiceAudit WHEN NEW.EventType='posted' BEGIN SELECT RAISE(ABORT,'resumed_post_failure'); END;");
                var failed=await restarted.PostAsync(draft.Id,"resume-post",1,Ct);
                Assert.False(failed.Succeeded);Assert.Equal(beforeFailure,f.Snapshot(true));
                Assert.Equal("draft",(await restarted.GetAsync(draft.Id,Ct)).Status);
                using(var c=f.Open())c.Execute("DROP TRIGGER fail_resumed_post");
                var posted=await restarted.PostAsync(draft.Id,"resume-post",1,Ct);
                Assert.True(posted.Succeeded,posted.ErrorMessage);Assert.Equal(draft.Id,posted.Invoice.Id);
                Assert.Equal(draft.InvoiceNumber,posted.Invoice.InvoiceNumber);Assert.Equal("posted",posted.Invoice.Status);
                var after=f.Snapshot(true);
                Assert.True((await restarted.PostAsync(draft.Id,"resume-post",1,Ct)).Succeeded);
                Assert.Equal(after,f.Snapshot(true));
                Assert.False((await restarted.PostAsync(draft.Id,"different-key",1,Ct)).Succeeded);
                await Assert.ThrowsAsync<InvalidOperationException>(()=>restarted.UpdateDraftAsync(draft.Id,command,1,Ct));
                Assert.Equal(after,f.Snapshot(true));
                using(var c=f.Open())
                {
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesInvoices"));
                    Assert.Equal(8000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1"));
                    Assert.Equal(180000,c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM InventoryCostAllocations"));
                    Assert.Equal(method=="credit"?24m:0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=1"));
                    Assert.Equal(method=="cash"?1:0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesPayments"));
                    Assert.Equal(method=="cash"?1:0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));
                    Assert.Equal(method=="cash"?240000:0,c.ExecuteScalar<long>("SELECT COALESCE(SUM(AmountScaled),0) FROM SalesPayments"));
                    Assert.Equal(method=="credit"?240000:0,c.ExecuteScalar<long>("SELECT COALESCE(SUM(AmountScaled),0) FROM CustomerTransactions"));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
                Assert.True(await new SqliteInventoryStore(f.ConnectionString).VerifyProjectionAsync(Ct));
            }
        }

        [Fact]
        public async Task Draft_view_discard_save_stale_retry_and_post_preserve_reconciliation()
        {
            using(var f = await Fixture.Create())
            {
                var original = await f.Sales.CreateDraftAsync(f.Command(),1,Ct);
                var before = f.Snapshot();
                var viewed = await f.Sales.GetAsync(original.Id,Ct);
                Assert.Equal(original.InvoiceNumber,viewed.InvoiceNumber);
                Assert.Equal("عميل الاختبار",viewed.CustomerName);
                Assert.Equal(original.TotalScaled,viewed.TotalScaled);
                Assert.Equal("12.00",viewed.Lines.Single().UnitPrice);
                // Touch POS keeps edits locally until Save; a fresh read must still be original.
                viewed.Lines.Single().Quantity="8";
                Assert.Equal("1",(await f.Sales.GetAsync(original.Id,Ct)).Lines.Single().Quantity);
                Assert.Equal(before,f.Snapshot());

                var update=f.Command();update.ExpectedVersion=original.Version;
                update.Lines.Single().Quantity="2";update.Lines.Single().Discount="1.00";update.Discount="2.00";
                var saved=await f.Sales.UpdateDraftAsync(original.Id,update,1,Ct);
                Assert.Equal(original.Id,saved.Id);Assert.Equal(original.InvoiceNumber,saved.InvoiceNumber);
                Assert.Equal(original.Version+1,saved.Version);Assert.Equal(210000,saved.TotalScaled);
                Assert.Equal(before,f.Snapshot()); // draft edits never touch financial projections
                var savedDocument=f.Snapshot(true);
                await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Sales.UpdateDraftAsync(original.Id,update,1,Ct));
                Assert.Equal(savedDocument,f.Snapshot(true));
                Assert.Equal(saved.Version,(await f.Sales.GetAsync(saved.Id,Ct)).Version);
                Assert.Equal(before,f.Snapshot());

                var invalid=f.Command();invalid.ExpectedVersion=saved.Version;invalid.Lines.Single().UnitPrice="13.00";
                await Assert.ThrowsAsync<ArgumentException>(()=>f.Sales.UpdateDraftAsync(saved.Id,invalid,1,Ct));
                Assert.Equal(210000,(await f.Sales.GetAsync(saved.Id,Ct)).TotalScaled);Assert.Equal(before,f.Snapshot());Assert.Equal(savedDocument,f.Snapshot(true));
                var posted=await f.Sales.PostAsync(saved.Id,"edit-post",1,Ct);Assert.True(posted.Succeeded,posted.ErrorMessage);
                var afterPost=f.Snapshot();
                Assert.True((await f.Sales.PostAsync(saved.Id,"edit-post",1,Ct)).Succeeded);
                Assert.Equal(afterPost,f.Snapshot());
                using(var c=f.Open())
                {
                    Assert.Equal(21m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=1"));
                    Assert.Equal(8000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances WHERE WarehouseId=1"));
                    Assert.Equal(180000,c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM InventoryCostAllocations"));
                    Assert.Equal(210000,c.ExecuteScalar<long>("SELECT SUM(AmountScaled) FROM CustomerTransactions WHERE TransactionType='increase'"));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
                Assert.True(await new SqliteInventoryStore(f.ConnectionString).VerifyProjectionAsync(Ct));
            }
        }

        [Fact]
        public async Task Previous_business_day_and_injected_failure_leave_original_effective()
        {
            using(var f=await Fixture.Create())
            {
                var draft=await f.Sales.CreateDraftAsync(f.Command(),1,Ct);var posted=await f.Sales.PostAsync(draft.Id,"rollback-original",1,Ct);Assert.True(posted.Succeeded,posted.ErrorMessage);
                var store=new SqliteSalesStore(f.ConnectionString);var priorDay=await store.GetForEditAsync(posted.Invoice.Id,1,"2026-11-02",Ct);Assert.True(priorDay.CanEditPosted);
                var before=f.Snapshot(true);using(var c=f.Open())c.Execute("CREATE TRIGGER fail_correction BEFORE INSERT ON SalesInvoiceCorrections BEGIN SELECT RAISE(ABORT,'injected'); END;");
                var lower=f.Command();lower.Lines.Single().Quantity="0.5";var failed=await f.Sales.CorrectAsync(posted.Invoice.Id,new SaleCorrectionCommand{ExpectedVersion=posted.Invoice.Version,OperationKey="rollback-correction",SettlementKind="customer_credit",Invoice=lower},1,Ct);Assert.False(failed.Succeeded);Assert.Equal("sales_conflict",failed.ErrorCode);
                using(var c=f.Open())c.Execute("DROP TRIGGER fail_correction");Assert.Equal(before,f.Snapshot(true));Assert.True((await f.Sales.GetForEditAsync(posted.Invoice.Id,1,Ct)).CanEditPosted);Assert.True(await new SqliteInventoryStore(f.ConnectionString).VerifyProjectionAsync(Ct));
            }
        }

        [Theory]
        [InlineData("cash")]
        [InlineData("credit")]
        [InlineData("paid")]
        [InlineData("returned")]
        [InlineData("canceled")]
        [InlineData("deleted")]
        public async Task Unsupported_invoice_edits_leave_all_financial_history_unchanged(string scenario)
        {
            using(var f=await Fixture.Create())
            {
                var command=f.Command();if(scenario=="cash"){command.PaymentMode="cash";command.ImmediateMethod="cash";}
                var invoice=await f.Sales.CreateDraftAsync(command,1,Ct);
                if(scenario=="deleted")Assert.True((await f.Sales.DeleteDraftAsync(invoice.Id,"delete",1,Ct)).Succeeded);
                else
                {
                    var posted=await f.Sales.PostAsync(invoice.Id,"unsupported-post",1,Ct);Assert.True(posted.Succeeded,posted.ErrorMessage);invoice=posted.Invoice;
                    if(scenario=="paid")Assert.True((await f.Sales.AddPaymentAsync(invoice.Id,new SalePaymentCommand{Amount="2.00",Method="cash",OccurredOn="2026-11-01",OperationKey="paid",TreasuryAccountId=CollectionTestAccounts.Account(f.ConnectionString,"cash")},1,Ct)).Succeeded);
                    if(scenario=="returned")Assert.True((await f.Sales.ReturnAsync(invoice.Id,new SaleReturnCommand{OperationKey="returned",Reason="اختبار الإرجاع",Lines=new(){new SaleReturnLineCommand{InvoiceLineId=invoice.Lines.Single().Id,Quantity="0.5"}}},1,Ct)).Succeeded);
                    if(scenario=="canceled")Assert.True((await f.Sales.CancelAsync(invoice.Id,"cancel","اختبار الإلغاء",1,Ct)).Succeeded);
                }
                var before=f.Snapshot(true);command.ExpectedVersion=invoice.Version;command.Lines.Single().Quantity="3";
                await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Sales.UpdateDraftAsync(invoice.Id,command,1,Ct));
                Assert.Equal(before,f.Snapshot(true));
                Assert.True(await new SqliteInventoryStore(f.ConnectionString).VerifyProjectionAsync(Ct));
            }
        }

        [Fact]
        public async Task Same_day_posted_credit_invoice_is_revised_atomically_and_replay_is_idempotent()
        {
            using(var f=await Fixture.Create())
            {
                var draft=await f.Sales.CreateDraftAsync(f.Command(),1,Ct);var posted=await f.Sales.PostAsync(draft.Id,"correction-original",1,Ct);Assert.True(posted.Succeeded,posted.ErrorMessage);
                var editable=await f.Sales.GetForEditAsync(posted.Invoice.Id,1,Ct);Assert.True(editable.CanEditPosted,editable.EditRestrictionMessage);
                var replacement=f.Command();replacement.Lines.Single().Quantity="0.5";replacement.Lines.Single().UnitPrice="10.00";
                var command=new SaleCorrectionCommand{ExpectedVersion=editable.Version,OperationKey="correction-one",SettlementKind="customer_credit",Invoice=replacement};
                var corrected=await f.Sales.CorrectAsync(editable.Id,command,1,Ct);Assert.True(corrected.Succeeded,corrected.ErrorMessage);
                Assert.Equal(editable.InvoiceNumber,corrected.Invoice.InvoiceNumber);Assert.Equal(1,corrected.Invoice.Revision);Assert.Equal(50000,corrected.Invoice.TotalScaled);
                var snapshot=f.Snapshot(true);var replay=await f.Sales.CorrectAsync(editable.Id,command,1,Ct);Assert.True(replay.Succeeded);Assert.True(replay.Replayed);Assert.Equal(snapshot,f.Snapshot(true));
                using(var c=f.Open())
                {
                    Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesInvoices WHERE Status='posted'"));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesInvoiceCorrections"));
                    Assert.Equal(9500000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances"));
                    Assert.Equal(5m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=1"));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesInvoices i WHERE Status='posted' AND NOT EXISTS(SELECT 1 FROM SalesInvoiceCorrections x WHERE x.PreviousInvoiceId=i.Id)"));
                    Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
                }
                var higher=f.Command();higher.Lines.Single().Quantity="1";higher.Lines.Single().UnitPrice="10.00";
                var second=await f.Sales.CorrectAsync(corrected.Invoice.Id,new SaleCorrectionCommand{ExpectedVersion=corrected.Invoice.Version,OperationKey="correction-two",SettlementKind="receivable",Invoice=higher},1,Ct);Assert.True(second.Succeeded,second.ErrorMessage);Assert.Equal(2,second.Invoice.Revision);Assert.Equal(editable.InvoiceNumber,second.Invoice.InvoiceNumber);
                using(var c=f.Open()){Assert.Equal(10m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=1"));Assert.Equal(9000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances"));Assert.Equal(2,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesInvoiceCorrections"));}
                var postedDay=DateTimeOffset.Parse(posted.Invoice.PostedUtc,System.Globalization.CultureInfo.InvariantCulture).ToLocalTime().ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
                using(var c=f.Open())
                {
                    var revisions=c.Query<(long Id,string PostedUtc)>("SELECT Id,PostedUtc FROM SalesInvoices WHERE RootInvoiceId=@Root OR Id=@Root ORDER BY Revision",new{Root=posted.Invoice.Id}).ToList();
                    Assert.Equal(3,revisions.Count);
                    Assert.All(revisions,x=>Assert.Equal(posted.Invoice.PostedUtc,x.PostedUtc));
                    Assert.Equal(second.Invoice.Id,revisions.Last().Id);
                }
                var salesReport=await new SalesReportService(new SqliteSalesReportStore(f.ConnectionString)).QueryAsync(new SalesReportQuery{From=postedDay,To=postedDay},Ct);Assert.Equal(1,salesReport.Count);Assert.Equal("10.0000",salesReport.Total);Assert.Equal(second.Invoice.Id,salesReport.Items.Single().Id);Assert.Equal(editable.InvoiceNumber,salesReport.Items.Single().InvoiceNumber);
                var movements=await new AlKhayal.Application.Reports.StockReportService(new AlKhayal.Data.Reports.SqliteStockReportStore(f.ConnectionString),TimeZoneInfo.Utc).QueryAsync("inventory-movements",new AlKhayal.Application.Reports.StockReportQuery{From="2000-01-01",To="2099-12-31",Type="sale",PageSize=100},Ct);
                Assert.NotEmpty(movements.Items);
                Assert.All(movements.Items,x=>{Assert.Equal("pos",x.Route);Assert.Equal(second.Invoice.Id,x.DocumentId);});
                var profit=await new NetProfitService(new SqliteNetProfitStore(f.ConnectionString)).QueryAsync(new NetProfitQuery{From=postedDay,To=postedDay},Ct);Assert.Equal("10.0000",profit.Summary.Revenue);Assert.Equal("9.0000",profit.Summary.Cogs);Assert.Equal("1.0000",profit.Summary.GrossProfit);Assert.Equal(1,profit.Summary.EventCount);
                Assert.True(await new SqliteInventoryStore(f.ConnectionString).VerifyProjectionAsync(Ct));
            }
        }

        [Fact]
        public async Task Shift_origin_must_be_the_current_open_shift_and_closed_shift_is_rejected()
        {
            using(var f=await Fixture.Create())
            {
                using(var c=f.Open())c.Execute("INSERT INTO ModuleConfiguration(ModuleKey,IsEnabled,OverrideState,UpdatedUtc,ChangedByUserId) VALUES('advanced-shifts',1,'administrator','2026-11-01T00:00:00Z',1)");
                var draft=await f.Sales.CreateDraftAsync(f.Command(),1,Ct);var posted=await f.Sales.PostAsync(draft.Id,"shift-correction-original",1,Ct);Assert.True(posted.Succeeded,posted.ErrorMessage);
                Assert.True((await f.Sales.GetForEditAsync(posted.Invoice.Id,1,Ct)).CanEditPosted);
                using(var c=f.Open())c.Execute("UPDATE CashierShifts SET ClosedUtc='2026-11-01T12:00:00Z',ClosedBy=1,CountedScaled=0,ClosingExpectedScaled=120000 WHERE Id=(SELECT ShiftId FROM SalesInvoiceShiftOrigins WHERE SalesInvoiceId=@Id)",new{Id=posted.Invoice.Id});
                var closed=await f.Sales.GetForEditAsync(posted.Invoice.Id,1,Ct);Assert.False(closed.CanEditPosted);Assert.Equal("different_or_closed_shift",closed.EditRestrictionCode);
            }
        }

        [Fact]
        public async Task Tracked_unit_and_exact_fifo_cost_are_reversed_and_reapplied_without_new_identity()
        {
            using(var f=await Fixture.Create())
            {
                var catalog=new CatalogService(new SqliteCatalogStore(f.ConnectionString));var product=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="correction-serial-product",Name="جهاز تصحيح",BaseUnitId=1,ProductType="stock",SalePrice="12",PurchasePrice="9",IsActive=true},Ct);Assert.True(product.Succeeded,product.ErrorMessage);
                long variant,unit;using(var c=f.Open()){variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=product.Value.Id});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=product.Value.Id});c.Execute("INSERT INTO ProductCapabilityAssignments(ProductId,CapabilityKey,IsEnabled,AssignedUtc,UpdatedUtc,ChangedByUserId) VALUES(@Id,'serial-numbers',1,'2026-11-01','2026-11-01',1),(@Id,'imei-tracking',1,'2026-11-01','2026-11-01',1)",new{Id=product.Value.Id});}
                var stock=await new SqliteInventoryStore(f.ConnectionString).PostAsync(new InventoryPostCommand{OperationKey="correction-serial-receipt",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد جهاز",Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitCost="9",SalePrice="12",PhysicalUnits=new[]{new InventoryUnitInput{Serial="CORR-SERIAL-1",Imeis=new[]{"490154203237518"}}}}}},"correction-serial-receipt",Ct);Assert.True(stock.Succeeded,stock.ErrorMessage);
                var originalCommand=new SaleDraftCommand{CustomerId=1,WarehouseId=1,InvoiceDate="2026-11-01",PaymentMode="credit",ImmediateMethod="credit",Discount="0.00",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitPrice="12.00",Discount="0.00",PhysicalUnits=new(){new SaleTrackingUnit{Serial="CORR-SERIAL-1",Imeis=new(){"490154203237518"}}}}}};
                var draft=await f.Sales.CreateDraftAsync(originalCommand,1,Ct);var posted=await f.Sales.PostAsync(draft.Id,"correction-serial-sale",1,Ct);Assert.True(posted.Succeeded,posted.ErrorMessage);
                var replacement=new SaleDraftCommand{CustomerId=1,WarehouseId=1,InvoiceDate="2026-11-01",PaymentMode="credit",ImmediateMethod="credit",Discount="0.00",Lines=new(){new SaleLine{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitPrice="10.00",Discount="0.00",PhysicalUnits=new(){new SaleTrackingUnit{Serial="CORR-SERIAL-1",Imeis=new(){"490154203237518"}}}}}};
                var corrected=await f.Sales.CorrectAsync(posted.Invoice.Id,new SaleCorrectionCommand{ExpectedVersion=posted.Invoice.Version,OperationKey="correction-serial-edit",SettlementKind="customer_credit",Invoice=replacement},1,Ct);Assert.True(corrected.Succeeded,corrected.ErrorMessage);
                using(var c=f.Open()){Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryPhysicalUnits WHERE WarehouseId IS NOT NULL AND Id=(SELECT PhysicalUnitId FROM InventoryIdentifiers WHERE NormalizedValue='CORR-SERIAL-1')"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryIdentifiers WHERE NormalizedValue='CORR-SERIAL-1'"));Assert.Equal(4,c.ExecuteScalar<long>("SELECT COUNT(*) FROM StockMovementUnits WHERE PhysicalUnitId=(SELECT PhysicalUnitId FROM InventoryIdentifiers WHERE NormalizedValue='CORR-SERIAL-1')"));Assert.Equal(180000,c.ExecuteScalar<long>("SELECT SUM(a.ValueScaled) FROM InventoryCostAllocations a JOIN StockMovements m ON m.Id=a.MovementId WHERE m.VariantId=@Variant",new{Variant=variant}));}
                Assert.True(await new SqliteInventoryStore(f.ConnectionString).VerifyProjectionAsync(Ct));
            }
        }

        [Fact]
        public async Task Zero_stock_sale_revision_preserves_uncovered_quantity_without_negative_stock_or_invented_cost()
        {
            using(var f=await Fixture.Create())
            {
                var original=f.Command();original.Lines.Single().Quantity="20";var draft=await f.Sales.CreateDraftAsync(original,1,Ct);var posted=await f.Sales.PostAsync(draft.Id,"zero-stock-original",1,Ct);Assert.True(posted.Succeeded,posted.ErrorMessage);
                var replacement=f.Command();replacement.Lines.Single().Quantity="25";var corrected=await f.Sales.CorrectAsync(posted.Invoice.Id,new SaleCorrectionCommand{ExpectedVersion=posted.Invoice.Version,OperationKey="zero-stock-correction",SettlementKind="receivable",Invoice=replacement},1,Ct);Assert.True(corrected.Succeeded,corrected.ErrorMessage);
                using(var c=f.Open()){Assert.Equal(0,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM InventoryBalances"));Assert.Equal(0,c.ExecuteScalar<long>("SELECT ValueScaled FROM InventoryBalances"));Assert.Equal(1800000,c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM InventoryCostAllocations"));Assert.Equal(-10000000,c.ExecuteScalar<long>("SELECT QuantityBaseScaled FROM StockMovements WHERE DocumentId=(SELECT InventoryDocumentId FROM SalesInvoices WHERE Id=@Id)",new{Id=corrected.Invoice.Id}));Assert.Equal(300m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=1"));}
                Assert.True(await new SqliteInventoryStore(f.ConnectionString).VerifyProjectionAsync(Ct));
            }
        }

        [Fact]
        public async Task Partial_credit_payment_requires_customer_credit_instead_of_physical_refund()
        {
            using(var f=await Fixture.Create())
            {
                var draft=await f.Sales.CreateDraftAsync(f.Command(),1,Ct);var posted=await f.Sales.PostAsync(draft.Id,"partial-original",1,Ct);Assert.True(posted.Succeeded,posted.ErrorMessage);
                Assert.True((await f.Sales.AddPaymentAsync(posted.Invoice.Id,new SalePaymentCommand{Amount="5.00",Method="cash",OperationKey="partial-collection",OccurredOn="2026-11-01",TreasuryAccountId=CollectionTestAccounts.Account(f.ConnectionString,"cash")},1,Ct)).Succeeded);
                var lower=f.Command();lower.Lines.Single().UnitPrice="10.00";
                var rejected=await f.Sales.CorrectAsync(posted.Invoice.Id,new SaleCorrectionCommand{ExpectedVersion=posted.Invoice.Version,OperationKey="partial-refund",SettlementKind="refund",SettlementMethod="cash",TreasuryAccountId=1,Invoice=lower},1,Ct);Assert.False(rejected.Succeeded);Assert.Equal("refund_exceeds_paid",rejected.ErrorCode);
                var corrected=await f.Sales.CorrectAsync(posted.Invoice.Id,new SaleCorrectionCommand{ExpectedVersion=posted.Invoice.Version,OperationKey="partial-credit",SettlementKind="customer_credit",Invoice=lower},1,Ct);Assert.True(corrected.Succeeded,corrected.ErrorMessage);Assert.Equal(50000,corrected.Invoice.PaidScaled);Assert.Equal(50000,corrected.Invoice.OutstandingScaled);
                using(var c=f.Open()){Assert.Equal(5m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=1"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));}
            }
        }

        [Fact]
        public async Task Concurrent_corrections_post_exactly_one_replacement()
        {
            using(var f=await Fixture.Create())
            {
                var draft=await f.Sales.CreateDraftAsync(f.Command(),1,Ct);var posted=await f.Sales.PostAsync(draft.Id,"concurrent-correction-original",1,Ct);Assert.True(posted.Succeeded,posted.ErrorMessage);
                var a=f.Command();a.Lines.Single().UnitPrice="10.00";var b=f.Command();b.Lines.Single().UnitPrice="9.00";
                var results=await Task.WhenAll(f.Sales.CorrectAsync(posted.Invoice.Id,new SaleCorrectionCommand{ExpectedVersion=posted.Invoice.Version,OperationKey="concurrent-correction-a",SettlementKind="customer_credit",Invoice=a},1,Ct),new SalesService(new SqliteSalesStore(f.ConnectionString)).CorrectAsync(posted.Invoice.Id,new SaleCorrectionCommand{ExpectedVersion=posted.Invoice.Version,OperationKey="concurrent-correction-b",SettlementKind="customer_credit",Invoice=b},1,Ct));
                Assert.Single(results,x=>x.Succeeded);Assert.Single(results,x=>!x.Succeeded);using(var c=f.Open()){Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesInvoiceCorrections"));Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesInvoices i WHERE i.Status='posted' AND NOT EXISTS(SELECT 1 FROM SalesInvoiceCorrections correction WHERE correction.PreviousInvoiceId=i.Id)"));}
            }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly string _path=Path.Combine(Path.GetTempPath(),"touch-edit-"+Guid.NewGuid()+".db");
            public string ConnectionString {get;private set;}
            public SalesService Sales {get;private set;}
            private long _variant,_unit;
            public SQLiteConnection Open(){var c=new SQLiteConnection(ConnectionString);c.Open();return c;}
            public static async Task<Fixture> Create()
            {
                var f=new Fixture();f.ConnectionString=Db.ConnectionStringFor(f._path);
                try
                {
                    Migrator.Run(f.ConnectionString);
                    using(var c=f.Open())c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('edit-test','test-only','2026-01-01T00:00:00Z'); INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('EDIT','عميل الاختبار','0100','',0,1000,'أساسي','نشط','2026-01-01','2026-01-01')");
                    var product=await new CatalogService(new SqliteCatalogStore(f.ConnectionString)).CreateProductAsync(new CreateProductCommand{OperationKey="edit-product",Name="صنف الاختبار",BaseUnitId=1,ProductType="stock",SalePrice="12",PurchasePrice="9",IsActive=true},Ct);
                    Assert.True(product.Succeeded,product.ErrorMessage);
                    using(var c=f.Open()){f._variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=product.Value.Id});f._unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=product.Value.Id});}
                    var receipt=await new SqliteInventoryStore(f.ConnectionString).PostAsync(new InventoryPostCommand{OperationKey="edit-receipt",Type="manual_receipt",WarehouseId=1,UserId=1,Reason="توريد الاختبار",Lines=new[]{new InventoryLineInput{VariantId=f._variant,ProductUnitId=f._unit,Quantity="10",UnitCost="9",SalePrice="12"}}},"edit-receipt-hash",Ct);
                    Assert.True(receipt.Succeeded,receipt.ErrorMessage);
                    using(var c=f.Open())c.Execute("INSERT INTO CashierShifts(CashierId,RegisterId,OpenedUtc,OpenedBy,OpeningScaled) VALUES(1,'standalone','2026-11-01T00:00:00Z',1,0)");
                    f.Sales=new SalesService(new SqliteSalesStore(f.ConnectionString));return f;
                }
                catch{f.Dispose();throw;}
            }
            public SaleDraftCommand Command(){return new SaleDraftCommand{CustomerId=1,WarehouseId=1,InvoiceDate="2026-11-01",PaymentMode="credit",ImmediateMethod="credit",Discount="0.00",Lines=new(){new SaleLine{VariantId=_variant,ProductUnitId=_unit,Quantity="1",UnitPrice="12.00",Discount="0.00"}}};}
            public string Snapshot(bool includeDocuments=false)
            {
                // Include row values, not only counts: silent value changes must also fail.
                var tables=new[]{"StockMovements","InventoryBalances","InventoryCostLayers","InventoryCostAllocations","SalesPayments","SalesReturns","SalesReturnLines","SalesReturnCostAllocations","SalesCancellations","CustomerTransactions","Customers","TreasuryAccounts","TreasuryMovements"};
                if(includeDocuments)tables=tables.Concat(new[]{"InventoryDocuments","InventoryDocumentLines","SalesInvoices","SalesInvoiceLines","SalesInvoiceAudit","SalesDraftDeletions","SalePaymentSelections","SalesInvoiceCorrections","SalesInvoiceShiftOrigins"}).ToArray();
                using(var c=Open())return string.Join("\n",tables.Select(table=>table+":"+string.Join(";",c.Query("SELECT * FROM "+table+" ORDER BY rowid").Select(row=>string.Join("|",((System.Collections.Generic.IDictionary<string,object>)row).Select(x=>x.Key+"="+Convert.ToString(x.Value,System.Globalization.CultureInfo.InvariantCulture)))))));
            }
            public void Dispose(){SQLiteConnection.ClearAllPools();if(File.Exists(_path))File.Delete(_path);}
        }
    }
}
