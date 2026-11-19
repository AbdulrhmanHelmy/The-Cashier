CREATE TABLE InstallmentReceivableEntries (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ContractId INTEGER NOT NULL REFERENCES CustomerInstallmentContracts(Id) ON DELETE RESTRICT,
    SalesReturnId INTEGER NULL REFERENCES SalesReturns(Id) ON DELETE RESTRICT,
    EntryType TEXT NOT NULL CHECK (EntryType IN ('contract_created','down_payment','payment','return_reduction')),
    Direction TEXT NOT NULL CHECK (Direction IN ('debit','credit')),
    AmountScaled INTEGER NOT NULL CHECK (AmountScaled > 0 AND AmountScaled % 100 = 0),
    OperationKey TEXT NOT NULL UNIQUE,
    RequestHash TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL
);

CREATE INDEX IX_InstallmentReceivableEntries_Contract ON InstallmentReceivableEntries(ContractId, Id);
CREATE INDEX IX_InstallmentReceivableEntries_Return ON InstallmentReceivableEntries(SalesReturnId) WHERE SalesReturnId IS NOT NULL;

-- Bring existing contracts into the new ledger so its opening balance is complete.
INSERT INTO InstallmentReceivableEntries(ContractId,EntryType,Direction,AmountScaled,OperationKey,RequestHash,CreatedUtc)
SELECT Id,'contract_created','debit',TotalAmountScaled,'installment-receivable:legacy:contract:'||Id,COALESCE(RequestHash,''),CreatedUtc
FROM CustomerInstallmentContracts;

INSERT INTO InstallmentReceivableEntries(ContractId,EntryType,Direction,AmountScaled,OperationKey,RequestHash,CreatedUtc)
SELECT Id,'down_payment','credit',DownPaymentScaled,'installment-receivable:legacy:down:'||Id,COALESCE(RequestHash,''),CreatedUtc
FROM CustomerInstallmentContracts WHERE DownPaymentScaled>0;

INSERT INTO InstallmentReceivableEntries(ContractId,EntryType,Direction,AmountScaled,OperationKey,RequestHash,CreatedUtc)
SELECT p.ContractId,'payment','credit',p.AmountScaled,'installment-receivable:legacy:payment:'||p.OperationKey,COALESCE(p.RequestHash,''),p.CreatedUtc
FROM CustomerInstallmentPayments p;

INSERT INTO InstallmentReceivableEntries(ContractId,EntryType,Direction,AmountScaled,OperationKey,RequestHash,CreatedUtc)
SELECT p.ContractId,'payment','credit',p.AmountScaled,'installment-receivable:legacy:partial:'||p.OperationKey,COALESCE(p.RequestHash,''),p.CreatedUtc
FROM CustomerInstallmentPartialPayments p;

INSERT INTO InstallmentReceivableEntries(ContractId,SalesReturnId,EntryType,Direction,AmountScaled,OperationKey,RequestHash,CreatedUtc)
SELECT a.ContractId,a.SalesReturnId,'return_reduction','credit',SUM(a.AmountScaled),
       'installment-receivable:legacy:return:'||a.SalesReturnId||':'||a.ContractId,
       COALESCE(c.RequestHash,''),r.CreatedUtc
FROM CustomerInstallmentReturnAllocations a
JOIN CustomerInstallmentContracts c ON c.Id=a.ContractId
JOIN SalesReturns r ON r.Id=a.SalesReturnId
GROUP BY a.ContractId,a.SalesReturnId;

CREATE TRIGGER TR_InstallmentReceivableEntries_NoUpdate
BEFORE UPDATE ON InstallmentReceivableEntries
BEGIN
    SELECT RAISE(ABORT, 'installment receivable entries are immutable');
END;

CREATE TRIGGER TR_InstallmentReceivableEntries_NoDelete
BEFORE DELETE ON InstallmentReceivableEntries
BEGIN
    SELECT RAISE(ABORT, 'installment receivable entries are immutable');
END;
