-- A fully discounted sold line is a legitimate zero-value return: stock and FIFO cost
-- are still restored while the monetary refund is zero. Historical rows, identities,
-- quantity checks and immutability are preserved exactly; only the positive-amount
-- requirement is relaxed to nonnegative. Migrator checks FKs before committing.
CREATE TABLE SalesReturns_085 (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ReturnNumber TEXT NOT NULL UNIQUE,
    SalesInvoiceId INTEGER NOT NULL REFERENCES SalesInvoices(Id) ON DELETE RESTRICT,
    InventoryDocumentId INTEGER NOT NULL UNIQUE REFERENCES InventoryDocuments(Id) ON DELETE RESTRICT,
    OperationKey TEXT NOT NULL UNIQUE,
    RequestHash TEXT NOT NULL,
    TotalScaled INTEGER NOT NULL CHECK(TotalScaled>=0),
    CashRefundScaled INTEGER NOT NULL CHECK(CashRefundScaled>=0),
    CreditReductionScaled INTEGER NOT NULL CHECK(CreditReductionScaled>=0),
    Reason TEXT NOT NULL CHECK(length(trim(Reason)) BETWEEN 2 AND 240),
    CreatedByUserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
    CreatedUtc TEXT NOT NULL,
    CHECK(CashRefundScaled+CreditReductionScaled=TotalScaled)
);
INSERT INTO SalesReturns_085(Id,ReturnNumber,SalesInvoiceId,InventoryDocumentId,OperationKey,RequestHash,TotalScaled,CashRefundScaled,CreditReductionScaled,Reason,CreatedByUserId,CreatedUtc)
SELECT Id,ReturnNumber,SalesInvoiceId,InventoryDocumentId,OperationKey,RequestHash,TotalScaled,CashRefundScaled,CreditReductionScaled,Reason,CreatedByUserId,CreatedUtc FROM SalesReturns;
DROP TABLE SalesReturns;
ALTER TABLE SalesReturns_085 RENAME TO SalesReturns;
CREATE INDEX IX_SalesReturns_Event ON SalesReturns(CreatedUtc,Id);
CREATE TRIGGER TR_SalesReturns_NoUpdate BEFORE UPDATE ON SalesReturns BEGIN SELECT RAISE(ABORT,'sales_return_immutable'); END;
CREATE TRIGGER TR_SalesReturns_NoDelete BEFORE DELETE ON SalesReturns BEGIN SELECT RAISE(ABORT,'sales_return_immutable'); END;

CREATE TABLE SalesReturnLines_085 (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SalesReturnId INTEGER NOT NULL REFERENCES SalesReturns(Id) ON DELETE RESTRICT,
    SalesInvoiceLineId INTEGER NOT NULL REFERENCES SalesInvoiceLines(Id) ON DELETE RESTRICT,
    QuantityScaled INTEGER NOT NULL CHECK(QuantityScaled>0),
    AmountScaled INTEGER NOT NULL CHECK(AmountScaled>=0),
    UNIQUE(SalesReturnId,SalesInvoiceLineId)
);
-- The copy targets the trigger-free replacement table, so the ProductPopularity
-- projection is not decremented again for historical return lines.
INSERT INTO SalesReturnLines_085(Id,SalesReturnId,SalesInvoiceLineId,QuantityScaled,AmountScaled)
SELECT Id,SalesReturnId,SalesInvoiceLineId,QuantityScaled,AmountScaled FROM SalesReturnLines;
DROP TABLE SalesReturnLines;
ALTER TABLE SalesReturnLines_085 RENAME TO SalesReturnLines;
CREATE INDEX IX_SalesReturnLines_InvoiceLine ON SalesReturnLines(SalesInvoiceLineId);
CREATE INDEX IX_SalesReturnLines_Original ON SalesReturnLines(SalesInvoiceLineId,SalesReturnId);
CREATE TRIGGER TR_SalesReturnLines_NoUpdate BEFORE UPDATE ON SalesReturnLines BEGIN SELECT RAISE(ABORT,'sales_return_immutable'); END;
CREATE TRIGGER TR_SalesReturnLines_NoDelete BEFORE DELETE ON SalesReturnLines BEGIN SELECT RAISE(ABORT,'sales_return_immutable'); END;
CREATE TRIGGER TR_ProductPopularity_Returned AFTER INSERT ON SalesReturnLines
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
