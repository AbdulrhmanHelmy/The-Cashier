-- Delivery is intentionally a sale adjunct: product revenue and inventory valuation stay on SalesInvoiceLines.
ALTER TABLE Customers ADD COLUMN SecondaryPhone TEXT NULL;

CREATE TABLE Drivers (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 Name TEXT NOT NULL CHECK(length(trim(Name)) BETWEEN 1 AND 160),
 Phone TEXT NOT NULL CHECK(length(trim(Phone)) BETWEEN 3 AND 40),
 IsActive INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0,1)),
 CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL
);
CREATE INDEX IX_Drivers_Active_Name ON Drivers(IsActive,Name,Id);

CREATE TABLE SalesInvoiceDeliveries (
 SalesInvoiceId INTEGER PRIMARY KEY REFERENCES SalesInvoices(Id) ON DELETE RESTRICT,
 DriverId INTEGER NULL REFERENCES Drivers(Id) ON DELETE RESTRICT,
 DriverName TEXT NOT NULL, DriverPhone TEXT NOT NULL,
 CustomerName TEXT NOT NULL, PrimaryPhone TEXT NOT NULL, SecondaryPhone TEXT NULL,
 Address TEXT NOT NULL, FeeScaled INTEGER NOT NULL CHECK(typeof(FeeScaled)='integer' AND FeeScaled>=0 AND FeeScaled%100=0),
 Status TEXT NOT NULL DEFAULT 'pending' CHECK(Status IN ('pending','out_for_delivery','delivered','failed')),
 Notes TEXT NULL, CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL
);
CREATE INDEX IX_SalesInvoiceDeliveries_Status_Driver ON SalesInvoiceDeliveries(Status,DriverId,SalesInvoiceId);
CREATE TRIGGER TR_SalesInvoiceDeliveries_NoDelete BEFORE DELETE ON SalesInvoiceDeliveries
 WHEN EXISTS(SELECT 1 FROM SalesInvoices WHERE Id=OLD.SalesInvoiceId AND Status='posted')
 BEGIN SELECT RAISE(ABORT,'posted_delivery_immutable'); END;
