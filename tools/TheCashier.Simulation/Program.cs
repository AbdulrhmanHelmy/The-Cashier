using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using AlKhayal.Application.Authentication;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Database;
using Dapper;

internal static class SimulationClock
{
    private static int documentId;
    internal static readonly TimeZoneInfo Cairo = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
    internal static DateTime UtcNow { get; private set; } = DateTime.UtcNow;
    internal static DateTime LocalNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, Cairo);
    internal static void Set(DateTime cairoTime) =>
        UtcNow = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(cairoTime, DateTimeKind.Unspecified), Cairo);
    internal static string Stamp => UtcNow.ToString("o", CultureInfo.InvariantCulture);
    internal static Guid NextDocumentId() => new Guid(Interlocked.Increment(ref documentId), 0, 0, new byte[8]);
    internal static void SetDocumentSequence(int value) => documentId = value;
}

internal static class Program
{
    internal const string AdminName = "nakheel-admin";
    internal const long Admin = 1;
    private static readonly string[] Preserved = ("ApplicationSettings PrintingConfiguration LabelConfiguration ScaleBarcodeProfiles " +
        "SchemaVersion TrialState Units ProductAttributes ProductAttributeValues").Split(' ');
    private static readonly string[] Operational = (
        "AlertReadStates BarcodeOperations BarcodeSymbologies BusinessActivities CapabilityConfiguration CashierShiftEvents CashierShifts CatalogAuditLog CatalogOperations " +
        "CompanyProfile CustomerCollectionTreasuryLinks CustomerInstallmentContracts CustomerInstallmentInterestAdjustments CustomerInstallmentPartialPayments CustomerInstallmentPayments CustomerInstallmentReturnAllocations CustomerInstallmentSchedule CustomerPayments CustomerTransactions Customers Drivers " +
        "EmployeeAdvances EmployeeAttendance EmployeePayrollAdvanceAllocations EmployeePayrollPayments Employees ExpenseCategories ExpenseProfitClassifications ExpenseVouchers FeatureAuditLog InstallmentReceivableEntries InventoryAudit InventoryBalances InventoryCostAllocations InventoryCostLayers InventoryCostReconciliations InventoryDocumentLines InventoryDocuments InventoryIdentifiers InventoryPhysicalUnits " +
        "LabelConfigurationAudit MaintenanceTicketReceipts MaintenanceTicketStatusHistory MaintenanceTickets ModuleConfiguration OperationAuditLog PaymentInstrumentAudit PaymentInstruments PrintJobs PrintingAudit ProductAttributeAssignments ProductAttributeValueAssignments ProductBarcodes ProductBatches ProductBundleComponents ProductCapabilityAssignments ProductCategories ProductDetails ProductMigrationIssues ProductPopularity ProductSkus ProductUnits ProductVariantValues ProductVariants Products " +
        "PurchaseInvoiceAudit PurchaseInvoiceLineImeis PurchaseInvoiceLineUnits PurchaseInvoiceLines PurchaseInvoices PurchaseReturnAudit PurchaseReturnLines PurchaseReturns ReturnTreasuryAccounts ReturnTreasuryLinks SalePaymentSelections SalesCancellations SalesDraftDeletions SalesElectronicRefundConfirmations SalesElectronicRefunds SalesInvoiceAudit SalesInvoiceBundleComponentLines SalesInvoiceCorrections SalesInvoiceCostParts SalesInvoiceDeliveries SalesInvoiceLinePhysicalUnits SalesInvoiceLines SalesInvoiceShiftOrigins SalesInvoiceStockEntries SalesInvoices SalesPayments SalesReturnCostAllocations SalesReturnEstimatedCosts SalesReturnLines SalesReturnPhysicalUnits SalesReturns SalesTreasuryReceipts " +
        "Sessions SettingsAuditLog ShiftRevenueTreasury StockMovementUnits StockMovements SupplierInvoices SupplierLedgerEntries SupplierPaymentAllocations SupplierPaymentTreasuryLinks SupplierPayments Suppliers TreasuryAccounts TreasuryAudit TreasuryMovements UserAuditLog UserPosStockPermissions UserSalePricePermissions UserScreenPermissions Users Warehouses WorkforceAudit WorkforceOperations").Split(' ');

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            Run(args);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static readonly string[] Modes = { "verify", "replace", "validate-stage", "publish-stage", "publish-refreshed-stage", "supplement-stage", "supplement-live", "check-reports" };

    private static void Run(string[] args)
    {
        if (args.Length != 5 || !Modes.Contains(args[2]) ||
            !int.TryParse(args[3], out var days) || days < 1 || days > 365 ||
            (args[2] == "replace" || args[2].StartsWith("publish", StringComparison.Ordinal) || args[2] == "supplement-live") && days != 365)
            throw new ArgumentException("Expected DATABASE END-DATE MODE DAYS RUN-DIRECTORY. Replacement requires 365 days.");

        var live = Path.GetFullPath(args[0]);
        var end = DateTime.ParseExact(args[1], "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var start = end.AddDays(1 - days);
        var run = Path.GetFullPath(args[4]);
        if (!File.Exists(live)) throw new FileNotFoundException("Database does not exist", live);
        if (args[2] == "check-reports")
        {
            ServiceChecks.Run(Db.ConnectionStringFor(live) + "FailIfMissing=True;Read Only=True;", start, end);
            return;
        }
        var stage = Path.Combine(run, "staged.db");
        var secretPath = Path.Combine(run, "admin-password.dpapi");
        var baseline = Path.Combine(run, "before-reset.db");
        if (args[2] == "supplement-live")
        {
            var extension = Path.Combine(run, "supplement.db");
            var beforeExtension = Path.Combine(run, "before-supplement.db");
            using (Acquire(live))
            {
                RejectSidecars(live);
                File.Copy(live, beforeExtension);
                File.Copy(beforeExtension, extension);
            }
            VerifyStage(extension, secretPath, start, end, days, supplement: true);
            Publish(live, extension, beforeExtension, secretPath, Fingerprint(beforeExtension));
            return;
        }
        if (args[2].EndsWith("-stage", StringComparison.Ordinal))
        {
            VerifyStage(stage, secretPath, start, end, days, supplement: args[2] == "supplement-stage");
            if (args[2] == "publish-stage")
                Publish(live, stage, baseline, secretPath, Fingerprint(baseline));
            if (args[2] == "publish-refreshed-stage")
            {
                var latest = Path.Combine(run, "before-reset-final.db");
                using (Acquire(live))
                {
                    RejectSidecars(live);
                    File.Copy(live, latest);
                }
                Publish(live, stage, latest, secretPath, Fingerprint(latest));
            }
            return;
        }

        if (Directory.Exists(run)) throw new IOException("Run directory already exists: " + run);
        Directory.CreateDirectory(run);
        string hash;
        using (Acquire(live))
        {
            RejectSidecars(live);
            hash = Fingerprint(live);
            File.Copy(live, baseline);
            File.Copy(baseline, stage);
        }
        Console.WriteLine("Saved an offline recovery copy.");

        var password = new SecureTokenGenerator().CreateToken();
        File.WriteAllBytes(secretPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser));
        var cs = Db.ConnectionStringFor(stage) + "Pooling=True;Synchronous=Off;Journal Mode=Memory;Cache Size=12000;Max Pool Size=8;";
        SimulationClock.Set(start.AddHours(5));
        using (var c = new SQLiteConnection(cs))
        {
            c.Open();
            Reset(c);
            c.Execute("CREATE INDEX simulation_fifo_lookup ON InventoryCostLayers(WarehouseId,VariantId,BatchId)");
            c.Execute("CREATE INDEX simulation_sales_returns ON SalesReturns(SalesInvoiceId)");
            var simulation = new Supermarket(cs, c, start, days);
            simulation.Generate(password);
            new Supplement(cs, c).Run();
            c.Execute("DROP INDEX simulation_fifo_lookup");
            c.Execute("DROP INDEX simulation_sales_returns");
            TestLogin(cs, password);
            Validation.Run(c, days);
            if (days == 365) ServiceChecks.Run(cs, start, end);
        }
        SQLiteConnection.ClearAllPools();
        RejectSidecars(stage);
        if (args[2] == "verify")
        {
            Console.WriteLine("Verified staging database. Live database was left in place.");
            return;
        }
        Publish(live, stage, baseline, secretPath, hash);
    }

    private static void VerifyStage(string stage, string secretPath, DateTime start, DateTime end, int days, bool supplement)
    {
        RejectSidecars(stage);
        var savedPassword = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(secretPath), null, DataProtectionScope.CurrentUser));
        var stageCs = Db.ConnectionStringFor(stage) + "FailIfMissing=True;";
        using (var validation = new SQLiteConnection(stageCs))
        {
            validation.Open();
            TestLogin(stageCs, savedPassword);
            if (supplement) new Supplement(stageCs, validation).Run();
            Validation.Run(validation, days);
            if (days == 365) ServiceChecks.Run(stageCs, start, end);
        }
    }

    private static void Publish(string live, string stage, string baseline, string secretPath, string hash)
    {
        RejectSidecars(stage);
        using (var c = new SQLiteConnection(Db.ConnectionStringFor(stage) + "FailIfMissing=True;Read Only=True;"))
        {
            c.Open();
            c.Execute("ATTACH DATABASE @baseline AS original", new { baseline });
            SameRows(c, "SELECT type,name,tbl_name,sql FROM {0}.sqlite_master WHERE name NOT LIKE 'sqlite_%'", "schema objects");
            SameRows(c, "SELECT * FROM {0}.SchemaVersion", "migration history");
            SameRows(c, "SELECT * FROM {0}.TrialState", "trial state");
            var settings = c.Query<string>("SELECT name FROM pragma_table_info('ApplicationSettings')").Where(name =>
                name != "CurrencyCode" && name != "CurrencySymbol" && name != "ChangedByUserId").Select(Quote);
            SameRows(c, "SELECT " + string.Join(",", settings) + " FROM {0}.ApplicationSettings", "general and tax settings");
            SameRows(c, "SELECT Id,Version,Json,UpdatedUtc FROM {0}.PrintingConfiguration", "printer settings");
            SameRows(c, "SELECT Id,Version,Json,UpdatedUtc FROM {0}.LabelConfiguration", "label configuration");
            SameRows(c, "SELECT * FROM {0}.ScaleBarcodeProfiles", "scale hardware profiles");
        }
        using (Acquire(live))
        {
            RejectSidecars(live);
            if (Fingerprint(live) != hash) throw new IOException("Live database changed after snapshot. Replacement canceled.");
            File.Replace(stage, live, null);
        }
        Console.WriteLine("Replaced database: " + live);
        Console.WriteLine("Recovery copy: " + baseline);
        Console.WriteLine("Administrator password (Windows DPAPI): " + secretPath);
    }

    private static void SameRows(SQLiteConnection c, string select, string name)
    {
        var original = string.Format(CultureInfo.InvariantCulture, select, "original");
        var current = string.Format(CultureInfo.InvariantCulture, select, "main");
        if (c.ExecuteScalar<long>("SELECT EXISTS(" + original + " EXCEPT " + current + ") OR EXISTS(" + current + " EXCEPT " + original + ")") != 0)
            throw new InvalidOperationException("Preservation check failed: " + name);
        Console.WriteLine("Preserved: " + name);
    }

    private static FileStream Acquire(string path) =>
        new FileStream(path + ".host-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static void RejectSidecars(string path)
    {
        if (new[] { "-wal", "-shm", "-journal" }.Any(suffix => File.Exists(path + suffix)))
            throw new IOException("SQLite sidecar present. Close the application cleanly before taking a snapshot: " + path);
    }

    private static string Fingerprint(string file)
    {
        using (var stream = File.OpenRead(file))
        using (var sha = SHA256.Create())
            return Convert.ToBase64String(sha.ComputeHash(stream));
    }

    private static void Reset(SQLiteConnection c)
    {
        var tables = c.Query<string>("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'").ToArray();
        var unclassified = tables.Except(Preserved).Except(Operational).ToArray();
        if (unclassified.Length > 0) throw new InvalidOperationException("Unclassified tables: " + string.Join(", ", unclassified));
        var triggers = c.Query<SchemaTrigger>("SELECT name,sql FROM sqlite_master WHERE type='trigger' ORDER BY name").ToArray();
        c.Execute("PRAGMA foreign_keys=OFF");
        using (var transaction = c.BeginTransaction())
        {
            foreach (var trigger in triggers)
                c.Execute("DROP TRIGGER " + Quote(trigger.Name), transaction: transaction);
            foreach (var table in tables.Intersect(Operational))
            {
                c.Execute("DELETE FROM " + Quote(table), transaction: transaction);
                c.Execute("DELETE FROM sqlite_sequence WHERE name=@table", new { table }, transaction);
            }
            foreach (var table in new[] { "ProductAttributeValues", "ProductAttributes", "Units" })
            {
                var columns = c.Query<string>("SELECT name FROM pragma_table_info(@table)", new { table }, transaction);
                if (columns.Contains("IsSystem"))
                    c.Execute("DELETE FROM " + Quote(table) + " WHERE IsSystem=0", transaction: transaction);
            }
            c.Execute("UPDATE ApplicationSettings SET CurrencyCode='EGP',CurrencySymbol='ج.م',ChangedByUserId=NULL", transaction: transaction);
            c.Execute("UPDATE PrintingConfiguration SET ChangedByUserId=1; UPDATE LabelConfiguration SET ActorId=1", transaction: transaction);
            foreach (var trigger in triggers)
                c.Execute(trigger.Sql, transaction: transaction);
            transaction.Commit();
        }
        c.Execute("PRAGMA foreign_keys=ON");
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static void TestLogin(string cs, string password)
    {
        var auth = new AuthService(new SqliteUserStore(cs), new SqliteSessionStore(cs), new PasswordHasher(),
            new SecureTokenGenerator(), new SystemClock());
        var result = auth.LoginAsync(new LoginCommand { Username = AdminName, Password = password }, CancellationToken.None).GetAwaiter().GetResult();
        if (!result.Succeeded || result.User.Role != "administrator" ||
            auth.GetCurrentUserAsync(result.SessionToken, CancellationToken.None).GetAwaiter().GetResult()?.Id != Admin)
            throw new InvalidOperationException("Administrator authentication or session validation failed.");
        auth.LogoutAsync(result.SessionToken, CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine("Verified administrator authentication and session validation.");
    }

    private sealed class SchemaTrigger
    {
        public string Name { get; set; }
        public string Sql { get; set; }
    }
}
