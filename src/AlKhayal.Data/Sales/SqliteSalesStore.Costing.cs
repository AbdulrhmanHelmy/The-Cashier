using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Inventory;
using Dapper;

namespace AlKhayal.Data.Sales
{
    public sealed partial class SqliteSalesStore
    {
        private sealed class CostDocumentPart
        {
            public long LineId { get; set; }
            public long VariantId { get; set; }
            public long DocumentLineId { get; set; }
            public long ExpectedBase { get; set; }
        }
        private sealed class IssuedCost
        {
            public long AllocationId { get; set; }
            public long MovementId { get; set; }
            public long QuantityScaled { get; set; }
            public long ValueScaled { get; set; }
            public string Basis { get; set; }
            public long? UnitCostScaled { get; set; }
        }
        private sealed class EstimatedBasis
        {
            public long? UnitCostScaled { get; set; }
            public long? MovementId { get; set; }
            public string Basis { get; set; }
        }
        private sealed class ReturnCostBasis
        {
            public long EstimatedQuantity { get; set; }
            public long EstimatedValue { get; set; }
            public long Unknown { get; set; }
            public long ActualQuantity { get; set; }
            public long SoldQuantity { get; set; }
            public long Numerator { get; set; }
            public long Denominator { get; set; }
            public bool Bundle { get; set; }
        }
        private sealed class PriorEstimatedReturn { public long Quantity { get; set; } public long Value { get; set; } }

        // Called inside the posting transaction, after FIFO allocation and before the
        // invoice becomes immutable. Physical balances are never changed for this estimate.
        private static async Task SaveCostSnapshotAsync(SQLiteConnection c,SQLiteTransaction tx,long invoiceId,long saleDocumentId,CancellationToken ct)
        {
            var parts=(await c.QueryAsync<CostDocumentPart>(Cmd(@"
                SELECT l.Id LineId,l.VariantId,l.InventoryDocumentLineId DocumentLineId,
                    l.QuantityScaled*u.ToBaseNumerator/u.ToBaseDenominator ExpectedBase
                FROM SalesInvoiceLines l JOIN ProductUnits u ON u.Id=l.ProductUnitId
                WHERE l.SalesInvoiceId=@Invoice AND l.InventoryDocumentLineId IS NOT NULL
                UNION ALL
                SELECT s.SalesInvoiceLineId,l.VariantId,s.InventoryDocumentLineId,s.QuantityBaseScaled
                FROM SalesInvoiceStockEntries s JOIN SalesInvoiceLines l ON l.Id=s.SalesInvoiceLineId WHERE l.SalesInvoiceId=@Invoice
                UNION ALL
                SELECT b.SalesInvoiceLineId,dl.VariantId,b.InventoryDocumentLineId,b.QuantityBaseScaled
                FROM SalesInvoiceBundleComponentLines b JOIN SalesInvoiceLines l ON l.Id=b.SalesInvoiceLineId
                JOIN InventoryDocumentLines dl ON dl.Id=b.InventoryDocumentLineId WHERE l.SalesInvoiceId=@Invoice",
                new{Invoice=invoiceId},tx,ct)).ConfigureAwait(false)).ToList();
            var expectedLines=await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM SalesInvoiceLines WHERE SalesInvoiceId=@Invoice",new{Invoice=invoiceId},tx,ct)).ConfigureAwait(false);
            if(parts.Select(x=>x.LineId).Distinct().LongCount()!=expectedLines)throw new InvalidOperationException("تعذر ربط تكلفة جميع بنود البيع بالمخزون.");
            var now=Now();
            foreach(var part in parts)
            {
                if(part.ExpectedBase<=0)throw new InvalidOperationException("كمية تكلفة البيع غير صحيحة.");
                var issued=(await c.QueryAsync<IssuedCost>(Cmd(@"SELECT a.Id AllocationId,m.Id MovementId,a.QuantityScaled,a.ValueScaled,cl.Basis,cl.UnitCostScaled
                    FROM StockMovements m JOIN InventoryCostAllocations a ON a.MovementId=m.Id
                    JOIN InventoryCostLayers cl ON cl.Id=a.LayerId
                    WHERE m.DocumentLineId=@DocumentLine AND m.QuantityBaseScaled<0 ORDER BY a.Id",
                    new{DocumentLine=part.DocumentLineId},tx,ct)).ConfigureAwait(false)).ToList();
                long covered=0;
                foreach(var allocation in issued)
                {
                    covered=checked(covered+allocation.QuantityScaled);
                    var valid=allocation.Basis!="unresolved"&&allocation.UnitCostScaled.HasValue;
                    await c.ExecuteAsync(Cmd(@"INSERT INTO SalesInvoiceCostParts(SalesInvoiceLineId,SourceDocumentLineId,SourceMovementId,SourceAllocationId,Basis,QuantityBaseScaled,ValueScaled,UnitCostScaled,CreatedUtc)
                        VALUES(@Line,@DocumentLine,@Movement,@Allocation,@Basis,@Quantity,@Value,@UnitCost,@Now)",new{Line=part.LineId,DocumentLine=part.DocumentLineId,Movement=allocation.MovementId,Allocation=allocation.AllocationId,Basis=valid?"actual":"unknown",Quantity=allocation.QuantityScaled,Value=valid?(long?)allocation.ValueScaled:null,UnitCost=valid?allocation.UnitCostScaled:null,Now=now},tx,ct)).ConfigureAwait(false);
                }
                if(covered>part.ExpectedBase)throw new InvalidOperationException("تكلفة المخزون المصروف تتجاوز كمية البيع.");
                var uncovered=part.ExpectedBase-covered;
                if(uncovered==0)continue;
                var basis=await c.QuerySingleOrDefaultAsync<EstimatedBasis>(Cmd(@"SELECT cl.UnitCostScaled,m.Id MovementId,'estimated_purchase' Basis
                    FROM InventoryCostLayers cl JOIN StockMovements m ON m.Id=cl.SourceMovementId
                    JOIN InventoryDocuments d ON d.Id=m.DocumentId
                    WHERE cl.VariantId=@Variant AND cl.UnitCostScaled IS NOT NULL AND cl.Basis='receipt'
                      AND d.Id<@SaleDocument AND d.ReversesDocumentId IS NULL
                      AND (EXISTS(SELECT 1 FROM PurchaseInvoiceLines pl WHERE pl.InventoryDocumentLineId=m.DocumentLineId)
                        OR (d.DocumentType='manual_receipt' AND NOT EXISTS(SELECT 1 FROM SalesReturns sr WHERE sr.InventoryDocumentId=d.Id)))
                    ORDER BY CASE WHEN cl.WarehouseId=@Warehouse THEN 0 ELSE 1 END,m.Id DESC LIMIT 1",
                    new{Variant=part.VariantId,SaleDocument=saleDocumentId,Warehouse=await c.ExecuteScalarAsync<long>(Cmd("SELECT WarehouseId FROM SalesInvoices WHERE Id=@Invoice",new{Invoice=invoiceId},tx,ct)).ConfigureAwait(false)},tx,ct)).ConfigureAwait(false);
                if(basis==null)
                    basis=await c.QuerySingleOrDefaultAsync<EstimatedBasis>(Cmd(@"SELECT COALESCE(v.PurchasePriceOverrideScaled,p.PurchasePriceScaled) UnitCostScaled,
                        'estimated_configured' Basis FROM ProductVariants v JOIN ProductDetails p ON p.ProductId=v.ProductId WHERE v.Id=@Variant",
                        new{Variant=part.VariantId},tx,ct)).ConfigureAwait(false);
                var validCost=basis!=null&&basis.UnitCostScaled.HasValue;
                var value=validCost?(long?)checked((long)decimal.Round((decimal)basis.UnitCostScaled.Value*uncovered/1000000m,0,MidpointRounding.AwayFromZero)):null;
                await c.ExecuteAsync(Cmd(@"INSERT INTO SalesInvoiceCostParts(SalesInvoiceLineId,SourceDocumentLineId,SourcePurchaseMovementId,Basis,QuantityBaseScaled,ValueScaled,UnitCostScaled,CreatedUtc)
                    VALUES(@Line,@DocumentLine,@Purchase,@Basis,@Quantity,@Value,@UnitCost,@Now)",new{Line=part.LineId,DocumentLine=part.DocumentLineId,Purchase=validCost?basis.MovementId:null,Basis=validCost?basis.Basis:"unknown",Quantity=uncovered,Value=value,UnitCost=validCost?basis.UnitCostScaled:null,Now=now},tx,ct)).ConfigureAwait(false);
            }
        }

        private static async Task SaveReturnEstimatedCostAsync(SQLiteConnection c,SQLiteTransaction tx,long returnLineId,long saleLineId,CancellationToken ct)
        {
            var basis=await c.QuerySingleOrDefaultAsync<ReturnCostBasis>(Cmd(@"SELECT
                COALESCE(SUM(CASE WHEN p.SourceAllocationId IS NULL THEN p.QuantityBaseScaled ELSE 0 END),0) EstimatedQuantity,
                COALESCE(SUM(CASE WHEN p.SourceAllocationId IS NULL THEN p.ValueScaled ELSE 0 END),0) EstimatedValue,
                COALESCE(SUM(CASE WHEN p.SourceAllocationId IS NULL AND p.ValueScaled IS NULL THEN 1 ELSE 0 END),0) Unknown,
                COALESCE(SUM(CASE WHEN p.SourceAllocationId IS NOT NULL THEN p.QuantityBaseScaled ELSE 0 END),0) ActualQuantity,
                l.QuantityScaled SoldQuantity,u.ToBaseNumerator Numerator,u.ToBaseDenominator Denominator,
                EXISTS(SELECT 1 FROM SalesInvoiceBundleComponentLines b WHERE b.SalesInvoiceLineId=l.Id) Bundle
                FROM SalesInvoiceLines l JOIN ProductUnits u ON u.Id=l.ProductUnitId
                LEFT JOIN SalesInvoiceCostParts p ON p.SalesInvoiceLineId=l.Id WHERE l.Id=@Line GROUP BY l.Id",
                new{Line=saleLineId},tx,ct)).ConfigureAwait(false);
            if(basis==null||basis.EstimatedQuantity==0)return;
            var returned=await c.ExecuteScalarAsync<long>(Cmd("SELECT COALESCE(SUM(QuantityScaled),0) FROM SalesReturnLines WHERE SalesInvoiceLineId=@Line",new{Line=saleLineId},tx,ct)).ConfigureAwait(false);
            var target=basis.Bundle
                ?checked((long)decimal.Floor((decimal)basis.EstimatedQuantity*returned/basis.SoldQuantity))
                :Math.Max(0,checked((long)((decimal)returned*basis.Numerator/basis.Denominator))-basis.ActualQuantity);
            target=Math.Min(target,basis.EstimatedQuantity);
            var previous=await c.QuerySingleAsync<PriorEstimatedReturn>(Cmd(@"SELECT COALESCE(SUM(ec.QuantityBaseScaled),0) Quantity,COALESCE(SUM(ec.ValueScaled),0) Value
                FROM SalesReturnEstimatedCosts ec JOIN SalesReturnLines rl ON rl.Id=ec.SalesReturnLineId
                WHERE rl.SalesInvoiceLineId=@Line",new{Line=saleLineId},tx,ct)).ConfigureAwait(false);
            var delta=target-previous.Quantity;
            if(delta<0)throw new InvalidOperationException("توزيع تكلفة المرتجع التقديرية غير متسق.");
            if(delta==0)return;
            var cumulativeValue=target==basis.EstimatedQuantity?basis.EstimatedValue:
                checked((long)decimal.Round((decimal)basis.EstimatedValue*target/basis.EstimatedQuantity,0,MidpointRounding.AwayFromZero));
            await c.ExecuteAsync(Cmd(@"INSERT INTO SalesReturnEstimatedCosts(SalesReturnLineId,QuantityBaseScaled,ValueScaled,CreatedUtc)
                VALUES(@Line,@Quantity,@Value,@Now)",new{Line=returnLineId,Quantity=delta,Value=basis.Unknown>0?(long?)null:checked(cumulativeValue-previous.Value),Now=Now()},tx,ct)).ConfigureAwait(false);
        }
    }
}
