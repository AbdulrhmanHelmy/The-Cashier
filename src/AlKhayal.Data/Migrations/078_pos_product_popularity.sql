-- Durable derived projection for Touch POS product popularity.
-- Net actual base-unit sold quantity per product, all-time, excluding canceled invoices.
-- Maintained by triggers so read paths never scan the sales ledger.
CREATE TABLE IF NOT EXISTS ProductPopularity (
    ProductId INTEGER PRIMARY KEY REFERENCES Products(Id) ON DELETE RESTRICT,
    NetQuantityBaseScaled INTEGER NOT NULL DEFAULT 0 CHECK(NetQuantityBaseScaled >= 0)
);

-- One-time backfill from historical posted sales, subtracting returns.
-- Canceled invoices are already excluded from the sold set (SalesCancellations tombstone).
-- Uses the original inventory line conversion (QuantityScaled * ToBaseNumerator / ToBaseDenominator).
INSERT INTO ProductPopularity (ProductId, NetQuantityBaseScaled)
SELECT
    p.Id,
    COALESCE(sold.NetBase,0) - COALESCE(returned.ReturnBase,0)
FROM Products p
LEFT JOIN (
    SELECT pv.ProductId, SUM(l.QuantityScaled * idl.ToBaseNumerator / idl.ToBaseDenominator) AS NetBase
    FROM SalesInvoices si
    JOIN SalesInvoiceLines l ON l.SalesInvoiceId = si.Id
    JOIN InventoryDocumentLines idl ON idl.Id = l.InventoryDocumentLineId
    JOIN ProductVariants pv ON pv.Id = l.VariantId
    WHERE si.Status = 'posted'
      AND NOT EXISTS (SELECT 1 FROM SalesCancellations sc WHERE sc.SalesInvoiceId = si.Id)
    GROUP BY pv.ProductId
) sold ON sold.ProductId = p.Id
LEFT JOIN (
    SELECT pv.ProductId, SUM(rl.QuantityScaled * idl.ToBaseNumerator / idl.ToBaseDenominator) AS ReturnBase
    FROM SalesReturnLines rl
    JOIN SalesReturns sr ON sr.Id = rl.SalesReturnId
    JOIN SalesInvoices si ON si.Id = sr.SalesInvoiceId
    JOIN SalesInvoiceLines l ON l.Id = rl.SalesInvoiceLineId
    JOIN InventoryDocumentLines idl ON idl.Id = l.InventoryDocumentLineId
    JOIN ProductVariants pv ON pv.Id = l.VariantId
    WHERE si.Status = 'posted'
      AND NOT EXISTS (SELECT 1 FROM SalesCancellations sc WHERE sc.SalesInvoiceId = si.Id)
    GROUP BY pv.ProductId
) returned ON returned.ProductId = p.Id
WHERE COALESCE(sold.NetBase,0) - COALESCE(returned.ReturnBase,0) <> 0
ON CONFLICT(ProductId) DO UPDATE SET NetQuantityBaseScaled = excluded.NetQuantityBaseScaled;

-- Maintain on posting: add the actual base units sold by the invoice.
CREATE TRIGGER IF NOT EXISTS TR_ProductPopularity_Posted
AFTER UPDATE ON SalesInvoices
WHEN NEW.Status = 'posted' AND OLD.Status = 'draft'
BEGIN
    INSERT INTO ProductPopularity (ProductId, NetQuantityBaseScaled)
    SELECT pv.ProductId, SUM(l.QuantityScaled * idl.ToBaseNumerator / idl.ToBaseDenominator)
    FROM SalesInvoiceLines l
    JOIN InventoryDocumentLines idl ON idl.Id = l.InventoryDocumentLineId
    JOIN ProductVariants pv ON pv.Id = l.VariantId
    WHERE l.SalesInvoiceId = NEW.Id
    GROUP BY pv.ProductId
    ON CONFLICT(ProductId) DO UPDATE SET NetQuantityBaseScaled = ProductPopularity.NetQuantityBaseScaled + excluded.NetQuantityBaseScaled;
END;

-- Maintain on return: subtract requested returned base units using the original sale unit conversion.
-- This uses the requested return quantity, not the base units that were actually restored to stock.
-- Decrements only happen for products that were previously posted (a projection row exists).
CREATE TRIGGER IF NOT EXISTS TR_ProductPopularity_Returned
AFTER INSERT ON SalesReturnLines
BEGIN
    UPDATE ProductPopularity
    SET NetQuantityBaseScaled = NetQuantityBaseScaled - (
        SELECT NEW.QuantityScaled * idl.ToBaseNumerator / idl.ToBaseDenominator
        FROM SalesInvoiceLines l
        JOIN InventoryDocumentLines idl ON idl.Id = l.InventoryDocumentLineId
        WHERE l.Id = NEW.SalesInvoiceLineId
    )
    WHERE ProductId = (
        SELECT pv.ProductId
        FROM SalesInvoiceLines l
        JOIN ProductVariants pv ON pv.Id = l.VariantId
        WHERE l.Id = NEW.SalesInvoiceLineId
    );
END;

-- Maintain on cancellation: subtract the full original sold base quantity for the canceled invoice.
-- Cancellations are blocked when returns exist, so this subtracts the whole sold quantity once.
CREATE TRIGGER IF NOT EXISTS TR_ProductPopularity_Canceled
AFTER INSERT ON SalesCancellations
BEGIN
    UPDATE ProductPopularity
    SET NetQuantityBaseScaled = NetQuantityBaseScaled - (
        SELECT SUM(l.QuantityScaled * idl.ToBaseNumerator / idl.ToBaseDenominator)
        FROM SalesInvoiceLines l
        JOIN InventoryDocumentLines idl ON idl.Id = l.InventoryDocumentLineId
        WHERE l.SalesInvoiceId = NEW.SalesInvoiceId
          AND EXISTS (SELECT 1 FROM ProductVariants pv WHERE pv.Id = l.VariantId AND pv.ProductId = ProductPopularity.ProductId)
    )
    WHERE ProductId IN (
        SELECT pv.ProductId
        FROM SalesInvoiceLines l
        JOIN ProductVariants pv ON pv.Id = l.VariantId
        WHERE l.SalesInvoiceId = NEW.SalesInvoiceId
    );
END;
