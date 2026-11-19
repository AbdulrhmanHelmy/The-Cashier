-- Read-only diagnostic after migration 086. Historical rows are deliberately reported.
-- Review each row's provenance before deciding whether it represents missing funding.
SELECT t.Id AS CustomerTransactionId,t.CustomerId,t.OperationKey,t.Reference,
       t.SalesInvoiceId,t.TransactionType,t.AmountScaled,t.OccurredOn,
       l.TreasuryMovementId,l.TreasuryAccountId,l.PaymentMethod,
       m.Type AS TreasuryMovementType,m.AmountScaled AS TreasuryAmountScaled,
       m.AccountId AS MovementAccountId,
       CASE WHEN l.CustomerTransactionId IS NULL THEN 'unlinked_collection'
            WHEN m.Id IS NULL THEN 'missing_movement'
            WHEN m.Type <> 'deposit' OR m.AmountScaled <> t.AmountScaled
                 OR m.AccountId <> l.TreasuryAccountId THEN 'mismatch'
            ELSE 'matched' END AS Diagnostic
FROM CustomerTransactions t
LEFT JOIN CustomerCollectionTreasuryLinks l ON l.CustomerTransactionId=t.Id
LEFT JOIN TreasuryMovements m ON m.Id=l.TreasuryMovementId
WHERE t.TransactionType IN ('collection','legacy_payment')
  AND (l.CustomerTransactionId IS NULL OR m.Id IS NULL OR m.Type <> 'deposit'
       OR m.AmountScaled <> t.AmountScaled OR m.AccountId <> l.TreasuryAccountId)
ORDER BY t.Id;
