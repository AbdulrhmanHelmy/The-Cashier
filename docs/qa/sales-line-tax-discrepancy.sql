-- Read-only diagnostic for an unmistakable historical misallocation pattern.
-- A line with no net sale value must not carry positive tax. This query does not
-- attempt to identify every proportional-allocation error.
SELECT si.Id AS SalesInvoiceId,
       si.InvoiceNumber,
       l.Id AS SalesInvoiceLineId,
       l.LineNumber,
       si.TaxIncluded,
       l.InvoiceDiscountScaled,
       l.TaxScaled,
       l.LineTotalScaled
FROM SalesInvoiceLines l
JOIN SalesInvoices si ON si.Id = l.SalesInvoiceId
WHERE si.Status = 'posted'
  AND l.TaxScaled > 0
  AND ((si.TaxIncluded = 0 AND l.LineTotalScaled = l.TaxScaled)
       OR (si.TaxIncluded = 1 AND l.LineTotalScaled = 0))
ORDER BY si.Id, l.LineNumber;
