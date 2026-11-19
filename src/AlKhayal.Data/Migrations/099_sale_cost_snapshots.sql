-- Cost evidence is independent of physical inventory. Historical rows are deliberately
-- not backfilled: a missing snapshot cannot prove a historical purchase price.
CREATE TABLE SalesInvoiceCostParts (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 SalesInvoiceLineId INTEGER NOT NULL REFERENCES SalesInvoiceLines(Id) ON DELETE RESTRICT,
 SourceDocumentLineId INTEGER NOT NULL REFERENCES InventoryDocumentLines(Id) ON DELETE RESTRICT,
 SourceMovementId INTEGER NULL REFERENCES StockMovements(Id) ON DELETE RESTRICT,
 SourceAllocationId INTEGER NULL REFERENCES InventoryCostAllocations(Id) ON DELETE RESTRICT,
 SourcePurchaseMovementId INTEGER NULL REFERENCES StockMovements(Id) ON DELETE RESTRICT,
 Basis TEXT NOT NULL CHECK(Basis IN ('actual','estimated_purchase','estimated_configured','unknown')),
 QuantityBaseScaled INTEGER NOT NULL CHECK(QuantityBaseScaled>0),
 ValueScaled INTEGER NULL CHECK(ValueScaled IS NULL OR ValueScaled>=0),
 UnitCostScaled INTEGER NULL CHECK(UnitCostScaled IS NULL OR UnitCostScaled>=0),
 CreatedUtc TEXT NOT NULL,
 CHECK((Basis='unknown' AND ValueScaled IS NULL AND UnitCostScaled IS NULL) OR (Basis<>'unknown' AND ValueScaled IS NOT NULL)),
 CHECK((Basis='actual' AND SourceMovementId IS NOT NULL AND SourceAllocationId IS NOT NULL) OR Basis='unknown' OR (Basis<>'actual' AND SourceAllocationId IS NULL))
);
CREATE UNIQUE INDEX UX_SalesInvoiceCostParts_Allocation ON SalesInvoiceCostParts(SourceAllocationId) WHERE SourceAllocationId IS NOT NULL;
CREATE UNIQUE INDEX UX_SalesInvoiceCostParts_Estimate ON SalesInvoiceCostParts(SalesInvoiceLineId,SourceDocumentLineId) WHERE SourceAllocationId IS NULL;
CREATE INDEX IX_SalesInvoiceCostParts_Line ON SalesInvoiceCostParts(SalesInvoiceLineId);
CREATE TRIGGER TR_SalesInvoiceCostParts_NoUpdate BEFORE UPDATE ON SalesInvoiceCostParts BEGIN SELECT RAISE(ABORT,'sale_cost_immutable'); END;
CREATE TRIGGER TR_SalesInvoiceCostParts_NoDelete BEFORE DELETE ON SalesInvoiceCostParts BEGIN SELECT RAISE(ABORT,'sale_cost_immutable'); END;

CREATE TABLE SalesReturnEstimatedCosts (
 SalesReturnLineId INTEGER PRIMARY KEY REFERENCES SalesReturnLines(Id) ON DELETE RESTRICT,
 QuantityBaseScaled INTEGER NOT NULL CHECK(QuantityBaseScaled>=0),
 ValueScaled INTEGER NULL CHECK(ValueScaled IS NULL OR ValueScaled>=0),
 CreatedUtc TEXT NOT NULL
);
CREATE TRIGGER TR_SalesReturnEstimatedCosts_NoUpdate BEFORE UPDATE ON SalesReturnEstimatedCosts BEGIN SELECT RAISE(ABORT,'return_estimated_cost_immutable'); END;
CREATE TRIGGER TR_SalesReturnEstimatedCosts_NoDelete BEFORE DELETE ON SalesReturnEstimatedCosts BEGIN SELECT RAISE(ABORT,'return_estimated_cost_immutable'); END;
