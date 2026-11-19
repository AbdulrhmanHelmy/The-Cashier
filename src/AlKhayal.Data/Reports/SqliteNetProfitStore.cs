using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Expenses;
using AlKhayal.Application.Reports;
using Dapper;

namespace AlKhayal.Data.Reports
{
    public sealed partial class SqliteNetProfitStore : INetProfitStore
    {
        private readonly string _cs;
        public SqliteNetProfitStore(string connectionString) { _cs = connectionString; }
        private static string Money(decimal value) => (value / 10000m).ToString("0.0000", CultureInfo.InvariantCulture);
        private const string Classification = @"LEFT JOIN ExpenseProfitClassifications ec ON ec.VoucherId=v.Id
            AND ec.Revision=(SELECT MAX(Revision) FROM ExpenseProfitClassifications WHERE VoucherId=v.Id)";

        // Restrict before aggregating: one cost row per movement, then one row per original line.
        // Never join allocations to payments or return lines before aggregation.
        private const string Evidence = @"
        relevant AS (
            SELECT Id FROM SalesInvoices i WHERE Status='posted' AND PostedUtc>=@StartUtc AND PostedUtc<@EndUtc AND NOT EXISTS(SELECT 1 FROM SalesInvoiceCorrections correction WHERE correction.PreviousInvoiceId=i.Id)
            UNION SELECT SalesInvoiceId FROM SalesCancellations WHERE CreatedUtc>=@StartUtc AND CreatedUtc<@EndUtc
            UNION SELECT SalesInvoiceId FROM SalesReturns WHERE CreatedUtc>=@StartUtc AND CreatedUtc<@EndUtc
        ), issue_cost AS (
            SELECT m.Id, SUM(a.QuantityScaled) Quantity, SUM(a.ValueScaled) Value,
                SUM(CASE WHEN cl.Basis='unresolved' OR cl.UnitCostScaled IS NULL THEN 1 ELSE 0 END) Unknown
            FROM relevant r JOIN SalesInvoiceLines sl ON sl.SalesInvoiceId=r.Id
            JOIN StockMovements m ON m.DocumentLineId=sl.InventoryDocumentLineId AND m.QuantityBaseScaled<0
            JOIN InventoryCostAllocations a ON a.MovementId=m.Id JOIN InventoryCostLayers cl ON cl.Id=a.LayerId
            GROUP BY m.Id
        ), snapshot_cost AS (
            SELECT p.SalesInvoiceLineId,SUM(COALESCE(p.ValueScaled,0)) Cost,
                SUM(CASE WHEN p.ValueScaled IS NULL THEN 1 ELSE 0 END) Unknown,COUNT(*) Parts,
                SUM(CASE WHEN COALESCE((sl.InventoryDocumentLineId=p.SourceDocumentLineId
                    OR EXISTS(SELECT 1 FROM SalesInvoiceStockEntries se WHERE se.SalesInvoiceLineId=sl.Id AND se.InventoryDocumentLineId=p.SourceDocumentLineId)
                    OR EXISTS(SELECT 1 FROM SalesInvoiceBundleComponentLines bc WHERE bc.SalesInvoiceLineId=sl.Id AND bc.InventoryDocumentLineId=p.SourceDocumentLineId)),0)=0
                    OR p.SourceAllocationId IS NOT NULL AND (a.Id IS NULL OR a.QuantityScaled<>p.QuantityBaseScaled
                    OR a.ValueScaled<>p.ValueScaled OR a.MovementId<>p.SourceMovementId OR m.DocumentLineId<>p.SourceDocumentLineId)
                    THEN 1 ELSE 0 END) Broken,
                SUM(CASE WHEN p.SourceAllocationId IS NULL THEN p.QuantityBaseScaled ELSE 0 END) EstimatedQuantity,
                SUM(CASE WHEN p.SourceAllocationId IS NULL THEN COALESCE(p.ValueScaled,0) ELSE 0 END) EstimatedCost
            FROM relevant r JOIN SalesInvoiceLines sl ON sl.SalesInvoiceId=r.Id
            JOIN SalesInvoiceCostParts p ON p.SalesInvoiceLineId=sl.Id
            LEFT JOIN InventoryCostAllocations a ON a.Id=p.SourceAllocationId
            LEFT JOIN StockMovements m ON m.Id=p.SourceMovementId GROUP BY p.SalesInvoiceLineId
        ), line_facts AS (
            SELECT l.*, CASE WHEN MAX(s.Broken)>0 THEN NULL ELSE COALESCE(dl.ToBaseNumerator,pu.ToBaseNumerator) END ToBaseNumerator,
                CASE WHEN MAX(s.Broken)>0 THEN NULL ELSE COALESCE(dl.ToBaseDenominator,pu.ToBaseDenominator) END ToBaseDenominator,
                CASE WHEN MAX(s.Parts)>0 THEN MAX(s.Cost) ELSE COALESCE(SUM(ic.Value),0) END Cost,
                CASE WHEN MAX(s.Parts)>0 THEN MAX(s.EstimatedCost) ELSE 0 END EstimatedCost,
                CASE WHEN MAX(s.Parts)>0 THEN MAX(s.EstimatedQuantity) ELSE 0 END OriginalEstimatedQuantity,
                CASE WHEN MAX(s.Parts)>0 THEN COALESCE((SELECT SUM(cp.QuantityBaseScaled) FROM SalesInvoiceCostParts cp
                    WHERE cp.SalesInvoiceLineId=l.Id AND cp.Basis='actual'),0)
                    ELSE -COALESCE(MIN(dl.BaseQuantityScaled),0) END ConsumedBase,
                CASE WHEN MAX(s.Parts)>0 THEN CASE WHEN MAX(s.Unknown)=0 AND MAX(s.Broken)=0 THEN 1 ELSE 0 END
                    WHEN COUNT(m.Id)=1 AND MIN(dl.DocumentId)=i.InventoryDocumentId
                    AND MIN(dl.VariantId)=l.VariantId AND MIN(dl.ProductUnitId)=l.ProductUnitId
                    AND MIN(dl.EnteredQuantityScaled)=l.QuantityScaled
                    AND -MIN(dl.BaseQuantityScaled)=l.QuantityScaled*MIN(dl.ToBaseNumerator)/MIN(dl.ToBaseDenominator)
                    AND MIN(m.QuantityBaseScaled)=MIN(dl.BaseQuantityScaled)
                    AND COALESCE(MIN(ic.Quantity),0)=-MIN(dl.BaseQuantityScaled)
                    AND COALESCE(MIN(ic.Value),0)=MIN(-m.ValueScaled) AND COALESCE(MAX(ic.Unknown),0)=0 THEN 1 ELSE 0 END CostComplete,
                CASE WHEN MAX(s.Parts)>0 THEN 1 ELSE 0 END HasSnapshot,
                EXISTS(SELECT 1 FROM SalesInvoiceBundleComponentLines b WHERE b.SalesInvoiceLineId=l.Id) IsBundle
            FROM relevant r JOIN SalesInvoices i ON i.Id=r.Id JOIN SalesInvoiceLines l ON l.SalesInvoiceId=i.Id
            LEFT JOIN InventoryDocumentLines dl ON dl.Id=l.InventoryDocumentLineId
            JOIN ProductUnits pu ON pu.Id=l.ProductUnitId
            LEFT JOIN snapshot_cost s ON s.SalesInvoiceLineId=l.Id
            LEFT JOIN StockMovements m ON m.DocumentLineId=dl.Id AND m.QuantityBaseScaled<=0
            LEFT JOIN issue_cost ic ON ic.Id=m.Id GROUP BY l.Id
        ), invoice_facts AS (
            SELECT i.Id,i.PostedUtc,i.TotalScaled,i.TaxScaled,i.InventoryDocumentId,
                COALESCE(SUM(l.Cost),0) Cost,COALESCE(SUM(l.EstimatedCost),0) EstimatedCost,
                CASE WHEN COUNT(l.Id)>0 AND MIN(l.CostComplete)=1 THEN 1 ELSE 0 END CostComplete,
                CASE WHEN COUNT(l.Id)>0 AND SUM(l.LineTotalScaled)=i.TotalScaled AND SUM(l.TaxScaled)=i.TaxScaled
                    AND SUM(l.InvoiceDiscountScaled)=i.DiscountScaled
                    AND SUM(l.LineTotalScaled+l.InvoiceDiscountScaled-CASE WHEN i.TaxIncluded=0 THEN l.TaxScaled ELSE 0 END)=i.SubtotalScaled
                    AND MIN(l.LineTotalScaled-l.TaxScaled)>=0 THEN 1 ELSE 0 END RevenueComplete
            FROM relevant r JOIN SalesInvoices i ON i.Id=r.Id LEFT JOIN line_facts l ON l.SalesInvoiceId=i.Id
            WHERE i.Status='posted' GROUP BY i.Id
        )";

        public async Task<NetProfitResult> QueryAsync(NetProfitQuery q, CancellationToken ct)
        {
            var args = new DynamicParameters(new { q.StartUtc, q.EndUtc, Limit=q.PageSize, Offset=(q.Page-1)*q.PageSize });
            var values = new List<string>();
            for (int n=0;n<q.Periods.Count;n++)
            {
                args.Add("S"+n,q.Periods[n].StartUtc); args.Add("E"+n,q.Periods[n].EndUtc);
                values.Add("("+n+",@S"+n+",@E"+n+")");
            }
            string buckets = "buckets(Bucket,StartUtc,EndUtc) AS (VALUES "+string.Join(",",values)+")";
            using (var c = new SQLiteConnection(_cs))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                // One consistent read snapshot, no financial or temporary-table writes.
                using (var tx = c.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                {
                    var rows = (await c.QueryAsync<Aggregate>(new CommandDefinition("WITH "+buckets+","+Evidence+@",
                        events AS (
                            SELECT PostedUtc EventUtc,TotalScaled Gross,TotalScaled-TaxScaled Revenue,Cost,EstimatedCost,RevenueComplete,CostComplete FROM invoice_facts
                            UNION ALL
                            SELECT sc.CreatedUtc,-f.TotalScaled,-(f.TotalScaled-f.TaxScaled),-f.Cost,-f.EstimatedCost,
                                CASE WHEN NOT EXISTS(SELECT 1 FROM SalesReturns WHERE SalesInvoiceId=f.Id) THEN f.RevenueComplete ELSE 0 END,
                                CASE WHEN NOT EXISTS(SELECT 1 FROM SalesReturns WHERE SalesInvoiceId=f.Id)
                                    AND (SELECT COALESCE(SUM(ValueScaled),0) FROM StockMovements WHERE DocumentId=sc.InventoryDocumentId)
                                        =COALESCE((SELECT SUM(-ValueScaled) FROM StockMovements WHERE DocumentId=f.InventoryDocumentId),0)
                                    THEN f.CostComplete ELSE 0 END
                            FROM invoice_facts f JOIN SalesCancellations sc ON sc.SalesInvoiceId=f.Id
                            UNION ALL
                            SELECT r.CreatedUtc,-r.TotalScaled,0,0,0,0,0 FROM SalesReturns r
                            WHERE r.CreatedUtc>=@StartUtc AND r.CreatedUtc<@EndUtc
                                AND NOT EXISTS(SELECT 1 FROM SalesReturnLines rl WHERE rl.SalesReturnId=r.Id)
                        )
                        SELECT b.Bucket,COUNT(e.EventUtc) EventCount,
                            COALESCE(SUM(CASE WHEN e.RevenueComplete=1 THEN e.Revenue ELSE 0 END),0) Revenue,
                            COALESCE(SUM(CASE WHEN e.CostComplete=1 THEN e.Cost ELSE 0 END),0) Cost,
                            COALESCE(SUM(CASE WHEN e.CostComplete=1 THEN e.EstimatedCost ELSE 0 END),0) EstimatedCost,
                            COALESCE(SUM(CASE WHEN e.RevenueComplete=0 THEN 1 ELSE 0 END),0) MissingRevenue,
                            COALESCE(SUM(CASE WHEN e.CostComplete=0 THEN 1 ELSE 0 END),0) MissingCost,
                            COALESCE(SUM(CASE WHEN e.RevenueComplete=0 OR e.CostComplete=0 THEN ABS(e.Gross) ELSE 0 END),0) IncompleteGross
                        FROM buckets b LEFT JOIN events e ON e.EventUtc>=b.StartUtc AND e.EventUtc<b.EndUtc
                        GROUP BY b.Bucket ORDER BY b.Bucket",args,tx,cancellationToken:ct)).ConfigureAwait(false)).ToArray();

                    var expenses = await c.QueryAsync<Aggregate>(new CommandDefinition("WITH "+buckets+@"
                        SELECT b.Bucket,COUNT(v.Id) EventCount,
                            COALESCE(SUM(CASE WHEN ec.Classification='included' THEN v.AmountScaled ELSE 0 END),0) Expenses,
                            COALESCE(SUM(CASE WHEN ec.Classification='excluded' THEN v.AmountScaled ELSE 0 END),0) Excluded,
                            COALESCE(SUM(CASE WHEN COALESCE(ec.Classification,'unclassified')='unclassified' THEN v.AmountScaled ELSE 0 END),0) Unclassified,
                            COALESCE(SUM(CASE WHEN v.Id IS NOT NULL AND COALESCE(ec.Classification,'unclassified')='unclassified' THEN 1 ELSE 0 END),0) UnclassifiedCount
                        FROM buckets b LEFT JOIN ExpenseVouchers v ON v.CreatedUtc>=b.StartUtc AND v.CreatedUtc<b.EndUtc "+Classification+@"
                        GROUP BY b.Bucket ORDER BY b.Bucket",args,tx,cancellationToken:ct)).ConfigureAwait(false);
                    foreach (var e in expenses) rows[e.Bucket].Add(e);
                    await AddReturns(c,tx,q,args,rows,ct).ConfigureAwait(false);
                    var count = await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM ExpenseVouchers WHERE CreatedUtc>=@StartUtc AND CreatedUtc<@EndUtc",args,tx,cancellationToken:ct)).ConfigureAwait(false);
                    var vouchers = (await c.QueryAsync<ExpenseRow>(new CommandDefinition(@"SELECT v.Id,v.CategoryName Category,v.Payee,v.ExpenseDate,v.CreatedUtc PostedUtc,v.AmountScaled,
                        COALESCE(ec.Classification,'unclassified') Classification,COALESCE(ec.Revision,0) Revision,ec.Reason,u.DisplayName ClassifiedBy,ec.CreatedUtc ClassifiedUtc
                        FROM ExpenseVouchers v "+Classification+@" LEFT JOIN Users u ON u.Id=ec.ActorUserId
                        WHERE v.CreatedUtc>=@StartUtc AND v.CreatedUtc<@EndUtc ORDER BY v.CreatedUtc DESC,v.Id DESC LIMIT @Limit OFFSET @Offset",args,tx,cancellationToken:ct)).ConfigureAwait(false)).Select(x=>new ClassifiedExpenseRow {
                            Id=x.Id,Category=x.Category,Payee=x.Payee,ExpenseDate=x.ExpenseDate,PostedUtc=x.PostedUtc,Amount=Money(x.AmountScaled),
                            Classification=x.Classification,Revision=x.Revision,Reason=x.Reason,ClassifiedBy=x.ClassifiedBy,ClassifiedUtc=x.ClassifiedUtc }).ToList();
                    tx.Commit();
                    var summary = new Aggregate(); foreach(var row in rows) summary.Add(row);
                    return new NetProfitResult { Summary=Figures(summary,null),Items=rows.Select((x,n)=>Figures(x,q.Periods[n].Period)).ToArray(),ExpenseCount=count,ExpenseItems=vouchers };
                }
            }
        }

        private static async Task AddReturns(SQLiteConnection c,SQLiteTransaction tx,NetProfitQuery q,DynamicParameters args,Aggregate[] buckets,CancellationToken ct,Action<ReturnRow,bool,long,bool> visitor=null,string productFilter="")
        {
            // History is restricted to lines with an event in the selected period. Stream one
            // original line at a time; previous partials affect allocation but not current totals.
            var sql="WITH "+Evidence+@", affected AS (
                    SELECT DISTINCT rl.SalesInvoiceLineId FROM SalesReturns r JOIN SalesReturnLines rl ON rl.SalesReturnId=r.Id
                    WHERE r.CreatedUtc>=@StartUtc AND r.CreatedUtc<@EndUtc "+productFilter+@"
                ), restored AS (
                    SELECT ca.SalesReturnLineId,SUM(ca.QuantityScaled) Quantity,SUM(ca.ValueScaled) Value,
                        SUM(CASE WHEN cl.Basis='unresolved' OR cl.UnitCostScaled IS NULL OR NOT EXISTS (
                            SELECT 1 FROM SalesInvoiceLines original JOIN StockMovements issue ON issue.DocumentLineId=original.InventoryDocumentLineId
                            JOIN InventoryCostAllocations issued ON issued.MovementId=issue.Id
                            WHERE original.Id=rl.SalesInvoiceLineId AND issued.LayerId=ca.CostLayerId)
                            AND NOT EXISTS(SELECT 1 FROM SalesInvoiceCostParts cp JOIN InventoryCostAllocations issued ON issued.Id=cp.SourceAllocationId
                                WHERE cp.SalesInvoiceLineId=rl.SalesInvoiceLineId AND issued.LayerId=ca.CostLayerId)
                            THEN 1 ELSE 0 END) Unknown
                    FROM affected af JOIN SalesReturnLines rl ON rl.SalesInvoiceLineId=af.SalesInvoiceLineId
                    JOIN SalesReturnCostAllocations ca ON ca.SalesReturnLineId=rl.Id JOIN InventoryCostLayers cl ON cl.Id=ca.CostLayerId
                    GROUP BY ca.SalesReturnLineId
                )
                SELECT rl.Id,l.VariantId,rl.SalesInvoiceLineId LineId,r.CreatedUtc EventUtc,rl.QuantityScaled Quantity,rl.AmountScaled Gross,
                    l.QuantityScaled SoldQuantity,l.LineTotalScaled OriginalGross,l.TaxScaled OriginalTax,l.DiscountScaled LineDiscount,l.InvoiceDiscountScaled InvoiceDiscount,
                    l.ToBaseNumerator,l.ToBaseDenominator,l.Cost OriginalCost,l.EstimatedCost OriginalEstimatedCost,l.OriginalEstimatedQuantity,l.ConsumedBase,l.CostComplete,l.HasSnapshot,l.IsBundle,f.RevenueComplete,
                    ca.Quantity RestoredQuantity,ca.Value RestoredCost,COALESCE(ca.Unknown,0) UnknownCost,
                    ec.QuantityBaseScaled EstimatedQuantity,ec.ValueScaled EstimatedCost,
                    CASE WHEN (SELECT SUM(AmountScaled) FROM SalesReturnLines WHERE SalesReturnId=r.Id)=r.TotalScaled THEN 1 ELSE 0 END HeaderComplete
                FROM affected af JOIN SalesReturnLines rl ON rl.SalesInvoiceLineId=af.SalesInvoiceLineId
                JOIN SalesReturns r ON r.Id=rl.SalesReturnId
                LEFT JOIN line_facts l ON l.Id=rl.SalesInvoiceLineId LEFT JOIN invoice_facts f ON f.Id=l.SalesInvoiceId
                LEFT JOIN restored ca ON ca.SalesReturnLineId=rl.Id
                LEFT JOIN SalesReturnEstimatedCosts ec ON ec.SalesReturnLineId=rl.Id
                WHERE r.CreatedUtc<@EndUtc ORDER BY rl.SalesInvoiceLineId,r.CreatedUtc,r.Id,rl.Id";
            using (var reader = await c.ExecuteReaderAsync(new CommandDefinition(sql,args,tx,cancellationToken:ct)).ConfigureAwait(false))
            {
                var parse=reader.GetRowParser<ReturnRow>(); var history=new List<ReturnRow>();
                while(reader.Read())
                {
                    ct.ThrowIfCancellationRequested(); var row=parse(reader);
                    if(history.Count>0 && row.LineId!=history[0].LineId) { Allocate(history,q,buckets,visitor); history.Clear(); }
                    history.Add(row);
                }
                if(history.Count>0) Allocate(history,q,buckets,visitor);
            }
        }
        private static void Allocate(List<ReturnRow> history,NetProfitQuery q,Aggregate[] buckets,Action<ReturnRow,bool,long,bool> visitor=null)
        {
            var original=history[0]; IReadOnlyList<ReturnAllocationPart> parts;
            bool revenue=original.RevenueComplete==1 && history.All(x=>x.HeaderComplete==1);
            bool allocated=ReturnTaxAllocation.TryAllocate(original.SoldQuantity,original.OriginalGross,original.OriginalTax,original.LineDiscount,original.InvoiceDiscount,
                history.Select(x=>new ReturnAllocationEvent {QuantityScaled=x.Quantity,GrossScaled=x.Gross}).ToArray(),out parts);
            decimal restoredCost=0,restoredQuantity=0,returnedBase=0,estimatedQuantity=0,estimatedCost=0; bool cost=original.CostComplete==1;
            for(int n=0;n<history.Count;n++)
            {
                var r=history[n]; restoredCost+=(r.RestoredCost??0)+(r.EstimatedCost??0); restoredQuantity+=r.RestoredQuantity??0;
                estimatedQuantity+=r.EstimatedQuantity??0;estimatedCost+=r.EstimatedCost??0;
                returnedBase+=(decimal)r.Quantity*r.ToBaseNumerator/r.ToBaseDenominator;
                // A floor-zero sale consumed at most the sold quantity, so a consistent return
                // history restores exactly the smaller of cumulative returns and consumed stock;
                // the unconsumed remainder is refunded financially without invented cost.
                cost=cost && r.UnknownCost==0 && r.ToBaseDenominator>0
                    && (r.EstimatedQuantity==null || r.EstimatedCost.HasValue);
                if(r.HasSnapshot==1)
                {
                    var expectedActual=r.IsBundle==1
                        ?decimal.Floor((decimal)original.ConsumedBase*(history.Take(n+1).Sum(x=>(decimal)x.Quantity))/r.SoldQuantity)
                        :Math.Min(returnedBase,(decimal)original.ConsumedBase);
                    var expectedEstimate=r.IsBundle==1
                        ?decimal.Floor((decimal)original.OriginalEstimatedQuantity*(history.Take(n+1).Sum(x=>(decimal)x.Quantity))/r.SoldQuantity)
                        :Math.Max(0,returnedBase-original.ConsumedBase);
                    expectedEstimate=Math.Min(expectedEstimate,original.OriginalEstimatedQuantity);
                    var expectedEstimateCost=original.OriginalEstimatedQuantity==0?0:
                        expectedEstimate==original.OriginalEstimatedQuantity?original.OriginalEstimatedCost:
                        decimal.Round((decimal)original.OriginalEstimatedCost*expectedEstimate/original.OriginalEstimatedQuantity,0,MidpointRounding.AwayFromZero);
                    cost=cost && restoredCost<=r.OriginalCost && restoredQuantity==expectedActual
                        && estimatedQuantity==expectedEstimate && estimatedCost==expectedEstimateCost;
                }
                else cost=cost
                    && (r.RestoredQuantity??0)<=(decimal)r.Quantity*r.ToBaseNumerator/r.ToBaseDenominator
                    && restoredCost<=r.OriginalCost && restoredQuantity<=original.ConsumedBase
                    && restoredQuantity==Math.Min(returnedBase,(decimal)original.ConsumedBase);
                if(returnedBase>=(decimal)r.SoldQuantity*r.ToBaseNumerator/r.ToBaseDenominator
                    && restoredCost!=r.OriginalCost) cost=false;
                int index=FindBucket(q.Periods,r.EventUtc); if(index<0) continue;
                if(visitor!=null) { visitor(r,revenue && allocated,revenue && allocated?parts[n].RevenueScaled:0,cost); continue; }
                var b=buckets[index]; b.EventCount++;
                if(revenue && allocated) b.Revenue-=parts[n].RevenueScaled; else b.MissingRevenue++;
                if(cost) { b.Cost-=(r.RestoredCost??0)+(r.EstimatedCost??0); b.EstimatedCost-=r.EstimatedCost??0; } else b.MissingCost++;
                if(!revenue || !allocated || !cost) b.IncompleteGross+=r.Gross;
            }
        }
        private static int FindBucket(IReadOnlyList<ProfitPeriod> periods,string utc)
        {
            int lo=0,hi=periods.Count-1;
            while(lo<=hi) { int mid=(lo+hi)/2; if(string.CompareOrdinal(utc,periods[mid].StartUtc)<0) hi=mid-1;
                else if(string.CompareOrdinal(utc,periods[mid].EndUtc)>=0) lo=mid+1; else return mid; }
            return -1;
        }
        private static ProfitFigures Figures(Aggregate x,string period)
        {
            bool gross=x.MissingRevenue==0 && x.MissingCost==0, complete=gross && x.UnclassifiedCount==0;
            decimal net=x.Revenue-x.Cost-x.Expenses;
            return new ProfitFigures { Period=period,Revenue=x.MissingRevenue==0?Money(x.Revenue):null,Cogs=x.MissingCost==0?Money(x.Cost):null,EstimatedCogs=x.MissingCost==0?Money(x.EstimatedCost):null,
                GrossProfit=gross?Money(x.Revenue-x.Cost):null,Expenses=Money(x.Expenses),NetProfit=complete?Money(net):null,
                NetMargin=complete && x.Revenue>0?(net/x.Revenue*100m).ToString("0.00",CultureInfo.InvariantCulture):null,
                Complete=complete,UnclassifiedAmount=Money(x.Unclassified),UnclassifiedCount=x.UnclassifiedCount,ExcludedExpenses=Money(x.Excluded),
                MissingRevenueCount=x.MissingRevenue,MissingCostCount=x.MissingCost,IncompleteGrossAmount=Money(x.IncompleteGross),EventCount=x.EventCount };
        }
        public async Task ClassifyAsync(long voucherId,ExpenseClassificationCommand cmd,long actorId,CancellationToken ct)
        {
            using(var c=new SQLiteConnection(_cs))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                using(var tx=c.BeginTransaction())
                {
                    var prior=await c.QuerySingleOrDefaultAsync<ClassificationRow>(new CommandDefinition("SELECT VoucherId,Revision,Classification,Reason,ActorUserId FROM ExpenseProfitClassifications WHERE OperationKey=@OperationKey",cmd,tx,cancellationToken:ct)).ConfigureAwait(false);
                    if(prior!=null)
                    {
                        if(prior.VoucherId!=voucherId || prior.Revision!=cmd.ExpectedRevision+1 || prior.Classification!=cmd.Classification || prior.Reason!=cmd.Reason || prior.ActorUserId!=actorId)
                            throw new ExpenseException("operation_key_conflict","مفتاح الطلب مستخدم لتصنيف مختلف.");
                        tx.Commit(); return;
                    }
                    if(await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM ExpenseVouchers WHERE Id=@Id",new{Id=voucherId},tx,cancellationToken:ct)).ConfigureAwait(false)==0)
                        throw new ExpenseException("not_found","سند المصروف غير موجود.");
                    var revision=await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COALESCE(MAX(Revision),0) FROM ExpenseProfitClassifications WHERE VoucherId=@Id",new{Id=voucherId},tx,cancellationToken:ct)).ConfigureAwait(false);
                    if(revision!=cmd.ExpectedRevision) throw new ExpenseException("classification_conflict","تغير التصنيف؛ حدّث التقرير ثم حاول مجددًا.");
                    await c.ExecuteAsync(new CommandDefinition(@"INSERT INTO ExpenseProfitClassifications(VoucherId,Revision,Classification,Reason,ActorUserId,CreatedUtc,OperationKey)
                        VALUES(@Id,@Revision,@Classification,@Reason,@Actor,@Now,@OperationKey)",new{Id=voucherId,Revision=checked(revision+1),cmd.Classification,cmd.Reason,Actor=actorId,Now=DateTime.UtcNow.ToString("o"),cmd.OperationKey},tx,cancellationToken:ct)).ConfigureAwait(false);
                    tx.Commit();
                }
            }
        }
        private sealed class Aggregate
        {
            public int Bucket{get;set;} public decimal Revenue{get;set;} public decimal Cost{get;set;} public decimal EstimatedCost{get;set;} public decimal Expenses{get;set;}
            public decimal Excluded{get;set;} public decimal Unclassified{get;set;} public decimal IncompleteGross{get;set;}
            public long UnclassifiedCount{get;set;} public long MissingRevenue{get;set;} public long MissingCost{get;set;} public long EventCount{get;set;}
            public void Add(Aggregate x) { Revenue+=x.Revenue;Cost+=x.Cost;EstimatedCost+=x.EstimatedCost;Expenses+=x.Expenses;Excluded+=x.Excluded;Unclassified+=x.Unclassified;
                IncompleteGross+=x.IncompleteGross;UnclassifiedCount+=x.UnclassifiedCount;MissingRevenue+=x.MissingRevenue;MissingCost+=x.MissingCost;EventCount+=x.EventCount; }
        }
        private sealed class ReturnRow
        {
            public long VariantId{get;set;} public long Id{get;set;} public long LineId{get;set;} public string EventUtc{get;set;} public long Quantity{get;set;} public long Gross{get;set;}
            public long SoldQuantity{get;set;} public long? OriginalGross{get;set;} public long? OriginalTax{get;set;} public long? LineDiscount{get;set;} public long? InvoiceDiscount{get;set;}
            public long ToBaseNumerator{get;set;} public long ToBaseDenominator{get;set;} public long OriginalCost{get;set;} public long OriginalEstimatedCost{get;set;} public long OriginalEstimatedQuantity{get;set;} public long ConsumedBase{get;set;} public int CostComplete{get;set;} public int RevenueComplete{get;set;}
            public long? RestoredQuantity{get;set;} public long? RestoredCost{get;set;} public long? EstimatedQuantity{get;set;} public long? EstimatedCost{get;set;} public long UnknownCost{get;set;} public int HeaderComplete{get;set;} public int HasSnapshot{get;set;} public int IsBundle{get;set;}
        }
        private sealed class ExpenseRow
        {
            public long Id{get;set;} public string Category{get;set;} public string Payee{get;set;} public string ExpenseDate{get;set;} public string PostedUtc{get;set;}
            public long AmountScaled{get;set;} public string Classification{get;set;} public long Revision{get;set;} public string Reason{get;set;} public string ClassifiedBy{get;set;} public string ClassifiedUtc{get;set;}
        }
        private sealed class ClassificationRow { public long VoucherId{get;set;} public long Revision{get;set;} public string Classification{get;set;} public string Reason{get;set;} public long ActorUserId{get;set;} }
    }
}
