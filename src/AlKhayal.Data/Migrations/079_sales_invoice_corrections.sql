-- Immutable posted-sale correction chain. Historical invoices and their financial evidence remain unchanged.
ALTER TABLE SalesInvoices ADD COLUMN RootInvoiceId INTEGER NULL REFERENCES SalesInvoices(Id) ON DELETE RESTRICT;
ALTER TABLE SalesInvoices ADD COLUMN Revision INTEGER NOT NULL DEFAULT 0;

CREATE TABLE SalesInvoiceCorrections (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 RootInvoiceId INTEGER NOT NULL REFERENCES SalesInvoices(Id) ON DELETE RESTRICT,
 PreviousInvoiceId INTEGER NOT NULL UNIQUE REFERENCES SalesInvoices(Id) ON DELETE RESTRICT,
 ReplacementInvoiceId INTEGER NOT NULL UNIQUE REFERENCES SalesInvoices(Id) ON DELETE RESTRICT,
 Revision INTEGER NOT NULL CHECK(Revision>0),
 ReversalInventoryDocumentId INTEGER NOT NULL UNIQUE REFERENCES InventoryDocuments(Id) ON DELETE RESTRICT,
 OperationKey TEXT NOT NULL UNIQUE,
 RequestHash TEXT NOT NULL,
 SettlementKind TEXT NOT NULL CHECK(SettlementKind IN ('none','refund','customer_credit','collect','receivable')),
 SettlementMethod TEXT NULL CHECK(SettlementMethod IS NULL OR SettlementMethod IN ('cash','transfer')),
 SettlementAmountScaled INTEGER NOT NULL CHECK(SettlementAmountScaled>=0),
 TreasuryAccountId INTEGER NULL REFERENCES TreasuryAccounts(Id) ON DELETE RESTRICT,
 TreasuryMovementId INTEGER NULL UNIQUE REFERENCES TreasuryMovements(Id) ON DELETE RESTRICT,
 ActorUserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
 CreatedUtc TEXT NOT NULL,
 UNIQUE(RootInvoiceId,Revision)
);
CREATE INDEX IX_SalesInvoiceCorrections_Root ON SalesInvoiceCorrections(RootInvoiceId,Revision);

-- This is separate from cash shift events so electronic sales also retain their authoritative origin shift.
CREATE TABLE SalesInvoiceShiftOrigins (
 SalesInvoiceId INTEGER PRIMARY KEY REFERENCES SalesInvoices(Id) ON DELETE RESTRICT,
 ShiftId INTEGER NOT NULL REFERENCES CashierShifts(Id) ON DELETE RESTRICT,
 CreatedUtc TEXT NOT NULL
);
INSERT OR IGNORE INTO SalesInvoiceShiftOrigins(SalesInvoiceId,ShiftId,CreatedUtc)
SELECT SalesInvoiceId,ShiftId,CreatedUtc FROM CashierShiftEvents
WHERE SalesInvoiceId IS NOT NULL AND Kind IN ('cash_sale','credit_sale');

ALTER TABLE SalesPayments ADD COLUMN SalesCorrectionId INTEGER NULL REFERENCES SalesInvoiceCorrections(Id) ON DELETE RESTRICT;
ALTER TABLE CustomerTransactions ADD COLUMN SalesCorrectionId INTEGER NULL REFERENCES SalesInvoiceCorrections(Id) ON DELETE RESTRICT;
ALTER TABLE CashierShiftEvents ADD COLUMN SalesCorrectionId INTEGER NULL REFERENCES SalesInvoiceCorrections(Id) ON DELETE RESTRICT;
CREATE UNIQUE INDEX IX_SalesPayments_Correction ON SalesPayments(SalesCorrectionId) WHERE SalesCorrectionId IS NOT NULL;
CREATE INDEX IX_CustomerTransactions_Correction ON CustomerTransactions(SalesCorrectionId);
CREATE UNIQUE INDEX IX_ShiftEvent_Correction ON CashierShiftEvents(SalesCorrectionId) WHERE SalesCorrectionId IS NOT NULL;

CREATE TRIGGER TR_SalesInvoiceCorrections_NoUpdate BEFORE UPDATE ON SalesInvoiceCorrections BEGIN SELECT RAISE(ABORT,'sales_correction_immutable'); END;
CREATE TRIGGER TR_SalesInvoiceCorrections_NoDelete BEFORE DELETE ON SalesInvoiceCorrections BEGIN SELECT RAISE(ABORT,'sales_correction_immutable'); END;
CREATE TRIGGER TR_SalesInvoiceShiftOrigins_NoUpdate BEFORE UPDATE ON SalesInvoiceShiftOrigins BEGIN SELECT RAISE(ABORT,'sales_shift_origin_immutable'); END;
CREATE TRIGGER TR_SalesInvoiceShiftOrigins_NoDelete BEFORE DELETE ON SalesInvoiceShiftOrigins BEGIN SELECT RAISE(ABORT,'sales_shift_origin_immutable'); END;

-- Posting the replacement adds its popularity. Subtract the superseded invoice exactly once.
CREATE TRIGGER TR_ProductPopularity_Corrected
AFTER INSERT ON SalesInvoiceCorrections
BEGIN
 UPDATE ProductPopularity
 SET NetQuantityBaseScaled = NetQuantityBaseScaled - (
   SELECT SUM(l.QuantityScaled * idl.ToBaseNumerator / idl.ToBaseDenominator)
   FROM SalesInvoiceLines l JOIN InventoryDocumentLines idl ON idl.Id=l.InventoryDocumentLineId
   JOIN ProductVariants pv ON pv.Id=l.VariantId
   WHERE l.SalesInvoiceId=NEW.PreviousInvoiceId AND pv.ProductId=ProductPopularity.ProductId)
 WHERE ProductId IN (SELECT pv.ProductId FROM SalesInvoiceLines l JOIN ProductVariants pv ON pv.Id=l.VariantId WHERE l.SalesInvoiceId=NEW.PreviousInvoiceId);
END;
