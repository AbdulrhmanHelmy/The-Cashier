-- Read-only diagnostic. Run only against a copy or a read-only SQLite connection.
-- Detects historical return quantities or supplier credits that exceed the posted source line.
WITH posted_returns AS (
    SELECT rl.PurchaseInvoiceLineId AS LineId,
           COUNT(*) AS ReturnCount,
           SUM(rl.QuantityScaled) AS ReturnedQuantityScaled,
           SUM(rl.AccountAmountScaled) AS CreditedScaled
    FROM PurchaseReturnLines rl
    JOIN PurchaseReturns pr ON pr.Id = rl.PurchaseReturnId
    WHERE pr.Status = 'posted'
    GROUP BY rl.PurchaseInvoiceLineId
)
SELECT p.Id AS PurchaseInvoiceId,
       p.InvoiceNumber,
       l.Id AS PurchaseInvoiceLineId,
       l.QuantityScaled AS PurchasedQuantityScaled,
       l.LineTotalScaled AS OriginalLineTotalScaled,
       r.ReturnCount,
       r.ReturnedQuantityScaled,
       r.CreditedScaled,
       r.CreditedScaled - l.LineTotalScaled AS ExcessCreditScaled,
       CASE WHEN r.ReturnedQuantityScaled > l.QuantityScaled THEN 'quantity_exceeded'
            WHEN r.CreditedScaled > l.LineTotalScaled THEN 'overcredited'
            WHEN r.ReturnedQuantityScaled = l.QuantityScaled AND r.CreditedScaled <> l.LineTotalScaled THEN 'full_return_mismatch'
       END AS Discrepancy
FROM posted_returns r
JOIN PurchaseInvoiceLines l ON l.Id = r.LineId
JOIN PurchaseInvoices p ON p.Id = l.PurchaseInvoiceId
WHERE r.ReturnedQuantityScaled > l.QuantityScaled
   OR r.CreditedScaled > l.LineTotalScaled
   OR (r.ReturnedQuantityScaled = l.QuantityScaled AND r.CreditedScaled <> l.LineTotalScaled)
ORDER BY p.Id, l.Id;
