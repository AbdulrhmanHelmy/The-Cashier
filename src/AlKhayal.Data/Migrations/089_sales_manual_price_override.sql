-- Preserve whether the cashier explicitly overrode the line price in drafts and history.
ALTER TABLE SalesInvoiceLines ADD COLUMN ManualPriceOverride INTEGER NOT NULL DEFAULT 0 CHECK(ManualPriceOverride IN (0,1));
