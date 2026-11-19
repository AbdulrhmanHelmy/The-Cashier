ALTER TABLE Employees ADD COLUMN NationalId TEXT;
CREATE UNIQUE INDEX IX_Employees_NationalId ON Employees(NationalId) WHERE NationalId IS NOT NULL AND length(trim(NationalId))>0;
ALTER TABLE EmployeeAttendance ADD COLUMN BreakMinutes INTEGER NOT NULL DEFAULT 0 CHECK(BreakMinutes BETWEEN 0 AND 1440);
CREATE TABLE EmployeeAdvances (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 EmployeeId INTEGER NOT NULL REFERENCES Employees(Id) ON DELETE RESTRICT,
 AmountScaled INTEGER NOT NULL CHECK(typeof(AmountScaled)='integer' AND AmountScaled>0 AND AmountScaled%100=0),
 CreatedUtc TEXT NOT NULL, TreasuryMovementId INTEGER NOT NULL REFERENCES TreasuryMovements(Id) ON DELETE RESTRICT,
 ActorUserId INTEGER NOT NULL REFERENCES Users(Id), OperationKey TEXT NOT NULL UNIQUE, Notes TEXT
);
CREATE INDEX IX_EmployeeAdvances_EmployeeDate ON EmployeeAdvances(EmployeeId,CreatedUtc,Id);
CREATE TABLE EmployeePayrollPayments (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 EmployeeId INTEGER NOT NULL REFERENCES Employees(Id) ON DELETE RESTRICT,
 SalaryScaled INTEGER NOT NULL CHECK(typeof(SalaryScaled)='integer' AND SalaryScaled>=0 AND SalaryScaled%100=0),
 AdvancesScaled INTEGER NOT NULL CHECK(typeof(AdvancesScaled)='integer' AND AdvancesScaled>=0 AND AdvancesScaled%100=0),
 NetScaled INTEGER NOT NULL CHECK(typeof(NetScaled)='integer' AND NetScaled>=0 AND NetScaled%100=0),
 PaidUtc TEXT NOT NULL, Period TEXT NOT NULL, TreasuryMovementId INTEGER REFERENCES TreasuryMovements(Id) ON DELETE RESTRICT,
 ActorUserId INTEGER NOT NULL REFERENCES Users(Id), OperationKey TEXT NOT NULL UNIQUE, Notes TEXT,
 UNIQUE(EmployeeId,Period), CHECK(NetScaled=SalaryScaled-AdvancesScaled)
);
CREATE INDEX IX_EmployeePayroll_EmployeePeriod ON EmployeePayrollPayments(EmployeeId,Period,Id);
CREATE TABLE EmployeePayrollAdvanceAllocations (
 AdvanceId INTEGER PRIMARY KEY REFERENCES EmployeeAdvances(Id) ON DELETE RESTRICT,
 PayrollPaymentId INTEGER NOT NULL REFERENCES EmployeePayrollPayments(Id) ON DELETE RESTRICT,
 AmountScaled INTEGER NOT NULL CHECK(typeof(AmountScaled)='integer' AND AmountScaled>0 AND AmountScaled%100=0)
);
CREATE INDEX IX_EmployeePayrollAllocation_Payment ON EmployeePayrollAdvanceAllocations(PayrollPaymentId,AdvanceId);
CREATE TRIGGER TR_EmployeeAdvances_NoUpdate BEFORE UPDATE ON EmployeeAdvances BEGIN SELECT RAISE(ABORT,'advance_immutable'); END;
CREATE TRIGGER TR_EmployeeAdvances_NoDelete BEFORE DELETE ON EmployeeAdvances BEGIN SELECT RAISE(ABORT,'advance_immutable'); END;
CREATE TRIGGER TR_EmployeePayroll_NoUpdate BEFORE UPDATE ON EmployeePayrollPayments BEGIN SELECT RAISE(ABORT,'payroll_immutable'); END;
CREATE TRIGGER TR_EmployeePayroll_NoDelete BEFORE DELETE ON EmployeePayrollPayments BEGIN SELECT RAISE(ABORT,'payroll_immutable'); END;
CREATE TRIGGER TR_EmployeePayrollAllocation_NoUpdate BEFORE UPDATE ON EmployeePayrollAdvanceAllocations BEGIN SELECT RAISE(ABORT,'payroll_allocation_immutable'); END;
CREATE TRIGGER TR_EmployeePayrollAllocation_NoDelete BEFORE DELETE ON EmployeePayrollAdvanceAllocations BEGIN SELECT RAISE(ABORT,'payroll_allocation_immutable'); END;
