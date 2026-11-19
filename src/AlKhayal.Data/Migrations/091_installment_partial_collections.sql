-- Existing full-payment rows remain immutable. This append-only table permits
-- multiple partial collections against a schedule row without rewriting history.
CREATE TABLE CustomerInstallmentPartialPayments (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ContractId INTEGER NOT NULL,
    InstallmentNumber INTEGER NOT NULL,
    OperationKey TEXT NOT NULL UNIQUE,
    RequestHash TEXT NOT NULL,
    AmountScaled INTEGER NOT NULL CHECK(AmountScaled > 0),
    PaidOn TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL,
    FOREIGN KEY(ContractId,InstallmentNumber) REFERENCES CustomerInstallmentSchedule(ContractId,InstallmentNumber) ON DELETE RESTRICT
);
CREATE INDEX IX_CustomerInstallmentPartialPayments_Row ON CustomerInstallmentPartialPayments(ContractId,InstallmentNumber);
CREATE TRIGGER TR_CustomerInstallmentPartialPayments_NoUpdate BEFORE UPDATE ON CustomerInstallmentPartialPayments BEGIN SELECT RAISE(ABORT,'installment_partial_payment_immutable'); END;
CREATE TRIGGER TR_CustomerInstallmentPartialPayments_NoDelete BEFORE DELETE ON CustomerInstallmentPartialPayments BEGIN SELECT RAISE(ABORT,'installment_partial_payment_immutable'); END;
