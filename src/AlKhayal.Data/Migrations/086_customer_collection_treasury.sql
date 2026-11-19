-- Prospective links only. Historical collections remain unchanged and unlinked.
CREATE TABLE CustomerCollectionTreasuryLinks (
    CustomerTransactionId INTEGER PRIMARY KEY REFERENCES CustomerTransactions(Id) ON DELETE RESTRICT,
    TreasuryMovementId INTEGER NOT NULL UNIQUE REFERENCES TreasuryMovements(Id) ON DELETE RESTRICT,
    TreasuryAccountId INTEGER NOT NULL REFERENCES TreasuryAccounts(Id) ON DELETE RESTRICT,
    PaymentMethod TEXT NOT NULL CHECK(PaymentMethod IN ('cash','card','transfer','wallet'))
);
CREATE INDEX IX_CustomerCollectionTreasuryLinks_Account ON CustomerCollectionTreasuryLinks(TreasuryAccountId);
CREATE TRIGGER CustomerCollectionTreasuryLinks_Match BEFORE INSERT ON CustomerCollectionTreasuryLinks
WHEN NOT EXISTS (
    SELECT 1 FROM CustomerTransactions t JOIN TreasuryMovements m ON m.Id=NEW.TreasuryMovementId
    WHERE t.Id=NEW.CustomerTransactionId AND t.TransactionType='collection'
      AND m.Type='deposit' AND m.AmountScaled=t.AmountScaled AND m.AccountId=NEW.TreasuryAccountId
)
BEGIN SELECT RAISE(ABORT,'collection_treasury_mismatch'); END;
CREATE TRIGGER CustomerCollectionTreasuryLinks_NoUpdate BEFORE UPDATE ON CustomerCollectionTreasuryLinks BEGIN SELECT RAISE(ABORT,'collection_treasury_link_immutable'); END;
CREATE TRIGGER CustomerCollectionTreasuryLinks_NoDelete BEFORE DELETE ON CustomerCollectionTreasuryLinks BEGIN SELECT RAISE(ABORT,'collection_treasury_link_immutable'); END;
ALTER TABLE CustomerPayments ADD COLUMN OperationKey TEXT NULL;
ALTER TABLE CustomerPayments ADD COLUMN RequestHash TEXT NULL;
CREATE UNIQUE INDEX UX_CustomerPayments_OperationKey ON CustomerPayments(OperationKey) WHERE OperationKey IS NOT NULL;
