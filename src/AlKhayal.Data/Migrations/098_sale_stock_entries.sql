-- A single invoice line can consume several internally identified stock entries.
-- Existing invoices retain SalesInvoiceLines.InventoryDocumentLineId unchanged.
CREATE TABLE SalesInvoiceStockEntries (
 SalesInvoiceLineId INTEGER NOT NULL REFERENCES SalesInvoiceLines(Id) ON DELETE RESTRICT,
 InventoryDocumentLineId INTEGER NOT NULL UNIQUE REFERENCES InventoryDocumentLines(Id) ON DELETE RESTRICT,
 QuantityBaseScaled INTEGER NOT NULL CHECK(QuantityBaseScaled>0),
 PRIMARY KEY(SalesInvoiceLineId,InventoryDocumentLineId)
);
CREATE INDEX IX_SalesInvoiceStockEntries_Line ON SalesInvoiceStockEntries(SalesInvoiceLineId);
CREATE TRIGGER TR_SalesInvoiceStockEntries_NoUpdate BEFORE UPDATE ON SalesInvoiceStockEntries BEGIN SELECT RAISE(ABORT,'sale_stock_entry_immutable'); END;
CREATE TRIGGER TR_SalesInvoiceStockEntries_NoDelete BEFORE DELETE ON SalesInvoiceStockEntries BEGIN SELECT RAISE(ABORT,'sale_stock_entry_immutable'); END;

-- The movement distinguishes repeated issues from the same FIFO layer on one invoice.
ALTER TABLE SalesReturnCostAllocations ADD COLUMN SourceMovementId INTEGER NULL REFERENCES StockMovements(Id) ON DELETE RESTRICT;
CREATE INDEX IX_SalesReturnCostAllocations_Source ON SalesReturnCostAllocations(SourceMovementId,CostLayerId);

-- Returned expired or damaged goods retain their value and provenance but cannot
-- be selected for an ordinary sale. Existing layers remain sellable.
ALTER TABLE InventoryCostLayers ADD COLUMN IsSellable INTEGER NOT NULL DEFAULT 1 CHECK(IsSellable IN (0,1));
