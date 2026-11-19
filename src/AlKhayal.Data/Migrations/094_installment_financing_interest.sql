ALTER TABLE CustomerInstallmentContracts
    ADD COLUMN InterestReturnPolicy TEXT NOT NULL DEFAULT 'keep'
        CHECK(InterestReturnPolicy IN ('cancel', 'keep'));

ALTER TABLE CustomerInstallmentPayments
    ADD COLUMN InterestAmountScaled INTEGER NOT NULL DEFAULT 0 CHECK(InterestAmountScaled >= 0);

ALTER TABLE CustomerInstallmentPartialPayments
    ADD COLUMN InterestAmountScaled INTEGER NOT NULL DEFAULT 0 CHECK(InterestAmountScaled >= 0);

CREATE TABLE CustomerInstallmentInterestAdjustments (
    SalesReturnId INTEGER PRIMARY KEY REFERENCES SalesReturns(Id) ON DELETE RESTRICT,
    ContractId INTEGER NOT NULL REFERENCES CustomerInstallmentContracts(Id) ON DELETE RESTRICT,
    AmountScaled INTEGER NOT NULL CHECK(AmountScaled > 0),
    DebtReductionScaled INTEGER NOT NULL CHECK(DebtReductionScaled >= 0),
    ScheduleReductionScaled INTEGER NOT NULL CHECK(ScheduleReductionScaled >= 0),
    CashRefundScaled INTEGER NOT NULL CHECK(CashRefundScaled >= 0),
    SettlementMethod TEXT NOT NULL CHECK(SettlementMethod IN ('cash', 'credit')),
    CreatedUtc TEXT NOT NULL,
    CHECK(DebtReductionScaled + CashRefundScaled = AmountScaled)
);
CREATE INDEX IX_InstallmentInterestAdjustments_Contract ON CustomerInstallmentInterestAdjustments(ContractId,SalesReturnId);
CREATE TRIGGER TR_InstallmentInterestAdjustments_NoUpdate BEFORE UPDATE ON CustomerInstallmentInterestAdjustments BEGIN SELECT RAISE(ABORT,'installment_interest_adjustment_immutable'); END;
CREATE TRIGGER TR_InstallmentInterestAdjustments_NoDelete BEFORE DELETE ON CustomerInstallmentInterestAdjustments BEGIN SELECT RAISE(ABORT,'installment_interest_adjustment_immutable'); END;

