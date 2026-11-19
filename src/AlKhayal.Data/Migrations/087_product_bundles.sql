-- Product bundles are virtual sellable products whose component stock is consumed
-- in the same inventory transaction as the sale. Historical invoices are unchanged.
CREATE TABLE ProductBundleComponents (
    BundleProductId INTEGER NOT NULL REFERENCES Products(Id) ON DELETE RESTRICT,
    ComponentVariantId INTEGER NOT NULL REFERENCES ProductVariants(Id) ON DELETE RESTRICT,
    QuantityBaseScaled INTEGER NOT NULL CHECK(QuantityBaseScaled > 0),
    PRIMARY KEY(BundleProductId, ComponentVariantId)
);

CREATE INDEX IX_ProductBundleComponents_Component ON ProductBundleComponents(ComponentVariantId);

-- Immutable snapshot of the actual component issue lines for each posted bundle sale line.
-- QuantityBaseScaled is the total component quantity issued for the sold invoice line.
CREATE TABLE SalesInvoiceBundleComponentLines (
    SalesInvoiceLineId INTEGER NOT NULL REFERENCES SalesInvoiceLines(Id) ON DELETE RESTRICT,
    InventoryDocumentLineId INTEGER NOT NULL REFERENCES InventoryDocumentLines(Id) ON DELETE RESTRICT,
    QuantityBaseScaled INTEGER NOT NULL CHECK(QuantityBaseScaled > 0),
    PRIMARY KEY(SalesInvoiceLineId, InventoryDocumentLineId)
);

CREATE INDEX IX_SalesInvoiceBundleComponentLines_DocumentLine
    ON SalesInvoiceBundleComponentLines(InventoryDocumentLineId);

CREATE TRIGGER TR_SalesInvoiceBundleComponentLines_NoUpdate
BEFORE UPDATE ON SalesInvoiceBundleComponentLines
BEGIN SELECT RAISE(ABORT,'sales_bundle_component_snapshot_immutable'); END;

CREATE TRIGGER TR_SalesInvoiceBundleComponentLines_NoDelete
BEFORE DELETE ON SalesInvoiceBundleComponentLines
BEGIN SELECT RAISE(ABORT,'sales_bundle_component_snapshot_immutable'); END;

-- Existing popularity triggers rely on one inventory line per invoice line.
-- Preserve their behavior for regular items and account for virtual bundle lines separately.
DROP TRIGGER IF EXISTS TR_ProductPopularity_Returned;
CREATE TRIGGER TR_ProductPopularity_Returned
AFTER INSERT ON SalesReturnLines
WHEN EXISTS(SELECT 1 FROM SalesInvoiceLines l WHERE l.Id=NEW.SalesInvoiceLineId AND l.InventoryDocumentLineId IS NOT NULL)
BEGIN
    UPDATE ProductPopularity
    SET NetQuantityBaseScaled = NetQuantityBaseScaled - (
        SELECT NEW.QuantityScaled * idl.ToBaseNumerator / idl.ToBaseDenominator
        FROM SalesInvoiceLines l JOIN InventoryDocumentLines idl ON idl.Id=l.InventoryDocumentLineId
        WHERE l.Id=NEW.SalesInvoiceLineId)
    WHERE ProductId=(SELECT pv.ProductId FROM SalesInvoiceLines l JOIN ProductVariants pv ON pv.Id=l.VariantId WHERE l.Id=NEW.SalesInvoiceLineId);
END;

CREATE TRIGGER TR_ProductPopularity_BundlePosted
AFTER UPDATE ON SalesInvoices
WHEN NEW.Status='posted' AND OLD.Status='draft'
BEGIN
    INSERT INTO ProductPopularity(ProductId,NetQuantityBaseScaled)
    SELECT ProductId,SUM(QuantityScaled) FROM (
        SELECT DISTINCT pv.ProductId,l.Id,l.QuantityScaled
        FROM SalesInvoiceLines l JOIN ProductVariants pv ON pv.Id=l.VariantId
        JOIN SalesInvoiceBundleComponentLines bc ON bc.SalesInvoiceLineId=l.Id
        WHERE l.SalesInvoiceId=NEW.Id)
    GROUP BY ProductId
    ON CONFLICT(ProductId) DO UPDATE SET NetQuantityBaseScaled=ProductPopularity.NetQuantityBaseScaled+excluded.NetQuantityBaseScaled;
END;

CREATE TRIGGER TR_ProductPopularity_BundleReturned
AFTER INSERT ON SalesReturnLines
WHEN EXISTS(SELECT 1 FROM SalesInvoiceBundleComponentLines bc WHERE bc.SalesInvoiceLineId=NEW.SalesInvoiceLineId)
BEGIN
    UPDATE ProductPopularity
    SET NetQuantityBaseScaled=NetQuantityBaseScaled-NEW.QuantityScaled
    WHERE ProductId=(SELECT pv.ProductId FROM SalesInvoiceLines l JOIN ProductVariants pv ON pv.Id=l.VariantId WHERE l.Id=NEW.SalesInvoiceLineId);
END;

CREATE TRIGGER TR_ProductPopularity_BundleCanceled
AFTER INSERT ON SalesCancellations
BEGIN
    UPDATE ProductPopularity
    SET NetQuantityBaseScaled=NetQuantityBaseScaled-COALESCE((
        SELECT SUM(l.QuantityScaled) FROM SalesInvoiceLines l JOIN ProductVariants pv ON pv.Id=l.VariantId
        JOIN SalesInvoiceBundleComponentLines bc ON bc.SalesInvoiceLineId=l.Id
        WHERE l.SalesInvoiceId=NEW.SalesInvoiceId AND pv.ProductId=ProductPopularity.ProductId),0)
    WHERE ProductId IN (
        SELECT pv.ProductId FROM SalesInvoiceLines l JOIN ProductVariants pv ON pv.Id=l.VariantId
        JOIN SalesInvoiceBundleComponentLines bc ON bc.SalesInvoiceLineId=l.Id WHERE l.SalesInvoiceId=NEW.SalesInvoiceId);
END;

CREATE TRIGGER TR_ProductPopularity_BundleCorrected
AFTER INSERT ON SalesInvoiceCorrections
BEGIN
    UPDATE ProductPopularity
    SET NetQuantityBaseScaled=NetQuantityBaseScaled-COALESCE((
        SELECT SUM(bundleLine.QuantityScaled) FROM (
            SELECT DISTINCT l.Id,l.QuantityScaled,pv.ProductId
            FROM SalesInvoiceLines l JOIN ProductVariants pv ON pv.Id=l.VariantId
            JOIN SalesInvoiceBundleComponentLines bc ON bc.SalesInvoiceLineId=l.Id
            WHERE l.SalesInvoiceId=NEW.PreviousInvoiceId) bundleLine
        WHERE bundleLine.ProductId=ProductPopularity.ProductId),0)
    WHERE ProductId IN (
        SELECT pv.ProductId FROM SalesInvoiceLines l JOIN ProductVariants pv ON pv.Id=l.VariantId
        JOIN SalesInvoiceBundleComponentLines bc ON bc.SalesInvoiceLineId=l.Id WHERE l.SalesInvoiceId=NEW.PreviousInvoiceId);
END;
