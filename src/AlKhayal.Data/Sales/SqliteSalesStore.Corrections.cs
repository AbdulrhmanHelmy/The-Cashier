using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Finance;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Treasury;
using AlKhayal.Data.Treasury;
using Dapper;

namespace AlKhayal.Data.Sales
{
    public sealed partial class SqliteSalesStore
    {
        private sealed class CorrectionInvoice : SaleInvoice { public long? RootInvoiceId{get;set;} }
        private sealed class CorrectionRow { public long ReplacementInvoiceId{get;set;} public string RequestHash{get;set;} }
        private sealed class CorrectionMovement { public long Id{get;set;} public long VariantId{get;set;} public long? BatchId{get;set;} public long QuantityBaseScaled{get;set;} public long ValueScaled{get;set;} public long ProductUnitId{get;set;} public long EnteredQuantityScaled{get;set;} public long ToBaseNumerator{get;set;} public long ToBaseDenominator{get;set;} }
        private sealed class CorrectionAllocation { public long LayerId{get;set;} public long QuantityScaled{get;set;} public long ValueScaled{get;set;} public long? OriginMovementId{get;set;} public long? UnitCostScaled{get;set;} }
        private sealed class CorrectionBalance { public long QuantityBaseScaled{get;set;} public long ValueScaled{get;set;} }
        private sealed class EditEligibility { public bool Allowed{get;set;} public string Code{get;set;} public string Message{get;set;} public static EditEligibility Yes(){return new EditEligibility{Allowed=true};} public static EditEligibility No(string code,string message){return new EditEligibility{Code=code,Message=message};} }

        public async Task<SaleInvoice> GetForEditAsync(long id,long userId,string localBusinessDate,CancellationToken ct)
        {
            var invoice=await GetAsync(id,ct).ConfigureAwait(false);if(invoice==null)return null;
            using(var c=Open())
            {
                var eligibility=await EligibleAsync(c,null,id,userId,localBusinessDate,ct).ConfigureAwait(false);invoice.CanEditPosted=eligibility.Allowed;
                invoice.EditRestrictionCode=eligibility.Code;invoice.EditRestrictionMessage=eligibility.Message;
                if(invoice.CanEditPosted)invoice.SettlementAccounts=(await c.QueryAsync<SaleSettlementAccount>(Cmd("SELECT Id,Name,Type FROM TreasuryAccounts WHERE IsDeleted=0 ORDER BY Name,Id",null,null,ct)).ConfigureAwait(false)).ToList();
            }
            return invoice;
        }

        private static async Task<EditEligibility> EligibleAsync(SQLiteConnection c,SQLiteTransaction tx,long id,long userId,string localDate,CancellationToken ct)
        {
            var invoice=await c.QuerySingleOrDefaultAsync<CorrectionInvoice>(Cmd("SELECT Id,Status,InvoiceDate,RootInvoiceId FROM SalesInvoices WHERE Id=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);
            if(invoice==null)return EditEligibility.No("not_found","الفاتورة غير موجودة.");
            if(invoice.Status!="posted")return EditEligibility.No("not_posted","التعديل المالي متاح للفواتير المنشورة فقط.");
            if(await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM CustomerInstallmentContracts WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false)>0)return EditEligibility.No("installment_reversal_required","لا يمكن تصحيح فاتورة تقسيط مرتبطة بعقد؛ الإرجاع متاح من إدارة المرتجعات.");
            if(await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM SalesCancellations WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false)>0)return EditEligibility.No("canceled","الفاتورة الملغاة للعرض فقط.");
            if(await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM SalesReturns WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false)>0)return EditEligibility.No("linked_returns","لا يمكن تعديل فاتورة لها مرتجعات مرتبطة.");
            if(await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM SalesInvoiceCorrections WHERE PreviousInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false)>0)return EditEligibility.No("superseded","هذه مراجعة سابقة. افتح أحدث مراجعة للفاتورة.");
            var origin=await c.ExecuteScalarAsync<long?>(Cmd("SELECT ShiftId FROM SalesInvoiceShiftOrigins WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);
            // Historical invoices without a trustworthy origin are never assigned a shift.
            if(!origin.HasValue)return EditEligibility.No("missing_shift_origin","لا يوجد ربط موثوق بالوردية الأصلية؛ الفاتورة للعرض فقط.");
            var current=await c.ExecuteScalarAsync<long?>(Cmd("SELECT Id FROM CashierShifts WHERE CashierId=@User AND RegisterId='standalone' AND ClosedUtc IS NULL",new{User=userId},tx,ct)).ConfigureAwait(false);
            if(!current.HasValue||current.Value!=origin.Value)return EditEligibility.No("different_or_closed_shift","يمكن تعديل الفاتورة داخل ورديتها الأصلية المفتوحة فقط.");
            return EditEligibility.Yes();
        }

        public async Task<SaleCorrectionResult> CorrectAsync(long id,SaleCorrectionCommand command,long subtotal,long discount,long tax,long total,string hash,long userId,string localBusinessDate,CancellationToken ct)
        {
            try{using(var c=Open())using(var tx=c.BeginTransaction())
            {
                var prior=await c.QuerySingleOrDefaultAsync<CorrectionRow>(Cmd("SELECT ReplacementInvoiceId,RequestHash FROM SalesInvoiceCorrections WHERE OperationKey=@Key",new{Key=command.OperationKey},tx,ct)).ConfigureAwait(false);
                if(prior!=null)
                {
                    if(prior.RequestHash!=hash)return SaleCorrectionResult.Fail("operation_key_conflict","مفتاح العملية مستخدم لتصحيح مختلف.");
                    tx.Commit();return new SaleCorrectionResult{Succeeded=true,Replayed=true,Invoice=await GetAsync(prior.ReplacementInvoiceId,ct).ConfigureAwait(false)};
                }
                var eligibility=await EligibleAsync(c,tx,id,userId,localBusinessDate,ct).ConfigureAwait(false);
                if(!eligibility.Allowed)return SaleCorrectionResult.Fail(eligibility.Code,eligibility.Message);
                var old=await c.QuerySingleAsync<CorrectionInvoice>(Cmd("SELECT * FROM SalesInvoices WHERE Id=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);
                if(old.Version!=command.ExpectedVersion)return SaleCorrectionResult.Fail("stale_version","تغيرت الفاتورة منذ فتحها. أعد فتح أحدث مراجعة.");
                var input=command.Invoice;
                if(input.CustomerId!=old.CustomerId||input.WarehouseId!=old.WarehouseId||input.InvoiceDate!=old.InvoiceDate||input.PaymentMode!=old.PaymentMode)
                    return SaleCorrectionResult.Fail("unsupported_header_change","لا يدعم التصحيح تغيير العميل أو المخزن أو التاريخ أو نوع البيع.");
                await ValidateCorrectionReferencesAsync(c,tx,input,ct).ConfigureAwait(false);
                subtotal=await ValidateReferences(c,tx,input,discount,ct,false).ConfigureAwait(false);
                var settings=await TaxAsync(c,tx,ct).ConfigureAwait(false);var totals=Totals(subtotal,discount,settings.Rate,settings.Enabled,settings.Included);total=checked(totals.total+(input.Delivery==null?0:input.Delivery.FeeScaled));tax=totals.tax;
                var difference=Math.Abs(total-old.TotalScaled);var expected=total==old.TotalScaled?"none":total<old.TotalScaled?(command.SettlementKind=="refund"?"refund":"customer_credit"):(command.SettlementKind=="collect"?"collect":"receivable");
                if(command.SettlementKind!=expected)return SaleCorrectionResult.Fail("settlement_required",total<old.TotalScaled?"اختر رد الفرق فعليًا أو إضافته كرصيد للعميل.":total>old.TotalScaled?"اختر تحصيل الفرق أو تسجيله كمديونية على العميل.":"لا توجد تسوية مطلوبة عندما لا يتغير الإجمالي.");
                if((expected=="customer_credit"||expected=="receivable")&&!old.CustomerId.HasValue)return SaleCorrectionResult.Fail("customer_required","رصيد العميل أو المديونية يتطلبان تحديد عميل.");
                var root=old.RootInvoiceId??old.Id;var rootNumber=await c.ExecuteScalarAsync<string>(Cmd("SELECT InvoiceNumber FROM SalesInvoices WHERE Id=@Root",new{Root=root},tx,ct)).ConfigureAwait(false);
                var netPaid=await c.ExecuteScalarAsync<long>(Cmd(@"SELECT COALESCE(SUM(CASE WHEN p.EventType IN ('cash_sale','customer_collection') THEN p.AmountScaled WHEN p.EventType='refund' AND p.PaymentMethod<>'credit_adjustment' THEN -p.AmountScaled ELSE 0 END),0)
                    FROM SalesPayments p JOIN SalesInvoices i ON i.Id=p.SalesInvoiceId WHERE COALESCE(i.RootInvoiceId,i.Id)=@Root",new{Root=root},tx,ct)).ConfigureAwait(false);
                if(expected=="refund"&&netPaid<old.TotalScaled)return SaleCorrectionResult.Fail("refund_exceeds_paid","لا يمكن رد فرق نقدي لفاتورة غير مسددة بالكامل؛ استخدم رصيد العميل.");
                var now=Now();var reversalId=await ReverseInventoryAsync(c,tx,old,command.OperationKey,hash,userId,now,ct).ConfigureAwait(false);
                var revision=old.Revision+1;var internalNumber=rootNumber+"-R"+revision.ToString(CultureInfo.InvariantCulture);
                await c.ExecuteAsync(Cmd(@"INSERT INTO SalesInvoices(InvoiceNumber,InvoiceDate,CustomerId,WarehouseId,CashierUserId,Status,PaymentMode,SubtotalScaled,DiscountScaled,TaxScaled,TotalScaled,PaidScaled,TaxRate,TaxIncluded,CreatedUtc,RootInvoiceId,Revision)
                    VALUES(@Number,@Date,@CustomerId,@WarehouseId,@User,'draft',@Mode,@Subtotal,@Discount,@Tax,@Total,0,@Rate,@Included,@Now,@Root,@Revision)",new{Number=internalNumber,Date=old.InvoiceDate,old.CustomerId,old.WarehouseId,User=userId,Mode=old.PaymentMode,Subtotal=subtotal,Discount=discount,Tax=tax,Total=total,Rate=settings.Rate,Included=settings.Included?1:0,Now=now,Root=root,Revision=revision},tx,ct)).ConfigureAwait(false);
                var replacementId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                input.ImmediateMethod=old.PaymentMode;input.PaymentInstrumentId=null;
                var selection=await c.QuerySingleOrDefaultAsync<SaleInvoice>(Cmd("SELECT Method ImmediateMethod,InstrumentId PaymentInstrumentId FROM SalePaymentSelections WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);
                input.ImmediateMethod=selection?.ImmediateMethod??old.PaymentMode;input.PaymentInstrumentId=selection?.PaymentInstrumentId;
                await SaveSelectionAsync(c,tx,replacementId,input,ct).ConfigureAwait(false);await SaveDeliveryAsync(c,tx,replacementId,input,ct).ConfigureAwait(false);await SaveLinesAsync(c,tx,replacementId,input.Lines,ct).ConfigureAwait(false);await FinalizeLinesAsync(c,tx,replacementId,discount,tax,settings.Included,ct).ConfigureAwait(false);
                var replacementLines=await LinesAsync(c,tx,replacementId,ct).ConfigureAwait(false);var stockPlan=await BuildSaleInventoryPlanAsync(c,tx,replacementLines,"sale-correction-replacement:"+command.OperationKey,old.WarehouseId,userId,"previousInvoiceId="+id,ct).ConfigureAwait(false);var stockCommand=stockPlan.Command;
                var stock=await _inventory.PostSaleWithinTransactionAsync(c,tx,stockCommand,InventoryService.CanonicalHashPost(stockCommand),ct).ConfigureAwait(false);if(!stock.Succeeded)return SaleCorrectionResult.Fail(stock.ErrorCode,stock.ErrorMessage);
                await LinkSaleInventoryPlanAsync(c,tx,replacementId,stock.Value.DocumentId,stockPlan,ct).ConfigureAwait(false);
                var paid=old.PaymentMode=="cash"?(expected=="receivable"?Math.Min(total,netPaid):total):Math.Min(total,netPaid+(expected=="collect"?difference:0)-(expected=="refund"?difference:0));
                await c.ExecuteAsync(Cmd("UPDATE SalesInvoices SET Status='posted',InventoryDocumentId=@DocumentId,OperationKey=@Key,RequestHash=@Hash,PostedUtc=@Posted,PaidScaled=@Paid WHERE Id=@Id AND Status='draft'",new{DocumentId=stock.Value.DocumentId,Key="correction:"+command.OperationKey,Hash=hash,Posted=old.PostedUtc,Paid=paid,Id=replacementId},tx,ct)).ConfigureAwait(false);
                var treasuryMovement=(long?)null;
                if(expected=="refund"||expected=="collect")
                {
                    var accountType=await c.ExecuteScalarAsync<string>(Cmd("SELECT Type FROM TreasuryAccounts WHERE Id=@Id AND IsDeleted=0",new{Id=command.TreasuryAccountId},tx,ct)).ConfigureAwait(false);
                    if(accountType==null||(command.SettlementMethod=="cash"&&accountType!="cash")||(command.SettlementMethod=="transfer"&&accountType!="bank"))return SaleCorrectionResult.Fail("settlement_account_mismatch","اختر خزنة للدفع النقدي أو حسابًا بنكيًا للتحويل.");
                    var movement=await SqliteTreasuryStore.PostWithinTransactionAsync(c,tx,new TreasuryMovementCommand{AccountId=command.TreasuryAccountId.Value,Amount=MoneyPolicy.FormatForUser(difference),OperationKey="sale-correction-treasury:"+command.OperationKey,Notes=(expected=="refund"?"رد فرق ":"تحصيل فرق ")+rootNumber},expected=="refund"?"withdraw":"deposit",difference,hash,userId,ct).ConfigureAwait(false);treasuryMovement=movement.Id;
                }
                var correctionId=await c.ExecuteScalarAsync<long>(Cmd(@"INSERT INTO SalesInvoiceCorrections(RootInvoiceId,PreviousInvoiceId,ReplacementInvoiceId,Revision,ReversalInventoryDocumentId,OperationKey,RequestHash,SettlementKind,SettlementMethod,SettlementAmountScaled,TreasuryAccountId,TreasuryMovementId,ActorUserId,CreatedUtc)
                    VALUES(@Root,@Previous,@Replacement,@Revision,@Reversal,@Key,@Hash,@Kind,@Method,@Amount,@Account,@Movement,@User,@Now); SELECT last_insert_rowid();",new{Root=root,Previous=id,Replacement=replacementId,Revision=revision,Reversal=reversalId,Key=command.OperationKey,Hash=hash,Kind=expected,Method=command.SettlementMethod,Amount=difference,Account=command.TreasuryAccountId,Movement=treasuryMovement,User=userId,Now=now},tx,ct)).ConfigureAwait(false);
                await ApplyFinancialDifferenceAsync(c,tx,old,replacementId,correctionId,expected,command.SettlementMethod,difference,hash,command.OperationKey,userId,rootNumber,now,ct).ConfigureAwait(false);
                var shiftId=await c.ExecuteScalarAsync<long?>(Cmd("SELECT ShiftId FROM SalesInvoiceShiftOrigins WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);
                if(shiftId.HasValue)await c.ExecuteAsync(Cmd("INSERT INTO SalesInvoiceShiftOrigins(SalesInvoiceId,ShiftId,CreatedUtc) VALUES(@Invoice,@Shift,@Now)",new{Invoice=replacementId,Shift=shiftId.Value,Now=now},tx,ct)).ConfigureAwait(false);
                if(difference>0&&(expected=="refund"||expected=="collect")&&command.SettlementMethod=="cash"&&shiftId.HasValue)await c.ExecuteAsync(Cmd("INSERT INTO CashierShiftEvents(ShiftId,Kind,AmountScaled,ActorUserId,Reason,CreatedUtc,SalesCorrectionId) VALUES(@Shift,@Kind,@Amount,@User,@Reason,@Now,@Correction)",new{Shift=shiftId.Value,Kind=expected=="refund"?"out":"in",Amount=difference,User=userId,Reason="تصحيح فاتورة "+rootNumber,Now=now,Correction=correctionId},tx,ct)).ConfigureAwait(false);
                await AuditAsync(c,tx,replacementId,userId,"posted",now,"correctionOf="+id+";reversalDocumentId="+reversalId,ct).ConfigureAwait(false);
                tx.Commit();var result=await GetAsync(replacementId,ct).ConfigureAwait(false);result.InvoiceNumber=rootNumber;result.CanEditPosted=true;return new SaleCorrectionResult{Succeeded=true,Invoice=result};
            }}catch(ArgumentException e){return SaleCorrectionResult.Fail("validation",e.Message);}catch(TreasuryException e){return SaleCorrectionResult.Fail("treasury_"+e.Code,e.Message);}catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Busy||e.ResultCode==SQLiteErrorCode.Locked){return SaleCorrectionResult.Fail("database_busy","قاعدة البيانات مشغولة. أعد المحاولة بالمفتاح نفسه.");}catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Constraint){return SaleCorrectionResult.Fail("sales_conflict","تعارض التصحيح مع حركة أخرى؛ بقيت الفاتورة الأصلية فعالة دون تغيير.");}
        }

        private static async Task ValidateCorrectionReferencesAsync(SQLiteConnection c,SQLiteTransaction tx,SaleDraftCommand command,CancellationToken ct)
        {
            if(await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM Warehouses WHERE Id=@Id AND IsActive=1",new{Id=command.WarehouseId},tx,ct)).ConfigureAwait(false)!=1)throw new ArgumentException("المخزن غير نشط أو غير موجود.");
            foreach(var x in command.Lines)if(await c.ExecuteScalarAsync<long>(Cmd(@"SELECT COUNT(*) FROM ProductVariants v JOIN ProductDetails d ON d.ProductId=v.ProductId JOIN ProductUnits pu ON pu.ProductId=v.ProductId AND pu.Id=@Unit AND pu.IsActive=1 AND pu.IsSaleUnit=1 WHERE v.Id=@Variant AND v.IsActive=1 AND v.ArchivedUtc IS NULL AND d.IsActive=1 AND d.ArchivedUtc IS NULL AND d.ProductType='stock'",new{Unit=x.ProductUnitId,Variant=x.VariantId},tx,ct)).ConfigureAwait(false)!=1)throw new ArgumentException("أحد الأصناف أو وحدات البيع لم يعد متاحًا.");
        }

        private static async Task ApplyFinancialDifferenceAsync(SQLiteConnection c,SQLiteTransaction tx,CorrectionInvoice old,long replacementId,long correctionId,string kind,string method,long amount,string hash,string key,long userId,string number,string now,CancellationToken ct)
        {
            if(amount==0)return;
            if(kind=="collect"||kind=="refund"||kind=="customer_credit")await c.ExecuteAsync(Cmd(@"INSERT INTO SalesPayments(SalesInvoiceId,CustomerId,ActorUserId,EventType,PaymentMethod,AmountScaled,OperationKey,RequestHash,OccurredOn,Notes,CreatedUtc,SalesCorrectionId)
                VALUES(@Invoice,@Customer,@User,@Event,@Method,@Amount,@Key,@Hash,@Date,@Notes,@Now,@Correction)",new{Invoice=replacementId,Customer=old.CustomerId,User=userId,Event=kind=="collect"?"customer_collection":"refund",Method=kind=="customer_credit"?"credit_adjustment":method,Amount=amount,Key="sale-correction-payment:"+key,Hash=hash,Date=old.InvoiceDate,Notes="فرق تصحيح فاتورة "+number,Now=now,Correction=correctionId},tx,ct)).ConfigureAwait(false);
            if(!old.CustomerId.HasValue)return;
            var entries=new List<string>();
            if(old.PaymentMode=="credit")
            {
                if(kind=="collect"){entries.Add("increase");entries.Add("collection");}
                else if(kind=="refund"){entries.Add("reduction");entries.Add("increase");}
                else if(kind=="customer_credit")entries.Add("reduction");
                else if(kind=="receivable")entries.Add("increase");
            }
            else if(kind=="customer_credit")entries.Add("reduction");
            else if(kind=="receivable")entries.Add("increase");
            decimal balanceDelta=0;var index=0;
            foreach(var type in entries)
            {
                index++;await c.ExecuteAsync(Cmd("INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc,Reference,SalesInvoiceId,SalesCorrectionId) VALUES(@Customer,@Key,@Hash,@Type,@Amount,@Date,@Notes,@Now,@Reference,@Invoice,@Correction)",new{Customer=old.CustomerId,Key="sale-correction-ledger:"+key+":"+index,Hash=hash,Type=type,Amount=amount,Date=old.InvoiceDate,Notes="فرق تصحيح فاتورة "+number,Now=now,Reference=number,Invoice=replacementId,Correction=correctionId},tx,ct)).ConfigureAwait(false);
                balanceDelta+=(type=="increase"?1m:-1m)*amount/10000m;
            }
            if(balanceDelta!=0)await c.ExecuteAsync(Cmd("UPDATE Customers SET Balance=Balance+@Delta,UpdatedAt=@Now WHERE Id=@Id",new{Delta=balanceDelta,Now=now,Id=old.CustomerId},tx,ct)).ConfigureAwait(false);
        }

        private static async Task<long> ReverseInventoryAsync(SQLiteConnection c,SQLiteTransaction tx,CorrectionInvoice invoice,string key,string hash,long userId,string now,CancellationToken ct)
        {
            if(!invoice.InventoryDocumentId.HasValue)throw new ArgumentException("مستند مخزون الفاتورة غير مكتمل.");
            var movements=(await c.QueryAsync<CorrectionMovement>(Cmd(@"SELECT m.Id,m.VariantId,m.BatchId,m.QuantityBaseScaled,m.ValueScaled,l.ProductUnitId,l.EnteredQuantityScaled,l.ToBaseNumerator,l.ToBaseDenominator FROM StockMovements m JOIN InventoryDocumentLines l ON l.Id=m.DocumentLineId WHERE m.DocumentId=@Document AND m.WarehouseId=@Warehouse ORDER BY l.LineNumber",new{Document=invoice.InventoryDocumentId,Warehouse=invoice.WarehouseId},tx,ct)).ConfigureAwait(false)).ToList();
            await c.ExecuteAsync(Cmd("INSERT INTO InventoryDocuments(DocumentType,Status,SourceWarehouseId,Reason,Notes,CreatedByUserId,CreatedUtc,OperationKey,RequestHash,ReversesDocumentId) VALUES('manual_receipt','draft',@Warehouse,@Reason,@Notes,@User,@Now,@Key,@Hash,@Original)",new{Warehouse=invoice.WarehouseId,Reason="عكس مخزون تصحيح فاتورة "+invoice.InvoiceNumber,Notes="invoiceId="+invoice.Id,User=userId,Now=now,Key="sale-correction-reversal:"+key,Hash=hash,Original=invoice.InventoryDocumentId},tx,ct)).ConfigureAwait(false);
            var documentId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);var lineNumber=0;
            foreach(var movement in movements)
            {
                lineNumber++;var allocations=(await c.QueryAsync<CorrectionAllocation>(Cmd("SELECT a.LayerId,a.QuantityScaled,a.ValueScaled,l.OriginMovementId,l.UnitCostScaled FROM InventoryCostAllocations a JOIN InventoryCostLayers l ON l.Id=a.LayerId WHERE a.MovementId=@Id ORDER BY a.Id",new{Id=movement.Id},tx,ct)).ConfigureAwait(false)).ToList();
                if(allocations.Sum(x=>x.QuantityScaled)!=-movement.QuantityBaseScaled||allocations.Sum(x=>x.ValueScaled)!=-movement.ValueScaled)throw new ArgumentException("تفاصيل تكلفة الأصناف المباعة الأصلية غير متطابقة؛ تعذر التصحيح بأمان.");
                var balance=await c.QuerySingleOrDefaultAsync<CorrectionBalance>(Cmd("SELECT QuantityBaseScaled,ValueScaled FROM InventoryBalances WHERE WarehouseId=@Warehouse AND VariantId=@Variant AND BatchId IS @Batch",new{Warehouse=invoice.WarehouseId,Variant=movement.VariantId,Batch=movement.BatchId},tx,ct)).ConfigureAwait(false);
                var layers=await c.QuerySingleAsync<CorrectionBalance>(Cmd("SELECT COALESCE(SUM(RemainingQuantityScaled),0) QuantityBaseScaled,COALESCE(SUM(RemainingValueScaled),0) ValueScaled FROM InventoryCostLayers WHERE WarehouseId=@Warehouse AND VariantId=@Variant AND BatchId IS @Batch",new{Warehouse=invoice.WarehouseId,Variant=movement.VariantId,Batch=movement.BatchId},tx,ct)).ConfigureAwait(false);
                if(balance==null||balance.QuantityBaseScaled!=layers.QuantityBaseScaled||balance.ValueScaled!=layers.ValueScaled)throw new ArgumentException("رصيد المخزون ودفعات المخزون غير متطابقين.");
                await c.ExecuteAsync(Cmd("INSERT INTO InventoryDocumentLines(DocumentId,LineNumber,VariantId,BatchId,EnteredQuantityScaled,ProductUnitId,ToBaseNumerator,ToBaseDenominator,BaseQuantityScaled) VALUES(@Document,@Line,@Variant,@Batch,@Entered,@Unit,@Numerator,@Denominator,@Base)",new{Document=documentId,Line=lineNumber,Variant=movement.VariantId,Batch=movement.BatchId,Entered=movement.EnteredQuantityScaled,Unit=movement.ProductUnitId,Numerator=movement.ToBaseNumerator,Denominator=movement.ToBaseDenominator,Base=-movement.QuantityBaseScaled},tx,ct)).ConfigureAwait(false);
                var lineId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                await c.ExecuteAsync(Cmd("INSERT INTO StockMovements(DocumentId,DocumentLineId,WarehouseId,VariantId,BatchId,QuantityBaseScaled,ValueScaled,CreatedUtc) VALUES(@Document,@Line,@Warehouse,@Variant,@Batch,@Quantity,@Value,@Now)",new{Document=documentId,Line=lineId,Warehouse=invoice.WarehouseId,Variant=movement.VariantId,Batch=movement.BatchId,Quantity=-movement.QuantityBaseScaled,Value=-movement.ValueScaled,Now=now},tx,ct)).ConfigureAwait(false);var newMovement=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                foreach(var a in allocations)await c.ExecuteAsync(Cmd(@"INSERT INTO InventoryCostLayers(WarehouseId,VariantId,BatchId,SourceMovementId,ParentLayerId,OriginMovementId,AcquiredUtc,Basis,QuantityScaled,ValueScaled,UnitCostScaled,RemainingQuantityScaled,RemainingValueScaled,IsSellable)
                    VALUES(@Warehouse,@Variant,@Batch,@Movement,@Parent,@Origin,@Now,'receipt',@Quantity,@Value,@Cost,@Quantity,@Value,
                    CASE WHEN EXISTS(SELECT 1 FROM ProductBatches b WHERE b.Id=@Batch AND b.ExpirationDate<@Today) THEN 0 ELSE 1 END)",new{Warehouse=invoice.WarehouseId,Variant=movement.VariantId,Batch=movement.BatchId,Movement=newMovement,Parent=a.LayerId,Origin=a.OriginMovementId,Now=now,Quantity=a.QuantityScaled,Value=a.ValueScaled,Cost=a.UnitCostScaled,Today=DateTime.Today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)},tx,ct)).ConfigureAwait(false);
                var units=(await c.QueryAsync<long>(Cmd("SELECT PhysicalUnitId FROM StockMovementUnits WHERE MovementId=@Movement",new{Movement=movement.Id},tx,ct)).ConfigureAwait(false)).ToList();
                foreach(var unit in units){if(await c.ExecuteAsync(Cmd("UPDATE InventoryPhysicalUnits SET WarehouseId=@Warehouse,UpdatedUtc=@Now WHERE Id=@Unit AND WarehouseId IS NULL",new{Warehouse=invoice.WarehouseId,Now=now,Unit=unit},tx,ct)).ConfigureAwait(false)!=1)throw new ArgumentException("إحدى الوحدات المتسلسلة لم تعد قابلة للعكس.");await c.ExecuteAsync(Cmd("INSERT INTO StockMovementUnits(MovementId,PhysicalUnitId) VALUES(@Movement,@Unit)",new{Movement=newMovement,Unit=unit},tx,ct)).ConfigureAwait(false);}
                await c.ExecuteAsync(Cmd("UPDATE InventoryBalances SET QuantityBaseScaled=QuantityBaseScaled+@Quantity,ValueScaled=ValueScaled+@Value,Version=Version+1,UpdatedUtc=@Now WHERE WarehouseId=@Warehouse AND VariantId=@Variant AND BatchId IS @Batch",new{Quantity=-movement.QuantityBaseScaled,Value=-movement.ValueScaled,Now=now,Warehouse=invoice.WarehouseId,Variant=movement.VariantId,Batch=movement.BatchId},tx,ct)).ConfigureAwait(false);
            }
            await c.ExecuteAsync(Cmd("UPDATE InventoryDocuments SET Status='posted',PostedUtc=@Now WHERE Id=@Id",new{Now=now,Id=documentId},tx,ct)).ConfigureAwait(false);await c.ExecuteAsync(Cmd("INSERT INTO InventoryAudit(DocumentId,ActorUserId,EventType,EventUtc,Details) VALUES(@Id,@User,'posted',@Now,@Details)",new{Id=documentId,User=userId,Now=now,Details="salesCorrectionInvoiceId="+invoice.Id},tx,ct)).ConfigureAwait(false);return documentId;
        }
    }
}
