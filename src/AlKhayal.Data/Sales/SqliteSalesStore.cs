using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Finance;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Sales;
using AlKhayal.Data.Inventory;
using Dapper;

namespace AlKhayal.Data.Sales
{
    public sealed partial class SqliteSalesStore : ISalesStore
    {
        private readonly string _connectionString;
        private readonly SqliteInventoryStore _inventory;
        public SqliteSalesStore(string connectionString){_connectionString=connectionString+"Default Timeout=5;";_inventory=new SqliteInventoryStore(connectionString);}
        private SQLiteConnection Open(){var c=new SQLiteConnection(_connectionString);c.Open();using(var cmd=new SQLiteCommand("PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;",c))cmd.ExecuteNonQuery();return c;}
        private static CommandDefinition Cmd(string sql,object args,SQLiteTransaction tx,CancellationToken ct){return new CommandDefinition(sql,args,tx,cancellationToken:ct);}
        private static string Now(){return DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);}

        public async Task<SaleInvoice> CreateDraftAsync(SaleDraftCommand command,long subtotal,long discount,long tax,long total,long userId,CancellationToken ct)
        {
            using(var c=Open())using(var tx=c.BeginTransaction())
            {
                subtotal=await ValidateReferences(c,tx,command,discount,ct).ConfigureAwait(false);
                var settings=await TaxAsync(c,tx,ct).ConfigureAwait(false);var totals=Totals(subtotal,discount,settings.Rate,settings.Enabled,settings.Included);totals.total=checked(totals.total+(command.Delivery==null?0:command.Delivery.FeeScaled));
                var now=Now();var number=NextNumber();
                await c.ExecuteAsync(Cmd(@"INSERT INTO SalesInvoices(InvoiceNumber,InvoiceDate,CustomerId,WarehouseId,CashierUserId,Status,PaymentMode,SubtotalScaled,DiscountScaled,TaxScaled,TotalScaled,PaidScaled,TaxRate,TaxIncluded,CreatedUtc)
                    VALUES(@Number,@Date,@CustomerId,@WarehouseId,@UserId,'draft',@Mode,@Subtotal,@Discount,@Tax,@Total,0,@Rate,@Included,@Now)",new{Number=number,Date=command.InvoiceDate,command.CustomerId,command.WarehouseId,UserId=userId,Mode=command.PaymentMode,Subtotal=subtotal,Discount=discount,Tax=totals.tax,Total=totals.total,Rate=settings.Rate,Included=settings.Included?1:0,Now=now},tx,ct)).ConfigureAwait(false);
                var id=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                await SaveSelectionAsync(c,tx,id,command,ct).ConfigureAwait(false);
                await SaveDeliveryAsync(c,tx,id,command,ct).ConfigureAwait(false);
                await SaveLinesAsync(c,tx,id,command.Lines,ct).ConfigureAwait(false);await FinalizeLinesAsync(c,tx,id,discount,totals.tax,settings.Included,ct).ConfigureAwait(false);await AuditAsync(c,tx,id,userId,"created",now,"",ct).ConfigureAwait(false);tx.Commit();return await GetAsync(id,ct).ConfigureAwait(false);
            }
        }
        public async Task<SaleInvoice> UpdateDraftAsync(long id,SaleDraftCommand command,long subtotal,long discount,long tax,long total,long userId,CancellationToken ct)
        {
            using(var c=Open())using(var tx=c.BeginTransaction())
            {
                var row=await c.QuerySingleOrDefaultAsync<SaleInvoice>(Cmd("SELECT * FROM SalesInvoices WHERE Id=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);if(row==null)return null;if(row.Status!="draft"||await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM SalesDraftDeletions WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false)>0)throw new InvalidOperationException("المسودة محذوفة أو الفاتورة منشورة.");
                if(command.ExpectedVersion.HasValue&&command.ExpectedVersion.Value!=row.Version)throw new InvalidOperationException("تغيرت المسودة منذ فتحها. أعد فتح الفاتورة قبل حفظ التغييرات.");
                subtotal=await ValidateReferences(c,tx,command,discount,ct).ConfigureAwait(false);var settings=await TaxAsync(c,tx,ct).ConfigureAwait(false);var totals=Totals(subtotal,discount,settings.Rate,settings.Enabled,settings.Included);totals.total=checked(totals.total+(command.Delivery==null?0:command.Delivery.FeeScaled));var now=Now();
                var changed=await c.ExecuteAsync(Cmd(@"UPDATE SalesInvoices SET InvoiceDate=@Date,CustomerId=@CustomerId,WarehouseId=@WarehouseId,PaymentMode=@Mode,SubtotalScaled=@Subtotal,DiscountScaled=@Discount,TaxScaled=@Tax,TotalScaled=@Total,TaxRate=@Rate,TaxIncluded=@Included,Version=Version+1 WHERE Id=@Id AND Status='draft' AND Version=@Version",new{Date=command.InvoiceDate,command.CustomerId,command.WarehouseId,Mode=command.PaymentMode,Subtotal=subtotal,Discount=discount,Tax=totals.tax,Total=totals.total,Rate=settings.Rate,Included=settings.Included?1:0,Id=id,row.Version},tx,ct)).ConfigureAwait(false);if(changed!=1)throw new InvalidOperationException("تغيرت المسودة. حدّث الشاشة ثم أعد المحاولة.");
                await SaveSelectionAsync(c,tx,id,command,ct).ConfigureAwait(false);
                await SaveDeliveryAsync(c,tx,id,command,ct).ConfigureAwait(false);
                await c.ExecuteAsync(Cmd("DELETE FROM SalesInvoiceLines WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);await SaveLinesAsync(c,tx,id,command.Lines,ct).ConfigureAwait(false);await FinalizeLinesAsync(c,tx,id,discount,totals.tax,settings.Included,ct).ConfigureAwait(false);await AuditAsync(c,tx,id,userId,"draft_updated",now,"",ct).ConfigureAwait(false);tx.Commit();return await GetAsync(id,ct).ConfigureAwait(false);
            }
        }
        public async Task<SaleInvoice> GetAsync(long id,CancellationToken ct)
        {using(var c=Open()){var row=await c.QuerySingleOrDefaultAsync<SaleInvoice>(Cmd(HeaderQuery+" WHERE si.Id=@Id AND NOT EXISTS(SELECT 1 FROM SalesDraftDeletions dd WHERE dd.SalesInvoiceId=si.Id)",new{Id=id},null,ct)).ConfigureAwait(false);if(row==null)return null;row.Delivery=await DeliveryAsync(c,null,id,ct).ConfigureAwait(false);row.Lines=await LinesAsync(c,null,id,ct).ConfigureAwait(false);row.Returns=(await c.QueryAsync<SaleReturnSummary>(new CommandDefinition("SELECT Id,ReturnNumber,TotalScaled,CashRefundScaled,CreditReductionScaled,Reason,CreatedUtc FROM SalesReturns WHERE SalesInvoiceId=@Id ORDER BY Id",new{Id=id},cancellationToken:ct)).ConfigureAwait(false)).ToList();return row;}}
        public async Task<IReadOnlyList<SaleInvoice>> ListAsync(string search,string from,string to,CancellationToken ct)
        {using(var c=Open()){var rows=(await c.QueryAsync<SaleInvoice>(new CommandDefinition(HeaderQuery+@" WHERE NOT EXISTS(SELECT 1 FROM SalesDraftDeletions dd WHERE dd.SalesInvoiceId=si.Id) AND NOT EXISTS(SELECT 1 FROM SalesInvoiceCorrections correction WHERE correction.PreviousInvoiceId=si.Id) AND (@Search IS NULL OR COALESCE(root.InvoiceNumber,si.InvoiceNumber) LIKE @Like OR COALESCE(c.Name,'') LIKE @Like) AND (@From IS NULL OR si.InvoiceDate>=@From) AND (@To IS NULL OR si.InvoiceDate<=@To) ORDER BY si.Id DESC LIMIT 500",new{Search=string.IsNullOrWhiteSpace(search)?null:search,Like="%"+(search??"")+"%",From=from,To=to},cancellationToken:ct)).ConfigureAwait(false)).ToList();return rows;}}
        public async Task<long> CountSuspendedAsync(CancellationToken ct)
        {using(var c=Open()){return await c.ExecuteScalarAsync<long>(new CommandDefinition(@"SELECT COUNT(*) FROM SalesInvoices si WHERE si.Status='draft' AND NOT EXISTS(SELECT 1 FROM SalesCancellations cancellation WHERE cancellation.SalesInvoiceId=si.Id) AND NOT EXISTS(SELECT 1 FROM SalesDraftDeletions dd WHERE dd.SalesInvoiceId=si.Id) AND NOT EXISTS(SELECT 1 FROM SalesInvoiceCorrections correction WHERE correction.PreviousInvoiceId=si.Id)",cancellationToken:ct)).ConfigureAwait(false);}}
        private const string HeaderQuery=@"SELECT EXISTS(SELECT 1 FROM SalesReturns sr WHERE sr.SalesInvoiceId=si.Id) HasReturns,EXISTS(SELECT 1 FROM SalesReturns sr WHERE sr.SalesInvoiceId=si.Id) AND NOT EXISTS(SELECT 1 FROM SalesInvoiceLines sl WHERE sl.SalesInvoiceId=si.Id AND sl.QuantityScaled>COALESCE((SELECT SUM(rl.QuantityScaled) FROM SalesReturnLines rl WHERE rl.SalesInvoiceLineId=sl.Id),0)) IsFullyReturned,si.Id,COALESCE(root.InvoiceNumber,si.InvoiceNumber) InvoiceNumber,si.InvoiceDate,si.PostedUtc,si.CustomerId,c.Name CustomerName,si.WarehouseId,w.Name WarehouseName,si.CashierUserId,u.DisplayName CashierName,CASE WHEN EXISTS(SELECT 1 FROM SalesCancellations sc WHERE sc.SalesInvoiceId=si.Id) THEN 'canceled' ELSE si.Status END Status,si.PaymentMode,COALESCE(ps.Method,si.PaymentMode) ImmediateMethod,ps.InstrumentId PaymentInstrumentId,pi.Name PaymentInstrumentName,si.SubtotalScaled,si.DiscountScaled,si.TaxScaled,si.TotalScaled,si.PaidScaled,COALESCE((SELECT SUM(sr.TotalScaled) FROM SalesReturns sr WHERE sr.SalesInvoiceId=si.Id),0) ReturnedScaled,CASE WHEN EXISTS(SELECT 1 FROM SalesCancellations sc WHERE sc.SalesInvoiceId=si.Id) THEN 0 ELSE MAX(0,si.TotalScaled-COALESCE((SELECT SUM(sr.TotalScaled) FROM SalesReturns sr WHERE sr.SalesInvoiceId=si.Id),0)-si.PaidScaled) END OutstandingScaled,si.InventoryDocumentId,si.OperationKey,si.RequestHash,si.Version,si.Revision
             ,(SELECT Id FROM CustomerInstallmentContracts ic WHERE ic.SalesInvoiceId=si.Id) InstallmentContractId,(SELECT TreasuryAccountId FROM SalesTreasuryReceipts r WHERE r.SalesInvoiceId=si.Id) TreasuryAccountId,(SELECT TreasuryMovementId FROM SalesTreasuryReceipts r WHERE r.SalesInvoiceId=si.Id) TreasuryMovementId,(SELECT a.Name FROM SalesTreasuryReceipts r JOIN TreasuryAccounts a ON a.Id=r.TreasuryAccountId WHERE r.SalesInvoiceId=si.Id) TreasuryAccountName FROM SalesInvoices si LEFT JOIN SalesInvoices root ON root.Id=si.RootInvoiceId JOIN Warehouses w ON w.Id=si.WarehouseId JOIN Users u ON u.Id=si.CashierUserId LEFT JOIN Customers c ON c.Id=si.CustomerId LEFT JOIN SalePaymentSelections ps ON ps.SalesInvoiceId=si.Id LEFT JOIN PaymentInstruments pi ON pi.Id=ps.InstrumentId";

        public async Task<SalePostResult> PostAsync(long id,long version,string key,string hash,long userId,CancellationToken ct)
        {
            try { using(var c=Open()) using(var tx=c.BeginTransaction())
            {
                var result=await PostWithinTransactionAsync(c,tx,id,version,key,hash,userId,ct).ConfigureAwait(false);
                if(!result.Succeeded)return result;
                tx.Commit();result.Invoice=await GetAsync(id,ct).ConfigureAwait(false);return result;
            }}catch(ArgumentException e){return SalePostResult.Fail("validation",e.Message);}catch(AlKhayal.Application.Treasury.TreasuryException e){return SalePostResult.Fail("treasury_"+e.Code,e.Message);}catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Busy||e.ResultCode==SQLiteErrorCode.Locked){return SalePostResult.Fail("database_busy","قاعدة البيانات مشغولة. أعد المحاولة بالمفتاح نفسه.");}catch(OverflowException){return SalePostResult.Fail("numeric_overflow","قيمة الفاتورة تتجاوز الحد المسموح.");}catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Constraint){return SalePostResult.Fail("sales_conflict","تعارضت الفاتورة مع حركة أخرى. حدّث الشاشة وأعد المحاولة.");}
        }
        private async Task<SalePostResult> PostWithinTransactionAsync(SQLiteConnection c,SQLiteTransaction tx,long id,long version,string key,string hash,long userId,CancellationToken ct,long installmentDownScaled=0)
        {
                var invoice=await c.QuerySingleOrDefaultAsync<SaleInvoice>(Cmd("SELECT * FROM SalesInvoices WHERE Id=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);if(invoice==null)return SalePostResult.Fail("not_found","الفاتورة غير موجودة.");
                var priorKey=await c.QuerySingleOrDefaultAsync<SaleInvoice>(Cmd("SELECT Id,RequestHash FROM SalesInvoices WHERE OperationKey=@Key",new{Key=key},tx,ct)).ConfigureAwait(false);
                if(priorKey!=null&&(priorKey.Id!=id||priorKey.RequestHash!=hash))return SalePostResult.Fail("operation_key_conflict","مفتاح العملية مستخدم لطلب بيع مختلف.");
                if(invoice.Status=="posted")return invoice.OperationKey==key&&invoice.RequestHash==hash?new SalePostResult{Succeeded=true}:SalePostResult.Fail("already_posted","الفاتورة منشورة بالفعل.");
                if(await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM SalesDraftDeletions WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false)>0)return SalePostResult.Fail("draft_deleted","حُذفت المسودة ولا يمكن نشرها.");
                if(invoice.Version!=version)return SalePostResult.Fail("draft_changed","تغيرت المسودة. حدّث الشاشة وأعد النشر.");
                var originShift=await c.ExecuteScalarAsync<long?>(Cmd("SELECT Id FROM CashierShifts WHERE CashierId=@User AND RegisterId='standalone' AND ClosedUtc IS NULL",new{User=userId},tx,ct)).ConfigureAwait(false);
                if(!originShift.HasValue)return SalePostResult.Fail("open_shift_required","افتح وردية الكاشير قبل نشر الفاتورة.");
                var selection=await c.QuerySingleOrDefaultAsync<SaleInvoice>(Cmd("SELECT Method ImmediateMethod,InstrumentId PaymentInstrumentId FROM SalePaymentSelections WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);
                invoice.ImmediateMethod=selection?.ImmediateMethod??invoice.PaymentMode;invoice.PaymentInstrumentId=selection?.PaymentInstrumentId;
                if(invoice.PaymentInstrumentId.HasValue && await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM PaymentInstruments WHERE Id=@Id AND Type=@Type AND IsActive=1",new{Id=invoice.PaymentInstrumentId,Type=invoice.ImmediateMethod},tx,ct)).ConfigureAwait(false)!=1) return SalePostResult.Fail("instrument_unavailable","وسيلة الدفع غير نشطة أو غير مناسبة. حدّث الاختيار.");
                invoice.Lines=await LinesAsync(c,tx,id,ct).ConfigureAwait(false);var plan=await BuildSaleInventoryPlanAsync(c,tx,invoice.Lines,"sale:"+key,invoice.WarehouseId,userId,"salesInvoiceId="+id,ct).ConfigureAwait(false);var command=plan.Command;
                var stock=await _inventory.PostSaleWithinTransactionAsync(c,tx,command,InventoryService.CanonicalHashPost(command),ct).ConfigureAwait(false);if(!stock.Succeeded)return SalePostResult.Fail(stock.ErrorCode,stock.ErrorMessage);
                await LinkSaleInventoryPlanAsync(c,tx,id,stock.Value.DocumentId,plan,ct).ConfigureAwait(false);
                await SaveCostSnapshotAsync(c,tx,id,stock.Value.DocumentId,ct).ConfigureAwait(false);
                if(invoice.PaymentMode=="credit")
                {
                    if(!invoice.CustomerId.HasValue)return SalePostResult.Fail("customer_required","البيع الآجل يتطلب اختيار عميل.");
                    var customer=await c.QuerySingleOrDefaultAsync<CustomerFinancial>(Cmd("SELECT Balance,CreditLimit FROM Customers WHERE Id=@Id AND Status='نشط'",new{Id=invoice.CustomerId},tx,ct)).ConfigureAwait(false);
                    if(customer==null)return SalePostResult.Fail("customer_unavailable","العميل غير متاح.");
                    var next=checked(customer.Balance+invoice.TotalScaled/10000m);
                    if(customer.CreditLimit>0&&next-installmentDownScaled/10000m>customer.CreditLimit)return SalePostResult.Fail("credit_limit","تتجاوز الفاتورة حد ائتمان العميل.");
                    await c.ExecuteAsync(Cmd("INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc,Reference,SalesInvoiceId) VALUES(@CustomerId,@Key,@Hash,'increase',@Amount,@Date,@Notes,@Now,@Reference,@InvoiceId)",new{CustomerId=invoice.CustomerId,Key="sale-invoice:"+id,Hash=hash,Amount=invoice.TotalScaled,Date=invoice.InvoiceDate,Notes="فاتورة بيع "+invoice.InvoiceNumber,Now=Now(),Reference=invoice.InvoiceNumber,InvoiceId=id},tx,ct)).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd("UPDATE Customers SET Balance=@Balance,UpdatedAt=@Now WHERE Id=@Id",new{Balance=next,Now=Now(),Id=invoice.CustomerId},tx,ct)).ConfigureAwait(false);
                }
                else { if(invoice.ImmediateMethod=="cash") await DepositCashSaleAsync(c,tx,invoice,key,hash,userId,ct).ConfigureAwait(false); await c.ExecuteAsync(Cmd("INSERT INTO SalesPayments(SalesInvoiceId,CustomerId,ActorUserId,EventType,PaymentMethod,AmountScaled,OperationKey,RequestHash,OccurredOn,CreatedUtc) VALUES(@Id,@CustomerId,@UserId,'cash_sale',@Method,@Amount,@Key,@Hash,@Date,@Now)",new{Id=id,invoice.CustomerId,UserId=userId,Method=invoice.ImmediateMethod=="wallet"?"transfer":invoice.ImmediateMethod,Amount=invoice.TotalScaled,Key="cash-sale:"+key,Hash=hash,Date=invoice.InvoiceDate,Now=Now()},tx,ct)).ConfigureAwait(false); }
                var updated=await c.ExecuteAsync(Cmd("UPDATE SalesInvoices SET Status='posted',InventoryDocumentId=@DocumentId,OperationKey=@Key,RequestHash=@Hash,PostedUtc=@Now,PaidScaled=CASE WHEN PaymentMode='cash' THEN TotalScaled ELSE 0 END WHERE Id=@Id AND Status='draft' AND Version=@Version",new{DocumentId=stock.Value.DocumentId,Key=key,Hash=hash,Now=Now(),Id=id,Version=version},tx,ct)).ConfigureAwait(false);if(updated!=1)return SalePostResult.Fail("draft_changed","تغيرت المسودة قبل النشر.");
                if(originShift.HasValue)await c.ExecuteAsync(Cmd("INSERT INTO SalesInvoiceShiftOrigins(SalesInvoiceId,ShiftId,CreatedUtc) VALUES(@Invoice,@Shift,@Now)",new{Invoice=id,Shift=originShift.Value,Now=Now()},tx,ct)).ConfigureAwait(false);
                if(invoice.ImmediateMethod=="cash"||invoice.PaymentMode=="credit") await AlKhayal.Data.Workforce.SqliteWorkforceStore.AssociateAsync(c,tx,userId,invoice.PaymentMode=="cash"?"cash_sale":"credit_sale",invoice.TotalScaled,id,null,ct).ConfigureAwait(false); await AuditAsync(c,tx,id,userId,"posted",Now(),"inventoryDocumentId="+stock.Value.DocumentId.ToString(CultureInfo.InvariantCulture),ct).ConfigureAwait(false);return new SalePostResult{Succeeded=true};
        }
        private static async Task SaveSelectionAsync(SQLiteConnection c,SQLiteTransaction tx,long id,SaleDraftCommand command,CancellationToken ct)
        {
            var method=command.ImmediateMethod??command.PaymentMode;
            if(command.PaymentInstrumentId.HasValue && await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM PaymentInstruments WHERE Id=@Id AND Type=@Type AND IsActive=1",new{Id=command.PaymentInstrumentId,Type=method},tx,ct)).ConfigureAwait(false)!=1)throw new ArgumentException("وسيلة الدفع غير نشطة أو غير مناسبة.");
            await c.ExecuteAsync(Cmd("INSERT INTO SalePaymentSelections(SalesInvoiceId,Method,InstrumentId) VALUES(@Id,@Method,@Instrument) ON CONFLICT(SalesInvoiceId) DO UPDATE SET Method=excluded.Method,InstrumentId=excluded.InstrumentId",new{Id=id,Method=method,Instrument=command.PaymentInstrumentId},tx,ct)).ConfigureAwait(false);
        }
        private static async Task SaveDeliveryAsync(SQLiteConnection c,SQLiteTransaction tx,long id,SaleDraftCommand command,CancellationToken ct)
        {
            if(command.Delivery==null){await c.ExecuteAsync(Cmd("DELETE FROM SalesInvoiceDeliveries WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);return;}
            DeliveryDetails driver=null;
            if(command.Delivery.DriverId.HasValue)
            {
                driver=await c.QuerySingleOrDefaultAsync<DeliveryDetails>(Cmd("SELECT Id DriverId,Name DriverName,Phone DriverPhone FROM Drivers WHERE Id=@Id AND IsActive=1",new{Id=command.Delivery.DriverId},tx,ct)).ConfigureAwait(false);
                if(driver==null)throw new ArgumentException("الطيار غير نشط أو غير موجود.");
            }
            var customer=await c.QuerySingleOrDefaultAsync<DeliveryDetails>(Cmd("SELECT Name CustomerName,Phone PrimaryPhone,SecondaryPhone,Address FROM Customers WHERE Id=@Id AND Status='نشط'",new{Id=command.CustomerId},tx,ct)).ConfigureAwait(false);
            if(customer==null)throw new ArgumentException("العميل غير نشط أو غير موجود.");
            var d=command.Delivery; d.DriverName=driver==null?"":driver.DriverName;d.DriverPhone=driver==null?"":driver.DriverPhone;d.CustomerName=customer.CustomerName;
            var now=Now();
            await c.ExecuteAsync(Cmd(@"INSERT INTO SalesInvoiceDeliveries(SalesInvoiceId,DriverId,DriverName,DriverPhone,CustomerName,PrimaryPhone,SecondaryPhone,Address,FeeScaled,Status,Notes,CreatedUtc,UpdatedUtc)
              VALUES(@Id,@DriverId,@DriverName,@DriverPhone,@CustomerName,@PrimaryPhone,@SecondaryPhone,@Address,@FeeScaled,'pending',@Notes,@Now,@Now)
              ON CONFLICT(SalesInvoiceId) DO UPDATE SET DriverId=excluded.DriverId,DriverName=excluded.DriverName,DriverPhone=excluded.DriverPhone,CustomerName=excluded.CustomerName,PrimaryPhone=excluded.PrimaryPhone,SecondaryPhone=excluded.SecondaryPhone,Address=excluded.Address,FeeScaled=excluded.FeeScaled,Notes=excluded.Notes,UpdatedUtc=excluded.UpdatedUtc",new{Id=id,d.DriverId,d.DriverName,d.DriverPhone,d.CustomerName,d.PrimaryPhone,d.SecondaryPhone,d.Address,d.FeeScaled,d.Notes,Now=now},tx,ct)).ConfigureAwait(false);
        }
        private static async Task<DeliveryDetails> DeliveryAsync(SQLiteConnection c,SQLiteTransaction tx,long id,CancellationToken ct)
        {var d=await c.QuerySingleOrDefaultAsync<DeliveryDetails>(Cmd("SELECT DriverId,DriverName,DriverPhone,CustomerName,PrimaryPhone,SecondaryPhone,Address,FeeScaled,Status,Notes FROM SalesInvoiceDeliveries WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);if(d!=null)d.Fee=MoneyPolicy.FormatForUser(d.FeeScaled);return d;}
        private static async Task<List<SaleLine>> LinesAsync(SQLiteConnection c,SQLiteTransaction tx,long id,CancellationToken ct)
        {
            var lines=(await c.QueryAsync<SaleLine>(Cmd(@"SELECT l.Id,l.VariantId,p.Id ProductId,p.Name ProductName,(SELECT sku.Sku FROM ProductSkus sku WHERE sku.VariantId=v.Id AND sku.RetiredUtc IS NULL LIMIT 1) Sku,COALESCE(NULLIF((SELECT group_concat(Value,' / ') FROM (SELECT pav.Value Value FROM ProductVariantValues vv JOIN ProductAttributeValues pav ON pav.Id=vv.AttributeValueId WHERE vv.VariantId=v.Id ORDER BY vv.AttributeId,pav.SortOrder,pav.Id)),''),v.Name) VariantName,l.ProductUnitId,CASE WHEN d.SellingType='weight' THEN 'كجم' ELSE u.Name END UnitName,d.SellingType,
                CAST(l.QuantityScaled AS TEXT) Quantity,CAST(COALESCE((SELECT SUM(rl.QuantityScaled) FROM SalesReturnLines rl WHERE rl.SalesInvoiceLineId=l.Id),0) AS TEXT) ReturnedQuantity,
                l.InventoryDocumentLineId,COALESCE(idl.ToBaseNumerator,pu.ToBaseNumerator) ToBaseNumerator,COALESCE(idl.ToBaseDenominator,pu.ToBaseDenominator) ToBaseDenominator,l.BatchLotNumber,l.ExpirationDate,CAST(l.UnitPriceScaled AS TEXT) UnitPrice,CAST(l.DiscountScaled AS TEXT) Discount,l.ManualPriceOverride,l.InvoiceDiscountScaled,l.TaxScaled,l.LineTotalScaled
                FROM SalesInvoiceLines l JOIN ProductVariants v ON v.Id=l.VariantId JOIN Products p ON p.Id=v.ProductId JOIN ProductDetails d ON d.ProductId=p.Id JOIN ProductUnits pu ON pu.Id=l.ProductUnitId JOIN Units u ON u.Id=pu.UnitId LEFT JOIN InventoryDocumentLines idl ON idl.Id=l.InventoryDocumentLineId
                WHERE l.SalesInvoiceId=@Id ORDER BY l.LineNumber",new{Id=id},tx,ct)).ConfigureAwait(false)).ToList();
            foreach(var x in lines)
            {
                x.Quantity=Format(Scale(x.Quantity),6);x.ReturnedQuantity=Format(Scale(x.ReturnedQuantity),6);x.UnitPrice=MoneyPolicy.FormatForUser(Scale(x.UnitPrice));x.Discount=MoneyPolicy.FormatForUser(Scale(x.Discount));x.LineTotal=MoneyPolicy.FormatForUser(x.LineTotalScaled);
                x.PhysicalUnits=(await c.QueryAsync<PersistedUnit>(Cmd("SELECT Serial,Imeis FROM SalesInvoiceLinePhysicalUnits WHERE SalesInvoiceLineId=@Id ORDER BY UnitNumber",new{Id=x.Id},tx,ct)).ConfigureAwait(false))
                    .Select(unit=>new SaleTrackingUnit{Serial=unit.Serial,Imeis=string.IsNullOrEmpty(unit.Imeis)?new List<string>():unit.Imeis.Split('|').ToList()}).ToList();
                x.AvailableTrackingUnits=(await c.QueryAsync<AvailableUnit>(Cmd(@"SELECT serial.DisplayValue Serial,COALESCE((SELECT group_concat(imei.DisplayValue,'|') FROM InventoryIdentifiers imei WHERE imei.PhysicalUnitId=p.Id AND imei.Kind='imei'),'') ImeiValues
                    FROM StockMovementUnits smu JOIN StockMovements m ON m.Id=smu.MovementId JOIN InventoryPhysicalUnits p ON p.Id=smu.PhysicalUnitId JOIN InventoryIdentifiers serial ON serial.PhysicalUnitId=p.Id AND serial.Kind='serial'
                    WHERE (m.Id=(SELECT Id FROM StockMovements WHERE DocumentLineId=@DocumentLineId AND QuantityBaseScaled<0 LIMIT 1)
                        OR m.DocumentLineId IN (SELECT InventoryDocumentLineId FROM SalesInvoiceStockEntries WHERE SalesInvoiceLineId=@LineId))
                      AND p.WarehouseId IS NULL ORDER BY p.Id",new{DocumentLineId=x.InventoryDocumentLineId,LineId=x.Id},tx,ct)).ConfigureAwait(false))
                    .Select(u=>new SaleTrackingUnit{Serial=u.Serial,Imeis=string.IsNullOrEmpty(u.ImeiValues)?new List<string>():u.ImeiValues.Split('|').ToList()}).ToList();
            }
            return lines;
        }
        private sealed class AvailableUnit{public string Serial{get;set;}public string ImeiValues{get;set;}}
        private sealed class PersistedUnit{public string Serial{get;set;}public string Imeis{get;set;}}
        private static async Task SaveLinesAsync(SQLiteConnection c,SQLiteTransaction tx,long id,IList<SaleLine> lines,CancellationToken ct)
        {var n=0;foreach(var x in lines){n++;long q,p,d;ScaleInput(x.Quantity,6,out q);MoneyPolicy.TryParseUserAmount(x.UnitPrice,false,out p);MoneyPolicy.TryParseUserAmount(x.Discount??"0",false,out d);await c.ExecuteAsync(Cmd("INSERT INTO SalesInvoiceLines(SalesInvoiceId,LineNumber,VariantId,ProductUnitId,QuantityScaled,UnitPriceScaled,ManualPriceOverride,DiscountScaled,InvoiceDiscountScaled,TaxScaled,LineTotalScaled,BatchLotNumber,ExpirationDate) VALUES(@Id,@N,@VariantId,@Unit,@Q,@P,@ManualPriceOverride,@D,0,0,@Total,@Lot,@Expiry)",new{Id=id,N=n,x.VariantId,Unit=x.ProductUnitId,Q=q,P=p,x.ManualPriceOverride,D=d,Total=x.LineTotalScaled,Lot=x.BatchLotNumber,Expiry=x.ExpirationDate},tx,ct)).ConfigureAwait(false);var lineId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);var index=0;foreach(var unit in x.PhysicalUnits??new List<SaleTrackingUnit>()){index++;await c.ExecuteAsync(Cmd("INSERT INTO SalesInvoiceLinePhysicalUnits(SalesInvoiceLineId,UnitNumber,Serial,Imeis) VALUES(@Line,@Number,@Serial,@Imeis)",new{Line=lineId,Number=index,unit.Serial,Imeis=string.Join("|",unit.Imeis??new List<string>())},tx,ct)).ConfigureAwait(false);}}}
        private sealed class LineValue { public long Id{get;set;} public long NetScaled{get;set;} }
        private static async Task FinalizeLinesAsync(SQLiteConnection c,SQLiteTransaction tx,long id,long invoiceDiscount,long tax,bool included,CancellationToken ct)
        {
            var lines=(await c.QueryAsync<LineValue>(Cmd("SELECT Id,LineTotalScaled NetScaled FROM SalesInvoiceLines WHERE SalesInvoiceId=@Id ORDER BY LineNumber",new{Id=id},tx,ct)).ConfigureAwait(false)).ToList();
            var weight=lines.Sum(x=>(decimal)x.NetScaled);long discountLeft=invoiceDiscount,taxLeft=tax;
            var allocatedDiscounts=new long[lines.Count];var taxable=new long[lines.Count];
            for(var i=0;i<lines.Count;i++)
            {
                var line=lines[i];var last=i==lines.Count-1;var discount=last?Math.Min(discountLeft,line.NetScaled):weight==0?0:checked((long)(decimal.Floor((decimal)invoiceDiscount*line.NetScaled/weight/100m)*100m));discountLeft-=discount;
                allocatedDiscounts[i]=discount;
            }
            for(var i=lines.Count-2;i>=0&&discountLeft>0;i--)
            {
                var extra=Math.Min(discountLeft,lines[i].NetScaled-allocatedDiscounts[i]);
                allocatedDiscounts[i]=checked(allocatedDiscounts[i]+extra);discountLeft-=extra;
            }
            if(discountLeft!=0)throw new InvalidOperationException("تعذر توزيع خصم الفاتورة على البنود.");
            for(var i=0;i<lines.Count;i++)taxable[i]=checked(lines[i].NetScaled-allocatedDiscounts[i]);
            var taxableTotal=taxable.Sum(x=>(decimal)x);var lastTaxable=Array.FindLastIndex(taxable,x=>x>0);
            for(var i=0;i<lines.Count;i++)
            {
                var lineTax=i==lastTaxable?taxLeft:taxable[i]==0||taxableTotal==0?0:checked((long)(decimal.Floor((decimal)tax*taxable[i]/taxableTotal/100m)*100m));taxLeft-=lineTax;
                var finalTotal=included?taxable[i]:checked(taxable[i]+lineTax);
                await c.ExecuteAsync(Cmd("UPDATE SalesInvoiceLines SET InvoiceDiscountScaled=@Discount,TaxScaled=@Tax,LineTotalScaled=@Total WHERE Id=@Id",new{Discount=allocatedDiscounts[i],Tax=lineTax,Total=finalTotal,Id=lines[i].Id},tx,ct)).ConfigureAwait(false);
            }
        }
        private static async Task<long> ValidateReferences(SQLiteConnection c,SQLiteTransaction tx,SaleDraftCommand command,long invoiceDiscount,CancellationToken ct,bool enforceCurrentPrice=true)
        {
            var warehouse=await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM Warehouses WHERE Id=@Id AND IsActive=1",new{Id=command.WarehouseId},tx,ct)).ConfigureAwait(false);
            if(warehouse!=1)throw new ArgumentException("المخزن غير نشط أو غير موجود.");
            if(command.CustomerId.HasValue&&await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM Customers WHERE Id=@Id AND Status='نشط'",new{Id=command.CustomerId},tx,ct)).ConfigureAwait(false)!=1)throw new ArgumentException("العميل غير نشط أو غير موجود.");
            long subtotal=0;
            foreach(var x in command.Lines)
            {
                var product=await c.QuerySingleOrDefaultAsync<SellableProduct>(Cmd(@"SELECT COALESCE(v.SellPriceOverrideScaled,d.SellPriceScaled) Price,pu.SellPriceScaled UnitPrice,pu.ToBaseNumerator Numerator,pu.ToBaseDenominator Denominator,pu.IsBase IsBase
                    FROM ProductVariants v JOIN ProductDetails d ON d.ProductId=v.ProductId JOIN ProductUnits pu ON pu.ProductId=v.ProductId AND pu.Id=@Unit AND pu.IsActive=1 AND pu.IsSaleUnit=1
                    WHERE v.Id=@Variant AND v.IsActive=1 AND v.ArchivedUtc IS NULL AND d.IsActive=1 AND d.ArchivedUtc IS NULL AND d.ProductType='stock'",new{Unit=x.ProductUnitId,Variant=x.VariantId},tx,ct)).ConfigureAwait(false);
                long entered;var expected=product==null?0:product.UnitPrice.HasValue?SalesPricing.SelectedUnitPrice(product.UnitPrice.Value,1,1):SalesPricing.SelectedUnitPrice(product.Price,product.Numerator,product.Denominator);
                if(product!=null&&!product.UnitPrice.HasValue&&product.IsBase)expected=await ExpectedBaseLinePriceAsync(c,tx,x,product,ct).ConfigureAwait(false);
                if(product==null||!MoneyPolicy.TryParseUserAmount(x.UnitPrice,false,out entered)||(enforceCurrentPrice&&!x.ManualPriceOverride&&entered!=expected))throw new ArgumentException("المنتج أو وحدة البيع غير متاح، أو تغير سعر البيع؛ حدّث السعر قبل الحفظ.");
                long quantity, lineDiscount;if(!ScaleInput(x.Quantity,6,out quantity)||!MoneyPolicy.TryParseUserAmount(x.Discount??"0",false,out lineDiscount))throw new ArgumentException("راجع كمية البند وخصمه.");
                var gross=MoneyPolicy.FinalizeQuantityAmount(quantity,entered);
                if(product.IsBase&&!product.UnitPrice.HasValue&&!x.ManualPriceOverride&&entered==expected)
                {
                    var tiers=await c.QueryAsync<SalePriceBreak>(Cmd(@"SELECT Id UnitId,SellPriceScaled,ToBaseNumerator Numerator,ToBaseDenominator Denominator FROM ProductUnits WHERE ProductId=(SELECT ProductId FROM ProductVariants WHERE Id=@Variant) AND IsActive=1 AND IsSaleUnit=1 AND IsBase=0",new{Variant=x.VariantId},tx,ct)).ConfigureAwait(false);
                    gross=SalesPricing.AutomaticBaseLineGross(product.Price,quantity/1000000m,tiers);
                }
                if(lineDiscount>gross)throw new ArgumentException("خصم البند أكبر من قيمته.");
                x.LineTotalScaled=checked(gross-lineDiscount);subtotal=checked(subtotal+x.LineTotalScaled);
            }
            if(invoiceDiscount>subtotal||subtotal-invoiceDiscount<=0)throw new ArgumentException("خصم الفاتورة غير صالح أو أكبر من الإجمالي.");
            return subtotal;
        }
        private static async Task<long> ExpectedBaseLinePriceAsync(SQLiteConnection c,SQLiteTransaction tx,SaleLine line,SellableProduct product,CancellationToken ct)
        {
            decimal quantity;if(!decimal.TryParse(line.Quantity,NumberStyles.Number,CultureInfo.InvariantCulture,out quantity))return SalesPricing.SelectedUnitPrice(product.Price,1,1);
            var tiers=await c.QueryAsync<SalePriceBreak>(Cmd(@"SELECT Id UnitId,SellPriceScaled,ToBaseNumerator Numerator,ToBaseDenominator Denominator FROM ProductUnits WHERE ProductId=(SELECT ProductId FROM ProductVariants WHERE Id=@Variant) AND IsActive=1 AND IsSaleUnit=1 AND IsBase=0",new{Variant=line.VariantId},tx,ct)).ConfigureAwait(false);
            return SalesPricing.AutomaticBaseUnitPrice(product.Price,quantity,tiers);
        }
        private sealed class SellableProduct{public long Price{get;set;}public long? UnitPrice{get;set;}public long Numerator{get;set;}public long Denominator{get;set;}public bool IsBase{get;set;}}
        private sealed class BundleInventoryComponent{public long VariantId{get;set;}public long QuantityBaseScaled{get;set;}public long ProductUnitId{get;set;}public bool IsTracked{get;set;}}
        private sealed class SaleInventoryMapping{public long InvoiceLineId{get;set;}public int DocumentLineNumber{get;set;}public long? BundleQuantityBaseScaled{get;set;}public long? StockQuantityBaseScaled{get;set;}}
        private sealed class SaleStockLayer{public long Id{get;set;}public long? BatchId{get;set;}public string LotNumber{get;set;}public string ExpirationDate{get;set;}public long RemainingQuantityScaled{get;set;}}
        private sealed class SaleSerialEntry{public long BatchId{get;set;}public string LotNumber{get;set;}public string ExpirationDate{get;set;}public SaleTrackingUnit Unit{get;set;}}
        private sealed class SaleInventoryPlan{public InventoryPostCommand Command{get;set;}public List<SaleInventoryMapping> Mappings{get;set;}=new List<SaleInventoryMapping>();}
        private async Task<SaleInventoryPlan> BuildSaleInventoryPlanAsync(SQLiteConnection c,SQLiteTransaction tx,IReadOnlyList<SaleLine> invoiceLines,string operationKey,long warehouseId,long userId,string notes,CancellationToken ct)
        {
            var plan=new SaleInventoryPlan{Command=new InventoryPostCommand{OperationKey=operationKey,Type="manual_issue",WarehouseId=warehouseId,UserId=userId,Reason="صرف فاتورة بيع",Notes=notes,Lines=new List<InventoryLineInput>()}};
            var documentLines=(List<InventoryLineInput>)plan.Command.Lines;var number=0;
            var reservedLayers=new Dictionary<long,long>();
            foreach(var line in invoiceLines)
            {
                var bundle=await c.QuerySingleAsync<BundleSaleState>(Cmd(@"SELECT pu.ToBaseNumerator Numerator,pu.ToBaseDenominator Denominator,d.AllowSaleWhenOutOfStock,
                    EXISTS(SELECT 1 FROM ProductBundleComponents bc JOIN ProductVariants bv ON bv.ProductId=bc.BundleProductId WHERE bv.Id=@Variant) IsBundle
                    FROM ProductUnits pu JOIN ProductVariants pv ON pv.ProductId=pu.ProductId JOIN ProductDetails d ON d.ProductId=pv.ProductId WHERE pu.Id=@Unit AND pv.Id=@Variant",new{Unit=line.ProductUnitId,Variant=line.VariantId},tx,ct)).ConfigureAwait(false);
                if(!bundle.IsBundle)
                {
                    var tracking=await c.QuerySingleAsync<SaleTrackingState>(Cmd(@"SELECT
                        EXISTS(SELECT 1 FROM ProductCapabilityAssignments a JOIN ProductVariants v ON v.ProductId=a.ProductId WHERE v.Id=@Variant AND a.CapabilityKey IN ('batch-tracking','expiry-dates') AND a.IsEnabled=1) EntryTracked,
                        EXISTS(SELECT 1 FROM ProductCapabilityAssignments a JOIN ProductVariants v ON v.ProductId=a.ProductId WHERE v.Id=@Variant AND a.CapabilityKey IN ('serial-numbers','imei-tracking') AND a.IsEnabled=1) SerialTracked,
                        (SELECT Id FROM ProductUnits WHERE ProductId=(SELECT ProductId FROM ProductVariants WHERE Id=@Variant) AND IsBase=1 AND IsActive=1 LIMIT 1) BaseUnitId",new{Variant=line.VariantId},tx,ct)).ConfigureAwait(false);
                    if(tracking.EntryTracked&&!tracking.SerialTracked)
                    {
                        long saleEntered;if(!ScaleInput(line.Quantity,6,out saleEntered)||saleEntered<=0)throw new ArgumentException("كمية البيع غير صالحة.");
                        var exact=(decimal)saleEntered*bundle.Numerator/bundle.Denominator;
                        if(exact!=decimal.Truncate(exact)||exact>long.MaxValue)throw new ArgumentException("كمية البيع لا تتحول بدقة إلى وحدة المخزون الأساسية.");
                        var remaining=(long)exact;
                        var today=DateTime.Today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                        var layers=(await c.QueryAsync<SaleStockLayer>(Cmd(@"SELECT l.Id,l.BatchId,b.LotNumber,b.ExpirationDate,l.RemainingQuantityScaled
                            FROM InventoryCostLayers l LEFT JOIN ProductBatches b ON b.Id=l.BatchId
                            WHERE l.WarehouseId=@Warehouse AND l.VariantId=@Variant AND l.BatchId IS NOT NULL AND l.IsSellable=1 AND l.RemainingQuantityScaled>0
                              AND (b.ExpirationDate IS NULL OR b.ExpirationDate>=@Today)
                            ORDER BY l.OriginMovementId,l.Id",new{Warehouse=warehouseId,Variant=line.VariantId,Today=today},tx,ct)).ConfigureAwait(false)).ToList();
                        foreach(var layer in layers)
                        {
                            if(remaining==0)break;
                            long reserved;reservedLayers.TryGetValue(layer.Id,out reserved);
                            var take=Math.Min(remaining,Math.Max(0,layer.RemainingQuantityScaled-reserved));
                            if(take<=0)continue;
                            reservedLayers[layer.Id]=checked(reserved+take);
                            number++;documentLines.Add(ToInventoryLine(line.VariantId,tracking.BaseUnitId,Format(take,6),layer.LotNumber,null,null,false));
                            plan.Mappings.Add(new SaleInventoryMapping{InvoiceLineId=line.Id,DocumentLineNumber=number,StockQuantityBaseScaled=take});
                            remaining-=take;
                        }
                        if(remaining>0)
                        {
                            if(!bundle.AllowSaleWhenOutOfStock)throw new ArgumentException("الرصيد المتاح أقل من الكمية المطلوبة.");
                            number++;documentLines.Add(ToInventoryLine(line.VariantId,tracking.BaseUnitId,Format(remaining,6),null,null,null,true));
                            plan.Mappings.Add(new SaleInventoryMapping{InvoiceLineId=line.Id,DocumentLineNumber=number,StockQuantityBaseScaled=remaining});
                        }
                        continue;
                    }
                    if(tracking.EntryTracked&&tracking.SerialTracked)
                    {
                        long serialEntered;if(!ScaleInput(line.Quantity,6,out serialEntered)||serialEntered<=0)throw new ArgumentException("كمية البيع غير صالحة.");
                        var serialBase=(decimal)serialEntered*bundle.Numerator/bundle.Denominator;
                        if(serialBase!=decimal.Truncate(serialBase)||serialBase%1000000m!=0)throw new ArgumentException("المنتج المتسلسل يحتاج كمية صحيحة من الوحدات.");
                        var physical=line.PhysicalUnits??new List<SaleTrackingUnit>();
                        if(physical.Count!=serialBase/1000000m&&!bundle.AllowSaleWhenOutOfStock)throw new ArgumentException("أدخل الأرقام التسلسلية للوحدات المتاحة.");
                        var serialEntries=new List<SaleSerialEntry>();
                        foreach(var unit in physical)
                        {
                            string normalized;if(!TrackingIdentityRules.TryNormalizeSerial(unit.Serial,out normalized))throw new ArgumentException("الرقم التسلسلي غير صالح.");
                            var entry=await c.QuerySingleOrDefaultAsync<SaleSerialEntry>(Cmd(@"SELECT p.BatchId,b.LotNumber,b.ExpirationDate FROM InventoryPhysicalUnits p
                                JOIN InventoryIdentifiers i ON i.PhysicalUnitId=p.Id AND i.Kind='serial'
                                JOIN ProductBatches b ON b.Id=p.BatchId
                                WHERE p.WarehouseId=@Warehouse AND p.VariantId=@Variant AND i.NormalizedValue=@Serial",new{Warehouse=warehouseId,Variant=line.VariantId,Serial=normalized},tx,ct)).ConfigureAwait(false);
                            if(entry==null)throw new ArgumentException("الرقم التسلسلي غير موجود في المخزن.");
                            entry.Unit=unit;serialEntries.Add(entry);
                        }
                        foreach(var group in serialEntries.GroupBy(x=>x.BatchId))
                        {
                            var qty=checked(group.Count()*1000000L);
                            number++;documentLines.Add(ToInventoryLine(line.VariantId,tracking.BaseUnitId,group.Count().ToString(CultureInfo.InvariantCulture),group.First().LotNumber,null,group.Select(x=>x.Unit).ToList(),false));
                            plan.Mappings.Add(new SaleInventoryMapping{InvoiceLineId=line.Id,DocumentLineNumber=number,StockQuantityBaseScaled=qty});
                        }
                        var uncovered=checked((long)serialBase-serialEntries.Count*1000000L);
                        if(uncovered>0)
                        {
                            number++;documentLines.Add(ToInventoryLine(line.VariantId,tracking.BaseUnitId,Format(uncovered,6),null,null,null,true));
                            plan.Mappings.Add(new SaleInventoryMapping{InvoiceLineId=line.Id,DocumentLineNumber=number,StockQuantityBaseScaled=uncovered});
                        }
                        continue;
                    }
                    number++;documentLines.Add(ToInventoryLine(line.VariantId,line.ProductUnitId,line.Quantity,line.BatchLotNumber,line.ExpirationDate,line.PhysicalUnits,bundle.AllowSaleWhenOutOfStock));
                    plan.Mappings.Add(new SaleInventoryMapping{InvoiceLineId=line.Id,DocumentLineNumber=number});continue;
                }
                long entered;if(!ScaleInput(line.Quantity,6,out entered)||entered<=0)throw new ArgumentException("كمية المنتج المركب غير صالحة.");
                var bundleBase=(decimal)entered*bundle.Numerator/bundle.Denominator;
                if(bundleBase!=decimal.Truncate(bundleBase)||bundleBase%1000000m!=0)throw new ArgumentException("المنتج المركب يُباع بعدد صحيح من العبوات.");
                var components=(await c.QueryAsync<BundleInventoryComponent>(Cmd(@"SELECT bc.ComponentVariantId VariantId,bc.QuantityBaseScaled,
                    (SELECT pu.Id FROM ProductUnits pu JOIN ProductVariants cv ON cv.ProductId=pu.ProductId WHERE cv.Id=bc.ComponentVariantId AND pu.IsBase=1 AND pu.IsActive=1 LIMIT 1) ProductUnitId,
                    EXISTS(SELECT 1 FROM ProductVariants cv JOIN ProductCapabilityAssignments a ON a.ProductId=cv.ProductId WHERE cv.Id=bc.ComponentVariantId AND a.IsEnabled=1 AND a.CapabilityKey IN ('batch-tracking','expiry-dates','serial-numbers','imei-tracking')) IsTracked
                    FROM ProductBundleComponents bc JOIN ProductVariants bv ON bv.ProductId=bc.BundleProductId WHERE bv.Id=@Variant ORDER BY bc.ComponentVariantId",new{Variant=line.VariantId},tx,ct)).ConfigureAwait(false)).ToList();
                if(components.Count<2)throw new ArgumentException("تركيبة المنتج غير مكتملة. حدّث بيانات المنتج قبل البيع.");
                foreach(var component in components)
                {
                    if(component.ProductUnitId<=0||component.IsTracked)throw new ArgumentException("أحد مكونات المنتج يحتاج تتبع تشغيلة أو رقمًا تسلسليًا، ولا يمكن صرفه ضمن تركيبة حاليًا.");
                    var qty=(decimal)bundleBase*component.QuantityBaseScaled/1000000m;
                    if(qty<=0||qty!=decimal.Truncate(qty)||qty>long.MaxValue)throw new ArgumentException("كمية مكون المنتج لا تتحول بدقة إلى وحدة المخزون الأساسية.");
                    number++;documentLines.Add(ToInventoryLine(component.VariantId,component.ProductUnitId,Format((long)qty,6),null,null,null,bundle.AllowSaleWhenOutOfStock));
                    plan.Mappings.Add(new SaleInventoryMapping{InvoiceLineId=line.Id,DocumentLineNumber=number,BundleQuantityBaseScaled=(long)qty});
                }
            }
            plan.Command.Reason="صرف فاتورة بيع";return plan;
        }
        private static InventoryLineInput ToInventoryLine(long variantId,long productUnitId,string quantity,string lot,string expiry,IReadOnlyList<SaleTrackingUnit> physical,bool allowShortfall)
        {return new InventoryLineInput{VariantId=variantId,ProductUnitId=productUnitId,Quantity=quantity,LotNumber=lot,ExpirationDate=expiry,PhysicalUnits=(physical??new List<SaleTrackingUnit>()).Select(u=>new InventoryUnitInput{Serial=u.Serial,Imeis=u.Imeis}).ToList(),AllowShortfall=allowShortfall};}
        private static async Task LinkSaleInventoryPlanAsync(SQLiteConnection c,SQLiteTransaction tx,long invoiceId,long documentId,SaleInventoryPlan plan,CancellationToken ct)
        {
            foreach(var mapping in plan.Mappings)
            {
                var documentLineId=await c.ExecuteScalarAsync<long>(Cmd("SELECT Id FROM InventoryDocumentLines WHERE DocumentId=@DocumentId AND LineNumber=@Number",new{DocumentId=documentId,Number=mapping.DocumentLineNumber},tx,ct)).ConfigureAwait(false);
                if(mapping.BundleQuantityBaseScaled.HasValue)
                {
                    await c.ExecuteAsync(Cmd("UPDATE SalesInvoiceLines SET InventoryDocumentLineId=NULL WHERE Id=@Id AND SalesInvoiceId=@Invoice",new{Id=mapping.InvoiceLineId,Invoice=invoiceId},tx,ct)).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd("INSERT INTO SalesInvoiceBundleComponentLines(SalesInvoiceLineId,InventoryDocumentLineId,QuantityBaseScaled) VALUES(@Line,@DocumentLine,@Quantity)",new{Line=mapping.InvoiceLineId,DocumentLine=documentLineId,Quantity=mapping.BundleQuantityBaseScaled.Value},tx,ct)).ConfigureAwait(false);
                }
                else if(mapping.StockQuantityBaseScaled.HasValue)
                    await c.ExecuteAsync(Cmd("INSERT INTO SalesInvoiceStockEntries(SalesInvoiceLineId,InventoryDocumentLineId,QuantityBaseScaled) VALUES(@Line,@DocumentLine,@Quantity)",new{Line=mapping.InvoiceLineId,DocumentLine=documentLineId,Quantity=mapping.StockQuantityBaseScaled.Value},tx,ct)).ConfigureAwait(false);
                else await c.ExecuteAsync(Cmd("UPDATE SalesInvoiceLines SET InventoryDocumentLineId=@DocumentLine WHERE Id=@Id AND SalesInvoiceId=@Invoice",new{DocumentLine=documentLineId,Id=mapping.InvoiceLineId,Invoice=invoiceId},tx,ct)).ConfigureAwait(false);
            }
        }
        private sealed class BundleSaleState{public long Numerator{get;set;}public long Denominator{get;set;}public bool IsBundle{get;set;}public bool AllowSaleWhenOutOfStock{get;set;}}
        private sealed class SaleTrackingState{public bool EntryTracked{get;set;}public bool SerialTracked{get;set;}public long BaseUnitId{get;set;}}
        private sealed class TaxState{public bool Enabled{get;set;}public decimal Rate{get;set;}public bool Included{get;set;}}
        private static Task<TaxState> TaxAsync(SQLiteConnection c,SQLiteTransaction tx,CancellationToken ct){return c.QuerySingleAsync<TaxState>(Cmd("SELECT TaxEnabled Enabled,DefaultTaxRate Rate,PricesIncludeTax Included FROM ApplicationSettings WHERE Id=1",null,tx,ct));}
        private static (long tax,long total) Totals(long subtotal,long discount,decimal rate,bool enabled,bool included){var baseAmount=checked(subtotal-discount);if(!enabled||rate==0)return(0,baseAmount);var taxAmount=included?baseAmount*rate/(100m+rate):baseAmount*rate/100m;var tax=MoneyPolicy.RoundInternalToPiastre(checked((long)decimal.Round(taxAmount,0,MidpointRounding.AwayFromZero)));return(tax,included?baseAmount:checked(baseAmount+tax));}
        private static Task AuditAsync(SQLiteConnection c,SQLiteTransaction tx,long id,long userId,string type,string now,string details,CancellationToken ct){return c.ExecuteAsync(Cmd("INSERT INTO SalesInvoiceAudit(SalesInvoiceId,ActorUserId,EventType,EventUtc,Details) VALUES(@Id,@UserId,@Type,@Now,@Details)",new{Id=id,UserId=userId,Type=type,Now=now,Details=details},tx,ct));}
        private static string NextNumber(){return "SAL-"+DateTime.UtcNow.ToString("yyyyMMdd",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,8).ToUpperInvariant();}
        private static long Scale(string text){long x;long.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out x);return x;}
        private static bool ScaleInput(string text,int digits,out long result){decimal d;if(!decimal.TryParse(text,NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out d)){result=0;return false;}result=checked((long)(d*(digits==6?1000000m:10000m)));return true;}
        private static string Format(long value,int digits){return (value/(digits==6?1000000m:10000m)).ToString(digits==6?"0.######":"0.0000",CultureInfo.InvariantCulture);}
    }
}
