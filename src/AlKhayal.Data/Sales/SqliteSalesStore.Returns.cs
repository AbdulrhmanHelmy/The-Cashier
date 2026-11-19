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
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Sales;
using AlKhayal.Data.Inventory;
using Dapper;

namespace AlKhayal.Data.Sales
{
    public sealed partial class SqliteSalesStore
    {
        public async Task<SaleReturnResult> ReturnAsync(long id,SaleReturnCommand command,long userId,CancellationToken ct)
        {
            var hash=ReturnHash(id,command,userId);
            try
            {
                using(var c=Open())using(var tx=c.BeginTransaction())
                {
                    var prior=await c.QuerySingleOrDefaultAsync<ReturnResultRow>(Cmd("SELECT r.Id,r.ReturnNumber,r.InventoryDocumentId,r.TotalScaled,r.CashRefundScaled,r.CreditReductionScaled,r.RequestHash,COALESCE(a.CashRefundScaled,0) InterestCashRefundScaled,COALESCE(a.DebtReductionScaled-a.ScheduleReductionScaled,0) InterestCreditScaled FROM SalesReturns r LEFT JOIN CustomerInstallmentInterestAdjustments a ON a.SalesReturnId=r.Id WHERE r.OperationKey=@Key",new{Key=command.OperationKey},tx,ct)).ConfigureAwait(false);
                    if(prior!=null){tx.Commit();return prior.RequestHash==hash?prior.ToResult(true):new SaleReturnResult{ErrorCode="operation_key_conflict",ErrorMessage="مفتاح العملية مستخدم لبيانات مختلفة."};}
                    var invoice=await c.QuerySingleOrDefaultAsync<ReturnInvoice>(Cmd("SELECT Id,CustomerId,WarehouseId,Status,PaymentMode,InvoiceNumber,PaidScaled FROM SalesInvoices WHERE Id=@Id AND NOT EXISTS(SELECT 1 FROM SalesInvoiceCorrections correction WHERE correction.PreviousInvoiceId=SalesInvoices.Id)",new{Id=id},tx,ct)).ConfigureAwait(false);
                    if(invoice==null)return new SaleReturnResult{ErrorCode="not_found",ErrorMessage="الفاتورة غير موجودة أو استُبدلت بتصحيح. افتح أحدث مراجعة للفاتورة."};
                    var contractId=await c.ExecuteScalarAsync<long?>(Cmd("SELECT Id FROM CustomerInstallmentContracts WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);
                    var electronic=await c.QuerySingleOrDefaultAsync<ElectronicSelection>(Cmd(@"SELECT s.Method,s.InstrumentId,p.OperationKey OriginalPaymentKey FROM SalePaymentSelections s
                        JOIN SalesInvoices i ON i.Id=s.SalesInvoiceId JOIN SalesPayments p ON p.SalesInvoiceId=COALESCE(i.RootInvoiceId,i.Id) AND p.EventType='cash_sale'
                        WHERE s.SalesInvoiceId=@Id AND s.Method IN ('wallet','card')",new{Id=id},tx,ct)).ConfigureAwait(false);
                    if(invoice.Status!="posted"||await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM SalesCancellations WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false)>0)return new SaleReturnResult{ErrorCode="return_not_allowed",ErrorMessage="لا يمكن إرجاع فاتورة غير منشورة أو ملغاة."};
                    var rows=new List<ReturnLine>();var seen=new HashSet<long>();long total=0;
                    foreach(var request in command.Lines)
                    {
                        if(!seen.Add(request.InvoiceLineId))return new SaleReturnResult{ErrorCode="duplicate_line",ErrorMessage="لا تكرر بند الفاتورة في طلب الإرجاع."};
                        long qty;if(!ScaleInput(request.Quantity,6,out qty)||qty<=0)return new SaleReturnResult{ErrorCode="invalid_quantity",ErrorMessage="كمية الإرجاع غير صحيحة."};
                        var line=await c.QuerySingleOrDefaultAsync<ReturnLine>(Cmd(@"SELECT l.Id,l.VariantId,l.ProductUnitId,l.LineTotalScaled,l.InventoryDocumentLineId,l.QuantityScaled SoldQuantity,
                            COALESCE((SELECT SUM(QuantityScaled) FROM SalesReturnLines WHERE SalesInvoiceLineId=l.Id),0) ReturnedQuantity,
                            d.BatchId,d.ToBaseNumerator,d.ToBaseDenominator
                            FROM SalesInvoiceLines l LEFT JOIN InventoryDocumentLines d ON d.Id=l.InventoryDocumentLineId
                            WHERE l.Id=@LineId AND l.SalesInvoiceId=@InvoiceId",new{LineId=request.InvoiceLineId,InvoiceId=id},tx,ct)).ConfigureAwait(false);
                        if(line==null||qty>line.SoldQuantity-line.ReturnedQuantity)return new SaleReturnResult{ErrorCode="return_quantity_exceeded",ErrorMessage="كمية الإرجاع أكبر من الكمية المتبقية من الفاتورة."};
                        if(line.InventoryDocumentLineId.HasValue)
                        {
                            var saleMovement=await c.ExecuteScalarAsync<long>(Cmd("SELECT Id FROM StockMovements WHERE DocumentLineId=@Id AND QuantityBaseScaled<0",new{Id=line.InventoryDocumentLineId.Value},tx,ct)).ConfigureAwait(false);
                            line.Components.Add(new ReturnStockPart{DocumentLineId=line.InventoryDocumentLineId.Value,SaleMovementId=saleMovement,VariantId=line.VariantId,BatchId=line.BatchId,ProductUnitId=line.ProductUnitId,ToBaseNumerator=line.ToBaseNumerator,ToBaseDenominator=line.ToBaseDenominator});
                            line.SaleMovementId=saleMovement;
                        }
                        else
                        {
                            var mapped=(await c.QueryAsync<ReturnStockPart>(Cmd(@"SELECT s.InventoryDocumentLineId DocumentLineId,s.QuantityBaseScaled SoldComponentQuantity,
                                dl.VariantId,dl.BatchId,dl.ProductUnitId,dl.ToBaseNumerator,dl.ToBaseDenominator,
                                COALESCE((SELECT sm.Id FROM StockMovements sm WHERE sm.DocumentLineId=dl.Id AND sm.QuantityBaseScaled<0 LIMIT 1),0) SaleMovementId
                                FROM SalesInvoiceStockEntries s JOIN InventoryDocumentLines dl ON dl.Id=s.InventoryDocumentLineId
                                LEFT JOIN ProductBatches b ON b.Id=dl.BatchId WHERE s.SalesInvoiceLineId=@LineId
                                ORDER BY b.ExpirationDate DESC,dl.Id DESC",new{LineId=line.Id},tx,ct)).ConfigureAwait(false)).ToList();
                            if(mapped.Count>0)
                            {
                                line.IsMapped=true;
                                line.Components=mapped.Where(x=>x.SaleMovementId>0).ToList();
                                var unit=await c.QuerySingleAsync<ReturnUnit>(Cmd("SELECT ToBaseNumerator,ToBaseDenominator FROM ProductUnits WHERE Id=@Id",new{Id=line.ProductUnitId},tx,ct)).ConfigureAwait(false);
                                line.ToBaseNumerator=unit.ToBaseNumerator;line.ToBaseDenominator=unit.ToBaseDenominator;
                                var soldBase=(decimal)line.SoldQuantity*unit.ToBaseNumerator/unit.ToBaseDenominator;
                                if(soldBase!=decimal.Truncate(soldBase)||mapped.Sum(x=>(decimal)x.SoldComponentQuantity)!=soldBase)
                                    return new SaleReturnResult{ErrorCode="legacy_allocation_incomplete",ErrorMessage="تفاصيل صرف الفاتورة غير مكتملة؛ لم يتغير المخزون."};
                            }
                            else
                            {
                            var componentRows=(await c.QueryAsync<ReturnStockPart>(Cmd(@"SELECT bc.InventoryDocumentLineId DocumentLineId,bc.QuantityBaseScaled SoldComponentQuantity,
                                dl.VariantId,dl.BatchId,dl.ProductUnitId,dl.ToBaseNumerator,dl.ToBaseDenominator,
                                (SELECT sm.Id FROM StockMovements sm WHERE sm.DocumentLineId=dl.Id AND sm.QuantityBaseScaled<0 LIMIT 1) SaleMovementId
                                FROM SalesInvoiceBundleComponentLines bc JOIN InventoryDocumentLines dl ON dl.Id=bc.InventoryDocumentLineId
                                WHERE bc.SalesInvoiceLineId=@LineId ORDER BY dl.LineNumber",new{LineId=line.Id},tx,ct)).ConfigureAwait(false)).ToList();
                            if(componentRows.Count<2)return new SaleReturnResult{ErrorCode="bundle_integrity",ErrorMessage="تركيبة الفاتورة غير مكتملة؛ لم يتغير المخزون."};
                            foreach(var part in componentRows)
                            {
                                var componentBase=(decimal)part.SoldComponentQuantity*qty/line.SoldQuantity;
                                if(componentBase!=decimal.Truncate(componentBase)||componentBase<=0)return new SaleReturnResult{ErrorCode="invalid_unit_quantity",ErrorMessage="كمية المكونات لا تتطابق مع كمية العبوات المرتجعة."};
                                part.RequestedQuantity=(long)componentBase;part.BaseQuantity=(long)componentBase;line.Components.Add(part);
                            }
                            }
                        }
                        var availableUnitCount=line.InventoryDocumentLineId.HasValue?await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM StockMovementUnits smu JOIN InventoryPhysicalUnits p ON p.Id=smu.PhysicalUnitId WHERE smu.MovementId=@Id AND p.WarehouseId IS NULL",new{Id=line.SaleMovementId},tx,ct)).ConfigureAwait(false):line.IsMapped?await c.ExecuteScalarAsync<long>(Cmd(@"SELECT COUNT(*) FROM SalesInvoiceStockEntries s JOIN StockMovements m ON m.DocumentLineId=s.InventoryDocumentLineId
                            JOIN StockMovementUnits smu ON smu.MovementId=m.Id JOIN InventoryPhysicalUnits p ON p.Id=smu.PhysicalUnitId
                            WHERE s.SalesInvoiceLineId=@Id AND p.WarehouseId IS NULL",new{Id=line.Id},tx,ct)).ConfigureAwait(false):0;
                        var product=await c.QuerySingleOrDefaultAsync<ReturnProduct>(Cmd(@"SELECT v.ProductId,COALESCE((SELECT a.IsEnabled FROM ProductCapabilityAssignments a WHERE a.ProductId=v.ProductId AND a.CapabilityKey='batch-tracking'),0) BatchTracked,
                            COALESCE((SELECT a.IsEnabled FROM ProductCapabilityAssignments a WHERE a.ProductId=v.ProductId AND a.CapabilityKey='serial-numbers'),0) SerialTracked,
                            COALESCE((SELECT a.IsEnabled FROM ProductCapabilityAssignments a WHERE a.ProductId=v.ProductId AND a.CapabilityKey='imei-tracking'),0) ImeiTracked
                            FROM ProductVariants v WHERE v.Id=@Id",new{Id=line.VariantId},tx,ct)).ConfigureAwait(false);
                        if(product==null)return new SaleReturnResult{ErrorCode="not_found",ErrorMessage="المنتج غير موجود."};
                        var baseQty=line.InventoryDocumentLineId.HasValue||line.IsMapped?checked(qty*line.ToBaseNumerator/line.ToBaseDenominator):qty;
                        if((line.InventoryDocumentLineId.HasValue||line.IsMapped)&&(baseQty<=0||qty*line.ToBaseNumerator%line.ToBaseDenominator!=0))return new SaleReturnResult{ErrorCode="invalid_unit_quantity",ErrorMessage="الكمية لا تطابق تحويل وحدة البيع."};
                        if(product.SerialTracked||product.ImeiTracked)
                        {
                            line.SerialTracked=true;
                            var serials=request.Serials??new List<string>();
                            if(baseQty%1000000L!=0||serials.Count!=Math.Min(baseQty/1000000L,availableUnitCount)||serials.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=serials.Count)return new SaleReturnResult{ErrorCode="serial_required",ErrorMessage="اختر رقمًا تسلسليًا لكل وحدة مرتجعة سبق صرفها من المخزون."};
                            if(serials.Count>0)
                            {
                                var selected=(await c.QueryAsync<ReturnPhysical>(Cmd(@"SELECT p.Id,m.Id SaleMovementId FROM StockMovementUnits smu
                                    JOIN StockMovements m ON m.Id=smu.MovementId JOIN InventoryPhysicalUnits p ON p.Id=smu.PhysicalUnitId
                                    JOIN InventoryIdentifiers i ON i.PhysicalUnitId=p.Id AND i.Kind='serial'
                                    WHERE p.WarehouseId IS NULL AND i.DisplayValue IN @Serials AND
                                    (m.Id=@Movement OR EXISTS(SELECT 1 FROM SalesInvoiceStockEntries s WHERE s.SalesInvoiceLineId=@Line AND s.InventoryDocumentLineId=m.DocumentLineId))",new{Movement=line.SaleMovementId,Line=line.Id,Serials=serials},tx,ct)).ConfigureAwait(false)).ToList();
                                line.PhysicalUnitIds=selected.Select(x=>x.Id).ToList();line.PhysicalUnitMovements=selected.ToDictionary(x=>x.Id,x=>x.SaleMovementId);
                            }
                            if(line.PhysicalUnitIds.Count!=serials.Count)return new SaleReturnResult{ErrorCode="serial_not_available",ErrorMessage="أحد الأرقام التسلسلية لا يتبع الفاتورة أو سبق إرجاعه."};
                            line.Serials=serials;
                        }
                        else if(request.Serials!=null&&request.Serials.Count>0)return new SaleReturnResult{ErrorCode="unexpected_serial",ErrorMessage="الأرقام التسلسلية غير مطلوبة لهذا الصنف."};
                        var returnedAmount=await c.ExecuteScalarAsync<long>(Cmd("SELECT COALESCE(SUM(AmountScaled),0) FROM SalesReturnLines WHERE SalesInvoiceLineId=@Id",new{Id=line.Id},tx,ct)).ConfigureAwait(false);
                        line.RequestedQuantity=qty;line.BaseQuantity=baseQty;line.IsDamaged=request.IsDamaged;
                        line.Amount=qty+line.ReturnedQuantity==line.SoldQuantity?line.LineTotalScaled-returnedAmount:checked((long)(decimal.Floor((decimal)line.LineTotalScaled*qty/line.SoldQuantity/100m)*100m));
                        // A fully discounted sold line legitimately returns zero money: it still
                        // restores stock and original FIFO cost below. Only a negative residual,
                        // which would mean a corrupted cap history, is rejected before any write.
                        if(line.Amount<0)return new SaleReturnResult{ErrorCode="negative_return",ErrorMessage="قيمة الإرجاع غير صحيحة."};
                        rows.Add(line);total=checked(total+line.Amount);
                    }
                    if(rows.Count==0)return new SaleReturnResult{ErrorCode="empty_return",ErrorMessage="اختر بندًا واحدًا على الأقل."};
                    var netPaid=await c.ExecuteScalarAsync<long>(Cmd(@"SELECT COALESCE(SUM(CASE WHEN p.EventType IN ('cash_sale','customer_collection') THEN p.AmountScaled WHEN p.EventType='refund' AND p.PaymentMethod<>'credit_adjustment' THEN -p.AmountScaled ELSE 0 END),0)
                        FROM SalesPayments p JOIN SalesInvoices source ON source.Id=p.SalesInvoiceId JOIN SalesInvoices target ON target.Id=@Id
                        WHERE COALESCE(source.RootInvoiceId,source.Id)=COALESCE(target.RootInvoiceId,target.Id)",new{Id=id},tx,ct)).ConfigureAwait(false);
                    var cash=Math.Min(total,Math.Max(0,netPaid));var credit=total-cash;
                    if(credit>0&&(invoice.PaymentMode!="credit"||!invoice.CustomerId.HasValue))return new SaleReturnResult{ErrorCode="refund_unfunded",ErrorMessage="قيمة الإرجاع تتجاوز المدفوع ولا يوجد رصيد آجل للعميل."};
                    long balanceAfterCredit=0,interestAdjustment=0,interestDebtReduction=0,interestScheduleReduction=0,interestCashRefund=0;
                    if(invoice.PaymentMode=="credit" && invoice.CustomerId.HasValue)
                    {
                        var balance=await c.ExecuteScalarAsync<long>(Cmd("SELECT CAST(ROUND(Balance*10000) AS INTEGER) FROM Customers WHERE Id=@Id",new{Id=invoice.CustomerId},tx,ct)).ConfigureAwait(false);
                        var settlement=SalesRefundPolicy.CapDebtReduction(total,balance);
                        cash=settlement.Cash; credit=settlement.Credit;
                        if(contractId.HasValue)
                        {
                            var contractDue=await InstallmentDueAsync(c,tx,contractId.Value,ct).ConfigureAwait(false);
                            if(balance<contractDue)return new SaleReturnResult{ErrorCode="installment_balance_mismatch",ErrorMessage="دين العميل أقل من الأقساط المتبقية؛ راجع كشف الحساب قبل الإرجاع."};
                            credit=Math.Min(credit,contractDue);cash=total-credit;
                            var contract=await c.QuerySingleAsync<InstallmentInterestContract>(Cmd("SELECT ProductPriceScaled,TotalAmountScaled,InterestReturnPolicy FROM CustomerInstallmentContracts WHERE Id=@Id",new{Id=contractId.Value},tx,ct)).ConfigureAwait(false);
                            var totalInterest=contract.TotalAmountScaled-contract.ProductPriceScaled;
                            if(contract.InterestReturnPolicy=="cancel"&&totalInterest>0)
                            {
                                var priorReturns=await c.ExecuteScalarAsync<long>(Cmd("SELECT COALESCE(SUM(TotalScaled),0) FROM SalesReturns WHERE SalesInvoiceId=@Id",new{Id=id},tx,ct)).ConfigureAwait(false);
                                var priorAdjustments=await c.ExecuteScalarAsync<long>(Cmd("SELECT COALESCE(SUM(AmountScaled),0) FROM CustomerInstallmentInterestAdjustments WHERE ContractId=@Id",new{Id=contractId.Value},tx,ct)).ConfigureAwait(false);
                                var cumulative=Math.Min(contract.ProductPriceScaled,checked(priorReturns+total));
                                var target=MoneyPolicy.RoundInternalToPiastre(checked((long)decimal.Round(totalInterest*(cumulative/(decimal)contract.ProductPriceScaled),0,MidpointRounding.AwayFromZero)));
                                var requested=Math.Max(0,target-priorAdjustments);
                                var paidInterest=await c.ExecuteScalarAsync<long>(Cmd(@"SELECT COALESCE(SUM(InterestAmountScaled),0) FROM
                                    (SELECT InterestAmountScaled FROM CustomerInstallmentPayments WHERE ContractId=@Id UNION ALL SELECT InterestAmountScaled FROM CustomerInstallmentPartialPayments WHERE ContractId=@Id)",new{Id=contractId.Value},tx,ct)).ConfigureAwait(false);
                                var paidRefunded=await c.ExecuteScalarAsync<long>(Cmd("SELECT COALESCE(SUM(CashRefundScaled+DebtReductionScaled-ScheduleReductionScaled),0) FROM CustomerInstallmentInterestAdjustments WHERE ContractId=@Id",new{Id=contractId.Value},tx,ct)).ConfigureAwait(false);
                                var priorScheduleReduction=await c.ExecuteScalarAsync<long>(Cmd("SELECT COALESCE(SUM(ScheduleReductionScaled),0) FROM CustomerInstallmentInterestAdjustments WHERE ContractId=@Id",new{Id=contractId.Value},tx,ct)).ConfigureAwait(false);
                                var unpaid=Math.Max(0,totalInterest-paidInterest-priorScheduleReduction);
                                var paidRemaining=Math.Max(0,paidInterest-paidRefunded);
                                interestScheduleReduction=Math.Min(requested,Math.Min(unpaid,Math.Max(0,contractDue-credit)));
                                var paidPart=Math.Min(Math.Max(0,requested-interestScheduleReduction),paidRemaining);
                                interestAdjustment=interestScheduleReduction+paidPart;
                                interestDebtReduction=interestScheduleReduction+(command.InterestSettlement=="credit"?paidPart:0);
                                interestCashRefund=command.InterestSettlement=="cash"?paidPart:0;
                            }
                        }
                        balanceAfterCredit=checked(balance-credit-interestDebtReduction);
                    }
                    var now=Now();var returnNumber="RET-"+DateTime.UtcNow.ToString("yyyyMMdd",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,8).ToUpperInvariant();
                    await c.ExecuteAsync(Cmd(@"INSERT INTO InventoryDocuments(DocumentType,Status,SourceWarehouseId,Reason,Notes,CreatedByUserId,CreatedUtc,OperationKey,RequestHash)
                        VALUES('manual_receipt','draft',@Warehouse,@Reason,@Notes,@User,@Now,@Key,@Hash)",new{Warehouse=invoice.WarehouseId,Reason="إرجاع فاتورة بيع "+invoice.InvoiceNumber,Notes="salesReturn="+command.OperationKey,User=userId,Now=now,Key="sales-return:"+command.OperationKey,Hash=hash},tx,ct)).ConfigureAwait(false);
                    var documentId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                    var made=new List<MadeLine>();long lineValueTotal=0;var lineNumber=0;
                    foreach(var line in rows)
                    {
                        var lineAllocations=new List<ReturnCost>();
                        var mappedRemainder=line.BaseQuantity;
                        foreach(var part in line.Components)
                        {
                            if(line.InventoryDocumentLineId.HasValue){part.RequestedQuantity=line.RequestedQuantity;part.BaseQuantity=line.BaseQuantity;}
                            if(line.IsMapped){if(mappedRemainder==0)break;part.BaseQuantity=line.SerialTracked?checked(line.PhysicalUnitMovements.Count(x=>x.Value==part.SaleMovementId)*1000000L):mappedRemainder;if(part.BaseQuantity==0)continue;}
                            var allocations=(await c.QueryAsync<ReturnCost>(Cmd(@"SELECT a.LayerId CostLayerId,a.QuantityScaled OriginalQuantity,a.ValueScaled OriginalValue,l.WarehouseId,l.VariantId,l.BatchId,l.OriginMovementId,l.AcquiredUtc,l.UnitCostScaled,
                                a.MovementId SourceMovementId,
                                COALESCE((SELECT SUM(ca.QuantityScaled) FROM SalesReturnCostAllocations ca JOIN SalesReturnLines rl ON rl.Id=ca.SalesReturnLineId WHERE ca.CostLayerId=a.LayerId AND rl.SalesInvoiceLineId=@LineId AND (ca.SourceMovementId=a.MovementId OR (ca.SourceMovementId IS NULL AND @Mapped=0))),0) ReturnedQuantity,
                                COALESCE((SELECT SUM(ca.ValueScaled) FROM SalesReturnCostAllocations ca JOIN SalesReturnLines rl ON rl.Id=ca.SalesReturnLineId WHERE ca.CostLayerId=a.LayerId AND rl.SalesInvoiceLineId=@LineId AND (ca.SourceMovementId=a.MovementId OR (ca.SourceMovementId IS NULL AND @Mapped=0))),0) ReturnedValue
                                FROM InventoryCostAllocations a JOIN InventoryCostLayers l ON l.Id=a.LayerId WHERE a.MovementId=@Movement ORDER BY a.Id ASC",new{LineId=line.Id,Movement=part.SaleMovementId,Mapped=line.IsMapped?1:0},tx,ct)).ConfigureAwait(false)).ToList();
                            var remainder=part.BaseQuantity;long value=0;
                            foreach(var allocation in allocations)
                            {
                                if(remainder==0)break;var available=allocation.OriginalQuantity-allocation.ReturnedQuantity;if(available<=0)continue;
                                var take=Math.Min(remainder,available);var allocatedValue=InventoryCosting.AllocateLayer(allocation.OriginalQuantity,allocation.OriginalValue,available,allocation.OriginalValue-allocation.ReturnedValue,take);
                                allocation.ReturnQuantity=take;allocation.ReturnValue=allocatedValue;value=checked(value+allocatedValue);remainder-=take;
                            }
                            var restore=checked(part.BaseQuantity-remainder);
                            if(line.IsMapped){mappedRemainder-=restore;if(restore==0)continue;part.RequestedQuantity=restore;}
                            var expiry=part.BatchId.HasValue?await c.ExecuteScalarAsync<string>(Cmd("SELECT ExpirationDate FROM ProductBatches WHERE Id=@Id",new{Id=part.BatchId.Value},tx,ct)).ConfigureAwait(false):null;
                            var sellable=!line.IsDamaged&&(expiry==null||string.CompareOrdinal(expiry,DateTime.Today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture))>=0);
                            lineNumber++;
                            await c.ExecuteAsync(Cmd(@"INSERT INTO InventoryDocumentLines(DocumentId,LineNumber,VariantId,BatchId,EnteredQuantityScaled,ProductUnitId,ToBaseNumerator,ToBaseDenominator,BaseQuantityScaled)
                                VALUES(@Doc,@Number,@Variant,@Batch,@Quantity,@Unit,@Numerator,@Denominator,@Base)",new{Doc=documentId,Number=lineNumber,Variant=part.VariantId,Batch=part.BatchId,Quantity=part.RequestedQuantity,Unit=part.ProductUnitId,Numerator=part.ToBaseNumerator,Denominator=part.ToBaseDenominator,Base=restore},tx,ct)).ConfigureAwait(false);
                            var documentLineId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                            var movement=restore>0?await SqliteInventoryStore.InsertMovementAsync(c,tx,documentId,documentLineId,invoice.WarehouseId,part.VariantId,part.BatchId,restore,value,null,now,ct).ConfigureAwait(false):0;
                            foreach(var allocation in allocations.Where(x=>x.ReturnQuantity>0))
                                await SqliteInventoryStore.AddLayerAsync(c,tx,invoice.WarehouseId,part.VariantId,part.BatchId,movement,allocation.CostLayerId,allocation.OriginMovementId,allocation.AcquiredUtc,"receipt",allocation.ReturnQuantity,allocation.ReturnValue,allocation.UnitCostScaled,ct,sellable).ConfigureAwait(false);
                            if(restore!=0)await SqliteInventoryStore.ApplyBalanceAsync(c,tx,invoice.WarehouseId,part.VariantId,part.BatchId,restore,value,now,ct).ConfigureAwait(false);
                            if(line.InventoryDocumentLineId.HasValue||line.IsMapped)foreach(var physicalId in line.PhysicalUnitIds.Where(x=>!line.IsMapped||line.PhysicalUnitMovements[x]==part.SaleMovementId))
                            {
                                await c.ExecuteAsync(Cmd("UPDATE InventoryPhysicalUnits SET WarehouseId=@Warehouse,UpdatedUtc=@Now WHERE Id=@Id AND WarehouseId IS NULL",new{Warehouse=invoice.WarehouseId,Now=now,Id=physicalId},tx,ct)).ConfigureAwait(false);
                                await c.ExecuteAsync(Cmd("INSERT INTO StockMovementUnits(MovementId,PhysicalUnitId) VALUES(@Movement,@Id)",new{Movement=movement,Id=physicalId},tx,ct)).ConfigureAwait(false);
                            }
                            lineValueTotal=checked(lineValueTotal+value);lineAllocations.AddRange(allocations.Where(x=>x.ReturnQuantity>0));
                        }
                        if(line.IsMapped&&mappedRemainder>0)
                        {
                            var stockSold=await c.ExecuteScalarAsync<long>(Cmd(@"SELECT COALESCE(SUM(-m.QuantityBaseScaled),0) FROM SalesInvoiceStockEntries s JOIN StockMovements m ON m.DocumentLineId=s.InventoryDocumentLineId AND m.QuantityBaseScaled<0 WHERE s.SalesInvoiceLineId=@Id",new{Id=line.Id},tx,ct)).ConfigureAwait(false);
                            var priorBase=checked(line.ReturnedQuantity*line.ToBaseNumerator/line.ToBaseDenominator);
                            if(stockSold>=checked(priorBase+line.BaseQuantity))return new SaleReturnResult{ErrorCode="legacy_allocation_incomplete",ErrorMessage="تفاصيل صرف الفاتورة لا تكفي لإرجاع هذه الكمية؛ لم يتغير المخزون."};
                        }
                        made.Add(new MadeLine{Line=line,MovementId=0,Allocations=lineAllocations});
                    }
                    await c.ExecuteAsync(Cmd("UPDATE InventoryDocuments SET Status='posted',PostedUtc=@Now WHERE Id=@Id",new{Now=now,Id=documentId},tx,ct)).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd("INSERT INTO InventoryAudit(DocumentId,ActorUserId,EventType,EventUtc,Details) VALUES(@Id,@User,'posted',@Now,@Details)",new{Id=documentId,User=userId,Now=now,Details="إرجاع فاتورة "+invoice.InvoiceNumber},tx,ct)).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd(@"INSERT INTO SalesReturns(ReturnNumber,SalesInvoiceId,InventoryDocumentId,OperationKey,RequestHash,TotalScaled,CashRefundScaled,CreditReductionScaled,Reason,CreatedByUserId,CreatedUtc)
                        VALUES(@Number,@Invoice,@Document,@Key,@Hash,@Total,@Cash,@Credit,@Reason,@User,@Now)",new{Number=returnNumber,Invoice=id,Document=documentId,Key=command.OperationKey,Hash=hash,Total=total,Cash=cash,Credit=credit,Reason=command.Reason,User=userId,Now=now},tx,ct)).ConfigureAwait(false);
                    var returnId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                    if(contractId.HasValue&&interestAdjustment>0)await c.ExecuteAsync(Cmd("INSERT INTO CustomerInstallmentInterestAdjustments(SalesReturnId,ContractId,AmountScaled,DebtReductionScaled,ScheduleReductionScaled,CashRefundScaled,SettlementMethod,CreatedUtc) VALUES(@Return,@Contract,@Amount,@Debt,@Schedule,@Cash,@Method,@Now)",new{Return=returnId,Contract=contractId.Value,Amount=interestAdjustment,Debt=interestDebtReduction,Schedule=interestScheduleReduction,Cash=interestCashRefund,Method=command.InterestSettlement,Now=now},tx,ct)).ConfigureAwait(false);
                    if(contractId.HasValue&&credit+interestScheduleReduction>0)await AllocateInstallmentReturnAsync(c,tx,contractId.Value,returnId,credit+interestScheduleReduction,ct).ConfigureAwait(false);
                    if(contractId.HasValue&&credit+interestScheduleReduction>0)await c.ExecuteAsync(Cmd(@"INSERT INTO InstallmentReceivableEntries(ContractId,SalesReturnId,EntryType,Direction,AmountScaled,OperationKey,RequestHash,CreatedUtc)
                        VALUES(@Contract,@Return,'return_reduction','credit',@Amount,@Key,@Hash,@Now)",new{Contract=contractId.Value,Return=returnId,Amount=credit+interestScheduleReduction,Key="installment-receivable:return:"+command.OperationKey,Hash=hash,Now=now},tx,ct)).ConfigureAwait(false);
                    if(electronic!=null&&cash>0)await c.ExecuteAsync(Cmd(@"INSERT INTO SalesElectronicRefunds(SalesReturnId,SalesInvoiceId,Method,InstrumentId,OriginalPaymentKey,AmountScaled,CreatedUtc)
                        VALUES(@Return,@Invoice,@Method,@Instrument,@Original,@Amount,@Now)",new{Return=returnId,Invoice=id,electronic.Method,Instrument=electronic.InstrumentId,Original=electronic.OriginalPaymentKey,Amount=cash,Now=now},tx,ct)).ConfigureAwait(false);
                    foreach(var item in made)
                    {
                        await c.ExecuteAsync(Cmd("INSERT INTO SalesReturnLines(SalesReturnId,SalesInvoiceLineId,QuantityScaled,AmountScaled) VALUES(@Return,@Line,@Quantity,@Amount)",new{Return=returnId,Line=item.Line.Id,Quantity=item.Line.RequestedQuantity,Amount=item.Line.Amount},tx,ct)).ConfigureAwait(false);
                        var returnLineId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                        foreach(var allocation in item.Allocations)await c.ExecuteAsync(Cmd("INSERT INTO SalesReturnCostAllocations(SalesReturnLineId,CostLayerId,QuantityScaled,ValueScaled,SourceMovementId) VALUES(@Line,@Layer,@Quantity,@Value,@Movement)",new{Line=returnLineId,Layer=allocation.CostLayerId,Quantity=allocation.ReturnQuantity,Value=allocation.ReturnValue,Movement=allocation.SourceMovementId},tx,ct)).ConfigureAwait(false);
                        await SaveReturnEstimatedCostAsync(c,tx,returnLineId,item.Line.Id,ct).ConfigureAwait(false);
                        foreach(var pair in item.Line.PhysicalUnitIds.Zip(item.Line.Serials,(physicalId,serial)=>new{physicalId,serial}))
                            await c.ExecuteAsync(Cmd("INSERT INTO SalesReturnPhysicalUnits(SalesReturnLineId,PhysicalUnitId,Serial) VALUES(@Line,@Id,@Serial)",new{Line=returnLineId,Id=pair.physicalId,Serial=pair.serial},tx,ct)).ConfigureAwait(false);
                    }
                    var date=DateTime.UtcNow.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                    if(cash>0&&electronic==null)await c.ExecuteAsync(Cmd(@"INSERT INTO SalesPayments(SalesInvoiceId,CustomerId,SalesReturnId,ActorUserId,EventType,PaymentMethod,AmountScaled,OperationKey,RequestHash,OccurredOn,Notes,CreatedUtc)
                        VALUES(@Invoice,@Customer,@Return,@User,'refund','cash',@Amount,@Key,@Hash,@Date,@Notes,@Now)",new{Invoice=id,Customer=invoice.CustomerId,Return=returnId,User=userId,Amount=cash,Key="refund:"+command.OperationKey,Hash=hash,Date=date,Notes="رد فاتورة "+invoice.InvoiceNumber,Now=now},tx,ct)).ConfigureAwait(false);
                    if(cash>0&&electronic==null)
                    {
                        var refundPayment=await c.ExecuteScalarAsync<long>(Cmd("SELECT Id FROM SalesPayments WHERE OperationKey=@Key",new{Key="refund:"+command.OperationKey},tx,ct)).ConfigureAwait(false);
                        await AlKhayal.Data.Workforce.SqliteWorkforceStore.AssociateAsync(c,tx,userId,"out",cash,id,refundPayment,ct).ConfigureAwait(false);
                    }
                    if(credit>0||interestDebtReduction>0)
                    {
                        if(credit>0)
                        {
                            await c.ExecuteAsync(Cmd(@"INSERT INTO SalesPayments(SalesInvoiceId,CustomerId,SalesReturnId,ActorUserId,EventType,PaymentMethod,AmountScaled,OperationKey,RequestHash,OccurredOn,Notes,CreatedUtc)
                                VALUES(@Invoice,@Customer,@Return,@User,'refund','credit_adjustment',@Amount,@Key,@Hash,@Date,@Notes,@Now)",new{Invoice=id,Customer=invoice.CustomerId,Return=returnId,User=userId,Amount=credit,Key="credit-refund:"+command.OperationKey,Hash=hash,Date=date,Notes="تخفيض مديونية بإرجاع "+invoice.InvoiceNumber,Now=now},tx,ct)).ConfigureAwait(false);
                            await c.ExecuteAsync(Cmd(@"INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc,Reference,SalesInvoiceId,SalesReturnId)
                                VALUES(@Customer,@Key,@Hash,'reduction',@Amount,@Date,@Notes,@Now,@Reference,@Invoice,@Return)",new{Customer=invoice.CustomerId,Key="sale-return:"+command.OperationKey,Hash=hash,Amount=credit,Date=date,Notes="إرجاع فاتورة "+invoice.InvoiceNumber,Now=now,Reference=returnNumber,Invoice=id,Return=returnId},tx,ct)).ConfigureAwait(false);
                        }
                        if(interestDebtReduction>0)
                            await c.ExecuteAsync(Cmd("INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc,Reference,SalesInvoiceId,SalesReturnId) VALUES(@Customer,@Key,@Hash,'reduction',@Amount,@Date,@Notes,@Now,@Reference,@Invoice,@Return)",new{Customer=invoice.CustomerId,Key="sale-return-interest:"+command.OperationKey,Hash=hash,Amount=interestDebtReduction,Date=date,Notes="تسوية فائدة مرتجع العقد "+returnNumber,Now=now,Reference=returnNumber,Invoice=id,Return=returnId},tx,ct)).ConfigureAwait(false);
                        // Balance is a legacy NUMERIC projection: SQLite subtraction uses REAL
                        // and can turn an exact final debt reduction into a negative residue.
                        // Calculate in the same scale-4 integers used by the settlement/ledger,
                        // then replace the projection inside this writer transaction.
                        await c.ExecuteAsync(Cmd("UPDATE Customers SET Balance=@Balance,UpdatedAt=@Now WHERE Id=@Id",new{Balance=balanceAfterCredit/10000m,Now=now,Id=invoice.CustomerId},tx,ct)).ConfigureAwait(false);
                    }
                    if(electronic==null)await c.ExecuteAsync(Cmd("UPDATE SalesInvoices SET PaidScaled=MAX(0,PaidScaled-@Amount) WHERE Id=@Id",new{Amount=cash,Id=id},tx,ct)).ConfigureAwait(false);
                    if(electronic==null)await AlKhayal.Data.Treasury.SqliteReturnTreasury.PostAsync(c,tx,true,returnId,cash+interestCashRefund,command.OperationKey,hash,userId,ct).ConfigureAwait(false);
                    else if(interestCashRefund>0)await AlKhayal.Data.Treasury.SqliteReturnTreasury.PostAsync(c,tx,true,returnId,interestCashRefund,"interest:"+command.OperationKey,hash,userId,ct).ConfigureAwait(false);
                    if(interestCashRefund>0)
                    {
                        await c.ExecuteAsync(Cmd("INSERT INTO SalesPayments(SalesInvoiceId,CustomerId,SalesReturnId,ActorUserId,EventType,PaymentMethod,AmountScaled,OperationKey,RequestHash,OccurredOn,Notes,CreatedUtc) VALUES(@Invoice,@Customer,@Return,@User,'refund','cash',@Amount,@Key,@Hash,@Date,@Notes,@Now)",new{Invoice=id,Customer=invoice.CustomerId,Return=returnId,User=userId,Amount=interestCashRefund,Key="interest-refund:"+command.OperationKey,Hash=hash,Date=date,Notes="رد فائدة عقد تقسيط "+returnNumber,Now=now},tx,ct)).ConfigureAwait(false);
                    }
                    tx.Commit();return new SaleReturnResult{Succeeded=true,ReturnId=returnId,ReturnNumber=returnNumber,InventoryDocumentId=documentId,TotalScaled=total,CashRefundScaled=cash,CreditReductionScaled=credit,InterestCashRefundScaled=interestCashRefund,InterestCreditScaled=Math.Max(0,interestDebtReduction-interestScheduleReduction)};
                }
            }
            catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Busy||e.ResultCode==SQLiteErrorCode.Locked){return new SaleReturnResult{ErrorCode="database_busy",ErrorMessage="قاعدة البيانات مشغولة. أعد المحاولة بالمفتاح نفسه."};}
            catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Constraint){return new SaleReturnResult{ErrorCode="sales_conflict",ErrorMessage="تعارض الإرجاع مع حركة أخرى؛ حدّث البيانات ثم أعد المحاولة."};}
            catch(AlKhayal.Application.Treasury.TreasuryException e){return new SaleReturnResult{ErrorCode="treasury_"+e.Code,ErrorMessage=e.Message};}
            catch(OverflowException){return new SaleReturnResult{ErrorCode="numeric_overflow",ErrorMessage="قيمة الإرجاع تتجاوز الحد المسموح."};}
        }

        private static string ReturnHash(long id,SaleReturnCommand command,long userId)
        {
            using(var sha=SHA256.Create()){var settlement=command.InterestSettlement=="cash"?"|interest-settlement:cash":"";var value=id+"|"+command.Reason+settlement+"|"+userId+"|"+string.Join(";",command.Lines.OrderBy(x=>x.InvoiceLineId).Select(x=>x.InvoiceLineId+":"+x.Quantity+(x.IsDamaged?":damaged":"")+":"+string.Join(",",(x.Serials??new List<string>()).OrderBy(s=>s,StringComparer.OrdinalIgnoreCase))));return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
        }
        private static async Task<List<InstallmentDueRow>> InstallmentRowsAsync(SQLiteConnection c,SQLiteTransaction tx,long contractId,CancellationToken ct)
        {
            return (await c.QueryAsync<InstallmentDueRow>(Cmd(@"SELECT s.InstallmentNumber,s.AmountScaled-COALESCE((SELECT SUM(a.AmountScaled) FROM CustomerInstallmentReturnAllocations a WHERE a.ContractId=s.ContractId AND a.InstallmentNumber=s.InstallmentNumber),0)
                -COALESCE((SELECT SUM(p.AmountScaled) FROM (SELECT ContractId,InstallmentNumber,AmountScaled FROM CustomerInstallmentPayments UNION ALL SELECT ContractId,InstallmentNumber,AmountScaled FROM CustomerInstallmentPartialPayments) p WHERE p.ContractId=s.ContractId AND p.InstallmentNumber=s.InstallmentNumber),0) DueScaled
                FROM CustomerInstallmentSchedule s WHERE s.ContractId=@Contract ORDER BY s.InstallmentNumber DESC",new{Contract=contractId},tx,ct)).ConfigureAwait(false)).ToList();
        }
        private static async Task<long> InstallmentDueAsync(SQLiteConnection c,SQLiteTransaction tx,long contractId,CancellationToken ct)
        {
            var rows=await InstallmentRowsAsync(c,tx,contractId,ct).ConfigureAwait(false);
            if(rows.Any(x=>x.DueScaled<0))throw new SQLiteException(SQLiteErrorCode.Constraint,"installment_schedule_inconsistent");
            return rows.Sum(x=>x.DueScaled);
        }
        private static async Task AllocateInstallmentReturnAsync(SQLiteConnection c,SQLiteTransaction tx,long contractId,long returnId,long amount,CancellationToken ct)
        {
            var rows=await InstallmentRowsAsync(c,tx,contractId,ct).ConfigureAwait(false);
            foreach(var row in rows)
            {
                var take=Math.Min(amount,row.DueScaled);if(take<=0)continue;
                await c.ExecuteAsync(Cmd("INSERT INTO CustomerInstallmentReturnAllocations(SalesReturnId,ContractId,InstallmentNumber,AmountScaled) VALUES(@Return,@Contract,@Number,@Amount)",new{Return=returnId,Contract=contractId,Number=row.InstallmentNumber,Amount=take},tx,ct)).ConfigureAwait(false);
                amount-=take;if(amount==0)break;
            }
            if(amount!=0)throw new SQLiteException(SQLiteErrorCode.Constraint,"installment_return_allocation_shortfall");
        }
        private sealed class InstallmentDueRow{public int InstallmentNumber{get;set;}public long DueScaled{get;set;}}
        private sealed class InstallmentInterestContract{public long ProductPriceScaled{get;set;}public long TotalAmountScaled{get;set;}public string InterestReturnPolicy{get;set;}}
        private sealed class ElectronicSelection{public string Method{get;set;}public long InstrumentId{get;set;}public string OriginalPaymentKey{get;set;}}
        private sealed class ElectronicRefundRow{public long SalesInvoiceId{get;set;}public string Method{get;set;}public long AmountScaled{get;set;}public string OperationKey{get;set;}public string ExternalReference{get;set;}public string RequestHash{get;set;}}
        public async Task<ElectronicRefundResult> ConfirmElectronicRefundAsync(long returnId,ElectronicRefundConfirmation command,long userId,CancellationToken ct)
        {
            var hash=returnId+"|"+command.ExternalReference+"|"+userId;
            try
            {
                using(var c=Open())using(var tx=c.BeginTransaction())
                {
                    var row=await c.QuerySingleOrDefaultAsync<ElectronicRefundRow>(Cmd(@"SELECT e.SalesInvoiceId,e.Method,e.AmountScaled,k.OperationKey,k.ExternalReference,k.RequestHash
                        FROM SalesElectronicRefunds e LEFT JOIN SalesElectronicRefundConfirmations k ON k.SalesReturnId=e.SalesReturnId WHERE e.SalesReturnId=@Id",new{Id=returnId},tx,ct)).ConfigureAwait(false);
                    if(row==null)return new ElectronicRefundResult{ErrorCode="not_found",ErrorMessage="طلب الرد الإلكتروني غير موجود."};
                    if(row.OperationKey!=null)
                    {
                        if(row.OperationKey==command.OperationKey&&row.RequestHash==hash){tx.Commit();return new ElectronicRefundResult{Succeeded=true,Replayed=true,ReturnId=returnId};}
                        return new ElectronicRefundResult{ErrorCode="refund_already_confirmed",ErrorMessage="تم تأكيد رد هذا المرتجع بالفعل."};
                    }
                    var now=Now();var date=DateTime.UtcNow.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                    await c.ExecuteAsync(Cmd("INSERT INTO SalesElectronicRefundConfirmations(SalesReturnId,ExternalReference,OperationKey,RequestHash,ActorUserId,ConfirmedUtc) VALUES(@Id,@Reference,@Key,@Hash,@User,@Now)",new{Id=returnId,Reference=command.ExternalReference,Key=command.OperationKey,Hash=hash,User=userId,Now=now},tx,ct)).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd(@"INSERT INTO SalesPayments(SalesInvoiceId,CustomerId,SalesReturnId,ActorUserId,EventType,PaymentMethod,AmountScaled,OperationKey,RequestHash,OccurredOn,Notes,CreatedUtc)
                        VALUES(@Invoice,(SELECT CustomerId FROM SalesInvoices WHERE Id=@Invoice),@Return,@User,'refund',@Method,@Amount,@Key,@Hash,@Date,@Notes,@Now)",new{Invoice=row.SalesInvoiceId,Return=returnId,User=userId,Method=row.Method=="wallet"?"transfer":"card",Amount=row.AmountScaled,Key="electronic-refund:"+command.OperationKey,Hash=hash,Date=date,Notes="رد إلكتروني مؤكد: "+command.ExternalReference,Now=now},tx,ct)).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd("UPDATE SalesInvoices SET PaidScaled=MAX(0,PaidScaled-@Amount) WHERE Id=@Id",new{Amount=row.AmountScaled,Id=row.SalesInvoiceId},tx,ct)).ConfigureAwait(false);
                    tx.Commit();return new ElectronicRefundResult{Succeeded=true,ReturnId=returnId};
                }
            }
            catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Constraint){return new ElectronicRefundResult{ErrorCode="refund_confirmation_conflict",ErrorMessage="مرجع الرد الإلكتروني أو مفتاح العملية مستخدم بالفعل."};}
            catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Busy||e.ResultCode==SQLiteErrorCode.Locked){return new ElectronicRefundResult{ErrorCode="database_busy",ErrorMessage="قاعدة البيانات مشغولة؛ أعد المحاولة بالمفتاح نفسه."};}
        }
        private sealed class ReturnInvoice{public long Id{get;set;}public long? CustomerId{get;set;}public long WarehouseId{get;set;}public string Status{get;set;}public string PaymentMode{get;set;}public string InvoiceNumber{get;set;}public long PaidScaled{get;set;}}
        private sealed class ReturnLine{public long Id{get;set;}public long VariantId{get;set;}public long ProductUnitId{get;set;}public long LineTotalScaled{get;set;}public long? InventoryDocumentLineId{get;set;}public long SoldQuantity{get;set;}public long ReturnedQuantity{get;set;}public long? BatchId{get;set;}public long ToBaseNumerator{get;set;}public long ToBaseDenominator{get;set;}public long RequestedQuantity{get;set;}public long BaseQuantity{get;set;}public long SaleMovementId{get;set;}public long Amount{get;set;}public bool IsMapped{get;set;}public bool IsDamaged{get;set;}public bool SerialTracked{get;set;}public List<ReturnStockPart> Components{get;set;}=new List<ReturnStockPart>();public List<long> PhysicalUnitIds{get;set;}=new List<long>();public Dictionary<long,long> PhysicalUnitMovements{get;set;}=new Dictionary<long,long>();public List<string> Serials{get;set;}=new List<string>();}
        private sealed class ReturnPhysical{public long Id{get;set;}public long SaleMovementId{get;set;}}
        private sealed class ReturnUnit{public long ToBaseNumerator{get;set;}public long ToBaseDenominator{get;set;}}
        private sealed class ReturnStockPart{public long DocumentLineId{get;set;}public long SoldComponentQuantity{get;set;}public long VariantId{get;set;}public long ProductUnitId{get;set;}public long? BatchId{get;set;}public long ToBaseNumerator{get;set;}public long ToBaseDenominator{get;set;}public long SaleMovementId{get;set;}public long RequestedQuantity{get;set;}public long BaseQuantity{get;set;}}
        private sealed class ReturnProduct{public long ProductId{get;set;}public bool BatchTracked{get;set;}public bool SerialTracked{get;set;}public bool ImeiTracked{get;set;}}
        private sealed class ReturnCost{public long CostLayerId{get;set;}public long SourceMovementId{get;set;}public long OriginalQuantity{get;set;}public long OriginalValue{get;set;}public long WarehouseId{get;set;}public long VariantId{get;set;}public long? BatchId{get;set;}public long? OriginMovementId{get;set;}public string AcquiredUtc{get;set;}public long? UnitCostScaled{get;set;}public long ReturnedQuantity{get;set;}public long ReturnedValue{get;set;}public long ReturnQuantity{get;set;}public long ReturnValue{get;set;}}
        private sealed class MadeLine{public ReturnLine Line{get;set;}public long MovementId{get;set;}public List<ReturnCost> Allocations{get;set;}}
        private sealed class ReturnResultRow{public long Id{get;set;}public string ReturnNumber{get;set;}public long InventoryDocumentId{get;set;}public long TotalScaled{get;set;}public long CashRefundScaled{get;set;}public long CreditReductionScaled{get;set;}public long InterestCashRefundScaled{get;set;}public long InterestCreditScaled{get;set;}public string RequestHash{get;set;}public SaleReturnResult ToResult(bool replay){return new SaleReturnResult{Succeeded=true,Replayed=replay,ReturnId=Id,ReturnNumber=ReturnNumber,InventoryDocumentId=InventoryDocumentId,TotalScaled=TotalScaled,CashRefundScaled=CashRefundScaled,CreditReductionScaled=CreditReductionScaled,InterestCashRefundScaled=InterestCashRefundScaled,InterestCreditScaled=InterestCreditScaled};}}
    }
}
