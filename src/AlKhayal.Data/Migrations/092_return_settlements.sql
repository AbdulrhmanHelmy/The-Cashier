-- Append-only reductions preserve the original contract, schedule, and collections.
CREATE TABLE CustomerInstallmentReturnAllocations (
 SalesReturnId INTEGER NOT NULL REFERENCES SalesReturns(Id) ON DELETE RESTRICT,
 ContractId INTEGER NOT NULL REFERENCES CustomerInstallmentContracts(Id) ON DELETE RESTRICT,
 InstallmentNumber INTEGER NOT NULL,
 AmountScaled INTEGER NOT NULL CHECK(AmountScaled>0),
 PRIMARY KEY(SalesReturnId,ContractId,InstallmentNumber),
 FOREIGN KEY(ContractId,InstallmentNumber) REFERENCES CustomerInstallmentSchedule(ContractId,InstallmentNumber) ON DELETE RESTRICT
);
CREATE INDEX IX_InstallmentReturnAllocations_Row ON CustomerInstallmentReturnAllocations(ContractId,InstallmentNumber);
CREATE TRIGGER TR_InstallmentReturnAllocations_NoUpdate BEFORE UPDATE ON CustomerInstallmentReturnAllocations BEGIN SELECT RAISE(ABORT,'installment_return_immutable'); END;
CREATE TRIGGER TR_InstallmentReturnAllocations_NoDelete BEFORE DELETE ON CustomerInstallmentReturnAllocations BEGIN SELECT RAISE(ABORT,'installment_return_immutable'); END;

-- CashRefundScaled on SalesReturns denotes the amount owed back to the payer.
-- An electronic obligation is recorded here instead of a cash treasury movement.
CREATE TABLE SalesElectronicRefunds (
 SalesReturnId INTEGER PRIMARY KEY REFERENCES SalesReturns(Id) ON DELETE RESTRICT,
 SalesInvoiceId INTEGER NOT NULL REFERENCES SalesInvoices(Id) ON DELETE RESTRICT,
 Method TEXT NOT NULL CHECK(Method IN ('card','wallet')),
 InstrumentId INTEGER NOT NULL REFERENCES PaymentInstruments(Id) ON DELETE RESTRICT,
 OriginalPaymentKey TEXT NOT NULL,
 AmountScaled INTEGER NOT NULL CHECK(AmountScaled>0),
 CreatedUtc TEXT NOT NULL
);
CREATE TABLE SalesElectronicRefundConfirmations (
 SalesReturnId INTEGER PRIMARY KEY REFERENCES SalesElectronicRefunds(SalesReturnId) ON DELETE RESTRICT,
 ExternalReference TEXT NOT NULL CHECK(length(trim(ExternalReference)) BETWEEN 2 AND 120),
 OperationKey TEXT NOT NULL UNIQUE,
 RequestHash TEXT NOT NULL,
 ActorUserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
 ConfirmedUtc TEXT NOT NULL,
 UNIQUE(ExternalReference)
);
CREATE UNIQUE INDEX UX_SalesElectronicRefundConfirmations_ReferenceNormalized ON SalesElectronicRefundConfirmations(lower(trim(ExternalReference)));
CREATE TRIGGER TR_SalesElectronicRefunds_NoUpdate BEFORE UPDATE ON SalesElectronicRefunds BEGIN SELECT RAISE(ABORT,'electronic_refund_immutable'); END;
CREATE TRIGGER TR_SalesElectronicRefunds_NoDelete BEFORE DELETE ON SalesElectronicRefunds BEGIN SELECT RAISE(ABORT,'electronic_refund_immutable'); END;
CREATE TRIGGER TR_SalesElectronicRefundConfirmations_NoUpdate BEFORE UPDATE ON SalesElectronicRefundConfirmations BEGIN SELECT RAISE(ABORT,'electronic_refund_confirmation_immutable'); END;
CREATE TRIGGER TR_SalesElectronicRefundConfirmations_NoDelete BEFORE DELETE ON SalesElectronicRefundConfirmations BEGIN SELECT RAISE(ABORT,'electronic_refund_confirmation_immutable'); END;
