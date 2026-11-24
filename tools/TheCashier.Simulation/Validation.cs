using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Dapper;

internal static class Validation
{
    internal static void Run(SQLiteConnection db, int days)
    {
        Check(db.Query<string>("PRAGMA integrity_check").SequenceEqual(new[] { "ok" }), "SQLite integrity");
        Check(!db.Query("PRAGMA foreign_key_check").Any(), "foreign keys");
        Check(db.ExecuteScalar<long>("SELECT COUNT(*) FROM Products") >= 5000, "product count");
        Check(db.ExecuteScalar<long>(@"SELECT COUNT(*) FROM ProductBarcodes WHERE RetiredUtc IS NULL") >= 5000, "barcode coverage");
        Valid(db, "sale totals including delivery fees", @"SELECT COUNT(*) FROM SalesInvoices i LEFT JOIN SalesInvoiceDeliveries d ON d.SalesInvoiceId=i.Id
            WHERE i.TotalScaled<>i.SubtotalScaled-i.DiscountScaled+(1-i.TaxIncluded)*i.TaxScaled+COALESCE(d.FeeScaled,0)
            OR i.TotalScaled-COALESCE(d.FeeScaled,0)<>(SELECT SUM(l.LineTotalScaled) FROM SalesInvoiceLines l WHERE l.SalesInvoiceId=i.Id)");
        Valid(db, "purchase totals", @"SELECT COUNT(*) FROM PurchaseInvoices p WHERE p.TotalScaled<>(SELECT SUM(l.LineTotalScaled) FROM PurchaseInvoiceLines l WHERE l.PurchaseInvoiceId=p.Id)");
        Valid(db, "stock movement balances", @"WITH movements AS (SELECT WarehouseId,VariantId,COALESCE(BatchId,0) BatchKey,SUM(QuantityBaseScaled) Quantity
            FROM StockMovements GROUP BY 1,2,3) SELECT COUNT(*) FROM movements m LEFT JOIN InventoryBalances b
            ON b.WarehouseId=m.WarehouseId AND b.VariantId=m.VariantId AND COALESCE(b.BatchId,0)=m.BatchKey
            WHERE COALESCE(b.QuantityBaseScaled,0)<>m.Quantity");
        Valid(db, "sale return quantities", @"SELECT COUNT(*) FROM (SELECT l.Id FROM SalesInvoiceLines l JOIN SalesReturnLines r ON r.SalesInvoiceLineId=l.Id
            GROUP BY l.Id HAVING SUM(r.QuantityScaled)>l.QuantityScaled)");
        Valid(db, "purchase return quantities", @"SELECT COUNT(*) FROM (SELECT l.Id FROM PurchaseInvoiceLines l JOIN PurchaseReturnLines r ON r.PurchaseInvoiceLineId=l.Id
            GROUP BY l.Id HAVING SUM(r.QuantityScaled)>l.QuantityScaled)");
        Valid(db, "sale return source, total and chronology", @"SELECT COUNT(*) FROM SalesReturns r JOIN SalesInvoices i ON i.Id=r.SalesInvoiceId
            WHERE r.CreatedUtc<i.PostedUtc OR r.TotalScaled<>(SELECT SUM(l.AmountScaled) FROM SalesReturnLines l WHERE l.SalesReturnId=r.Id)
            OR EXISTS (SELECT 1 FROM SalesReturnLines l JOIN SalesInvoiceLines ol ON ol.Id=l.SalesInvoiceLineId WHERE l.SalesReturnId=r.Id AND ol.SalesInvoiceId<>i.Id)");
        Valid(db, "purchase return source, total and chronology", @"SELECT COUNT(*) FROM PurchaseReturns r JOIN PurchaseInvoices i ON i.Id=r.PurchaseInvoiceId
            WHERE r.CreatedUtc<i.PostedUtc OR r.TotalScaled<>(SELECT SUM(l.AccountAmountScaled) FROM PurchaseReturnLines l WHERE l.PurchaseReturnId=r.Id)
            OR EXISTS (SELECT 1 FROM PurchaseReturnLines l JOIN PurchaseInvoiceLines ol ON ol.Id=l.PurchaseInvoiceLineId WHERE l.PurchaseReturnId=r.Id AND ol.PurchaseInvoiceId<>i.Id)");
        Valid(db, "FIFO cost layers", @"WITH layers AS (SELECT WarehouseId,VariantId,COALESCE(BatchId,0) BatchKey,SUM(RemainingQuantityScaled) Quantity,SUM(RemainingValueScaled) Value
            FROM InventoryCostLayers GROUP BY 1,2,3) SELECT COUNT(*) FROM InventoryBalances b LEFT JOIN layers l ON l.WarehouseId=b.WarehouseId
            AND l.VariantId=b.VariantId AND l.BatchKey=COALESCE(b.BatchId,0)
            WHERE b.QuantityBaseScaled<>COALESCE(l.Quantity,0) OR b.ValueScaled<>COALESCE(l.Value,0)");
        Valid(db, "customer receivables", @"SELECT COUNT(*) FROM Customers c WHERE ROUND(c.Balance*10000) <>
            COALESCE((SELECT SUM(CASE WHEN t.TransactionType IN ('opening','increase') THEN t.AmountScaled ELSE -t.AmountScaled END)
            FROM CustomerTransactions t WHERE t.CustomerId=c.Id),0)");
        Valid(db, "supplier payables", @"SELECT COUNT(*) FROM Suppliers s WHERE ROUND((s.Payable-s.SupplierCredit)*10000) <>
            COALESCE((SELECT SUM(e.AmountScaled) FROM SupplierLedgerEntries e WHERE e.SupplierId=s.Id),0)");
        Valid(db, "supplier payment applications", @"SELECT COUNT(*) FROM SupplierLedgerEntries e
            JOIN SupplierPaymentTreasuryLinks l ON l.SupplierLedgerEntryId=e.Id WHERE e.EntryType='payment' AND
            -e.AmountScaled<>COALESCE((SELECT SUM(a.AmountScaled) FROM SupplierPaymentAllocations a WHERE a.SupplierLedgerEntryId=e.Id),0)");
        Valid(db, "purchase cash settlements", @"SELECT COUNT(*) FROM PurchaseInvoices p LEFT JOIN TreasuryMovements m ON m.Id=p.TreasuryMovementId
            WHERE p.Status='posted' AND p.PaymentMode='cash' AND (m.Id IS NULL OR m.Type<>'withdraw' OR m.AmountScaled<>p.TotalScaled)");
        Valid(db, "sales payment posting", @"SELECT COUNT(*) FROM SalesInvoices i WHERE i.PaidScaled<>
            COALESCE((SELECT SUM(CASE WHEN p.EventType='refund' THEN -p.AmountScaled ELSE p.AmountScaled END)
            FROM SalesPayments p WHERE p.SalesInvoiceId=i.Id AND p.PaymentMethod<>'credit_adjustment'),0)");
        Valid(db, "treasury running balances", @"SELECT COUNT(*) FROM (SELECT BalanceAfterScaled,
            SUM(CASE WHEN Type='deposit' THEN AmountScaled ELSE -AmountScaled END) OVER (PARTITION BY AccountId ORDER BY Id) Running
            FROM TreasuryMovements) WHERE BalanceAfterScaled<>Running");
        Valid(db, "posted sales with inventory and shift", @"SELECT COUNT(*) FROM SalesInvoices i
            LEFT JOIN SalesInvoiceShiftOrigins o ON o.SalesInvoiceId=i.Id LEFT JOIN CashierShifts s ON s.Id=o.ShiftId
            WHERE i.Status='posted' AND (i.InventoryDocumentId IS NULL OR s.Id IS NULL OR i.PostedUtc<s.OpenedUtc OR i.PostedUtc>s.ClosedUtc)");
        Valid(db, "posted purchases with inventory", "SELECT COUNT(*) FROM PurchaseInvoices WHERE Status='posted' AND InventoryDocumentId IS NULL");
        Valid(db, "unclosed or unbalanced drawers", "SELECT COUNT(*) FROM CashierShifts WHERE ClosedUtc IS NULL OR ClosingExpectedScaled<>CountedScaled");
        if (days == 365)
        {
            Check(db.ExecuteScalar<long>("SELECT COUNT(DISTINCT InvoiceDate) FROM SalesInvoices WHERE Status='posted'") >= 364, "daily coverage through the last trading day");
            Check(db.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesInvoices WHERE Status='posted'") >= 35000, "annual sales volume");
            Check(db.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesInvoiceLines") >= 200000, "annual sale line volume");
            Check(db.ExecuteScalar<long>("SELECT COUNT(*) FROM PurchaseInvoices WHERE Status='posted'") >= 800, "annual purchase volume");
            Check(db.ExecuteScalar<long>("SELECT COUNT(*) FROM SalesReturns") >= 1000, "annual sales return volume");
            Check(db.ExecuteScalar<long>("SELECT COUNT(*) FROM PurchaseReturns") >= 150, "annual purchase return volume");
        }
        foreach (var table in ("Products ProductCategories Customers Suppliers Employees Users SalesInvoices SalesInvoiceLines PurchaseInvoices PurchaseInvoiceLines " +
            "SalesReturns PurchaseReturns InventoryBalances StockMovements ProductBatches SalesPayments CustomerTransactions SupplierPaymentAllocations CashierShifts " +
            "SalesInvoiceDeliveries ExpenseVouchers EmployeeAttendance EmployeePayrollPayments TreasuryMovements").Split(' '))
            Console.WriteLine(table + ": " + db.ExecuteScalar<long>("SELECT count(*) FROM " + table));
        foreach (var query in new[]
        {
            "SELECT MIN(InvoiceDate)||' through '||MAX(InvoiceDate) FROM SalesInvoices",
            "SELECT 'Sales (EGP): '||printf('%.2f',SUM(TotalScaled)/10000.0) FROM SalesInvoices",
            "SELECT 'Purchases (EGP): '||printf('%.2f',SUM(TotalScaled)/10000.0) FROM PurchaseInvoices",
            "SELECT 'Sales returns (EGP): '||printf('%.2f',SUM(TotalScaled)/10000.0) FROM SalesReturns",
            "SELECT 'Purchase returns (EGP): '||printf('%.2f',SUM(TotalScaled)/10000.0) FROM PurchaseReturns",
            "SELECT 'Expenses (EGP): '||printf('%.2f',SUM(AmountScaled)/10000.0) FROM ExpenseVouchers",
            "SELECT 'Stock valuation (EGP): '||printf('%.2f',SUM(ValueScaled)/10000.0) FROM InventoryBalances",
            "SELECT 'Customer receivables (EGP): '||printf('%.2f',SUM(Balance)) FROM Customers",
            "SELECT 'Supplier payables (EGP): '||printf('%.2f',SUM(Payable)) FROM Suppliers"
        })
            Console.WriteLine(db.ExecuteScalar<string>(query));
        Measure(db, "catalog Arabic search", "SELECT Id,Name FROM Products WHERE Name LIKE '%أرز%' LIMIT 50");
        Measure(db, "barcode index", "SELECT Id FROM ProductBarcodes WHERE NormalizedBarcode=(SELECT NormalizedBarcode FROM ProductBarcodes LIMIT 1)");
        Measure(db, "recent sales", "SELECT Id,InvoiceDate,TotalScaled FROM SalesInvoices ORDER BY InvoiceDate DESC,Id DESC LIMIT 50");
        Measure(db, "monthly sales summary", "SELECT substr(InvoiceDate,1,7),SUM(TotalScaled) FROM SalesInvoices WHERE Status='posted' GROUP BY 1");
    }

    private static void Valid(SQLiteConnection db, string name, string query) =>
        Check(db.ExecuteScalar<long>(query) == 0, name);

    private static void Check(bool valid, string name)
    {
        if (!valid) throw new InvalidOperationException("Validation failed: " + name);
        Console.WriteLine("Passed: " + name);
    }

    private static void Measure(SQLiteConnection db, string name, string query)
    {
        var time = Stopwatch.StartNew();
        var rows = db.Query(query).Count();
        Console.WriteLine(name + " | " + rows + " rows | " + time.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms");
    }
}
