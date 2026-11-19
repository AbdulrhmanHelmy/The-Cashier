-- Historical contracts remain unlinked. New postings link one contract to one sale.
ALTER TABLE CustomerInstallmentContracts ADD COLUMN SalesInvoiceId INTEGER REFERENCES SalesInvoices(Id) ON DELETE RESTRICT;
CREATE UNIQUE INDEX UX_CustomerInstallmentContracts_SalesInvoice ON CustomerInstallmentContracts(SalesInvoiceId) WHERE SalesInvoiceId IS NOT NULL;
