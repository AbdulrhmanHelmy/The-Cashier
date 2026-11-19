-- Only new explicit drawer-account movements are linked. No historical ownership inference.
ALTER TABLE CashierShiftEvents ADD COLUMN TreasuryMovementId INTEGER NULL REFERENCES TreasuryMovements(Id);
CREATE UNIQUE INDEX IX_ShiftEvent_Treasury ON CashierShiftEvents(TreasuryMovementId) WHERE TreasuryMovementId IS NOT NULL;
CREATE INDEX IX_SalesShiftOrigin_Shift ON SalesInvoiceShiftOrigins(ShiftId,SalesInvoiceId);
CREATE INDEX IX_OperationAudit_Resource ON OperationAuditLog(Resource,Id);
