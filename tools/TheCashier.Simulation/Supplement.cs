using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Features;
using AlKhayal.Application.Finance;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Maintenance;
using AlKhayal.Application.Payments;
using AlKhayal.Application.Reports;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Workforce;
using AlKhayal.Data.BusinessActivities;
using AlKhayal.Data.Features;
using AlKhayal.Data.Inventory;
using AlKhayal.Data.Maintenance;
using AlKhayal.Data.Payments;
using AlKhayal.Data.Reports;
using AlKhayal.Data.Sales;
using AlKhayal.Data.Workforce;
using Dapper;

internal sealed class Supplement
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private const long Admin = Program.Admin;
    private readonly string connectionString;
    private readonly SQLiteConnection db;
    private readonly FeatureService features;
    private readonly InventoryService inventory;
    private readonly WorkforceService workforce;
    private readonly SalesService sales;
    private readonly long warehouse, cash;
    private readonly DateTime day;
    private int sequence;

    internal Supplement(string cs, SQLiteConnection db)
    {
        connectionString = cs;
        this.db = db;
        features = new FeatureService(new SqliteFeatureStore(cs), new SqliteBusinessActivityStore(cs));
        inventory = new InventoryService(new SqliteInventoryStore(cs), features);
        workforce = new WorkforceService(new SqliteWorkforceStore(cs));
        sales = new SalesService(new SqliteSalesStore(cs));
        warehouse = db.ExecuteScalar<long>("SELECT Id FROM Warehouses WHERE IsDefault=1");
        cash = db.ExecuteScalar<long>("SELECT Id FROM TreasuryAccounts WHERE Code='MAIN'");
        var last = db.ExecuteScalar<string>("SELECT MAX(ClosedUtc) FROM CashierShifts");
        var utc = DateTime.Parse(last, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, SimulationClock.Cairo);
        day = local.Date;
        SimulationClock.Set(local.AddSeconds(2));
        // Document suffixes in this process must not collide with those from the annual run.
        SimulationClock.SetDocumentSequence(1000000);
    }

    internal void Run()
    {
        if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM WorkforceOperations WHERE OperationKey LIKE 'nakheel-close-of-year-%'") != 0)
            throw new InvalidOperationException("The extra workflow examples already exist.");
        Enable("multiple-warehouses");
        Enable("maintenance-management");
        Enable("installments");
        RecordStockWork();
        RecordMaintenance();
        RecordCheckoutExamples();
        ClassifyExpenses();
        Console.WriteLine("Posted stock transfers, counts and spoilage, maintenance, installment and electronic sales, staff advances, and expense classifications.");
    }

    private void Enable(string key)
    {
        var result = Wait(features.SetAsync(new SetFeatureCommand { Key = key, Kind = FeatureKinds.Module, IsEnabled = true, ChangedByUserId = Admin }, Ct));
        Require(result.Succeeded, result.ErrorCode, result.ErrorMessage);
    }

    private void RecordStockWork()
    {
        var products = db.Query<StockItem>(@"SELECT b.VariantId,u.Id UnitId
            FROM InventoryBalances b JOIN ProductVariants v ON v.Id=b.VariantId JOIN ProductUnits u ON u.ProductId=v.ProductId AND u.IsBase=1
            JOIN ProductDetails d ON d.ProductId=v.ProductId JOIN ProductCategories cat ON cat.Id=d.CategoryId
            WHERE b.WarehouseId=@warehouse AND b.BatchId IS NULL AND b.QuantityBaseScaled>=8000000 AND d.SellingType='piece'
            ORDER BY b.VariantId LIMIT 15", new { warehouse }).ToList();
        var back = db.ExecuteScalar<long>("SELECT Id FROM Warehouses WHERE Code='BACK'");
        var transfer = Wait(inventory.PostAsync(new InventoryPostCommand
        {
            OperationKey = Key("transfer"), Type = "transfer", WarehouseId = warehouse, DestinationWarehouseId = back, UserId = Admin,
            Reason = "نقل احتياطي بعد جرد الرفوف",
            Lines = products.Take(6).Select(item => new InventoryLineInput { VariantId = item.VariantId, ProductUnitId = item.UnitId, Quantity = "3" }).ToArray()
        }, Ct));
        Require(transfer.Succeeded, transfer.ErrorCode, transfer.ErrorMessage);
        var count = Wait(inventory.CreateCountAsync(warehouse, Admin, products.Skip(6).Select(item => item.VariantId).ToArray(), Ct));
        Require(count.Succeeded, count.ErrorCode, count.ErrorMessage);
        var entries = count.Value.Lines.ToArray();
        for (var i = 0; i < entries.Length; i++)
            entries[i].CountedQuantity = ((entries[i].RecordedQuantityScaled - (i % 4 == 0 ? 1000000 : 0)) / 1000000m).ToString("0.######", CultureInfo.InvariantCulture);
        var posted = Wait(inventory.PostCountAsync(count.Value.Id, new InventoryCountPostCommand
        {
            OperationKey = Key("stock-count"), UserId = Admin, Reason = "جرد دوري للرفوف وفروقات التعبئة", Lines = entries
        }, Ct));
        Require(posted.Succeeded, posted.ErrorCode, posted.ErrorMessage);
        var expired = db.Query<StockItem>(@"SELECT b.VariantId,u.Id UnitId,p.LotNumber
            FROM InventoryBalances b JOIN ProductBatches p ON p.Id=b.BatchId JOIN ProductVariants v ON v.Id=b.VariantId
            JOIN ProductUnits u ON u.ProductId=v.ProductId AND u.IsBase=1
            WHERE b.WarehouseId=@warehouse AND b.QuantityBaseScaled>=1000000 AND p.ExpirationDate<@date ORDER BY p.ExpirationDate,p.Id LIMIT 12",
            new { warehouse, date = Date(day) }).ToArray();
        if (expired.Length == 0) return;
        var spoilage = Wait(inventory.PostAsync(new InventoryPostCommand
        {
            OperationKey = Key("spoilage"), Type = "adjustment", WarehouseId = warehouse, UserId = Admin,
            Reason = "إتلاف عينات تالفة من تشغيلات منتهية",
            Lines = expired.Select(item => new InventoryLineInput
                { VariantId = item.VariantId, ProductUnitId = item.UnitId, LotNumber = item.LotNumber, Quantity = "-1" }).ToArray()
        }, Ct));
        Require(spoilage.Succeeded, spoilage.ErrorCode, spoilage.ErrorMessage);
    }

    private void RecordMaintenance()
    {
        var maintenance = new MaintenanceService(new SqliteMaintenanceRepository(connectionString));
        var devices = new[] { "ميزان مطبخ رقمي", "خلاط يدوي", "غلاية كهربائية", "مصباح طوارئ", "ميزان مطبخ رقمي", "مضرب كهربائي" };
        var faults = new[] { "قراءة الوزن غير مستقرة", "المحرك يتوقف عند التشغيل", "المفتاح لا يثبت", "البطارية لا تشحن", "ظهور رسالة خطأ", "اهتزاز غير معتاد" };
        for (var i = 0; i < devices.Length; i++)
        {
            var customer = i + 61;
            var phone = db.ExecuteScalar<string>("SELECT Phone FROM Customers WHERE Id=@customer", new { customer });
            var ticket = maintenance.Create(new MaintenanceTicketCommand
            {
                CustomerId = customer, Phone = phone, Device = devices[i], Model = "NK-" + (110 + i),
                Issue = faults[i], EstimatedCost = 185 + i * 40, AdvancePaid = 0, Technician = "فني خدمة الأجهزة",
                ReceivedOn = Date(day), ExpectedOn = Date(day.AddDays(2 + i % 4)), OperationKey = Key("repair-intake")
            });
            if (i % 2 == 0) maintenance.UpdateStatus(ticket.Id, "قيد الفحص", Key("repair-status"));
            if (i == 1)
            {
                maintenance.UpdateStatus(ticket.Id, "جاهز للتسليم", Key("repair-status"));
                maintenance.Deliver(ticket.Id, 225m, "cash", Date(day), Key("repair-delivery"), cash, Admin);
            }
        }
    }

    private void RecordCheckoutExamples()
    {
        var instrumentService = new PaymentInstrumentService(new SqlitePaymentInstrumentStore(connectionString));
        var instruments = new[]
        {
            Wait(instrumentService.SaveAsync(new PaymentInstrument { Name = "بطاقات فرع مدينة نصر", Number = "NK-CARD-01", Type = "card", Provider = "مزود تجريبي" }, Admin, Ct)),
            Wait(instrumentService.SaveAsync(new PaymentInstrument { Name = "محفظة فرع مدينة نصر", Number = "00000000001", Type = "wallet", Provider = "مزود تجريبي" }, Admin, Ct))
        };
        var products = db.Query<StockItem>(@"SELECT b.VariantId,u.Id UnitId,d.SellPriceScaled Price
            FROM InventoryBalances b JOIN ProductVariants v ON v.Id=b.VariantId JOIN ProductUnits u ON u.ProductId=v.ProductId AND u.IsBase=1
            JOIN ProductDetails d ON d.ProductId=v.ProductId JOIN ProductCategories cat ON cat.Id=d.CategoryId
            WHERE b.WarehouseId=@warehouse AND b.BatchId IS NULL AND b.QuantityBaseScaled>=8000000 AND d.SellingType='piece'
            AND cat.Name IN ('ملحقات إلكترونية','أدوات منزلية') AND d.SellPriceScaled>=650000 ORDER BY b.VariantId LIMIT 16",
            new { warehouse }).ToArray();
        var cashier = 2L;
        var shift = Work("shift-open", new WorkforceCommand { CashierId = cashier, Amount = "500" }, cashier);
        for (var i = 0; i < products.Length; i++)
        {
            var product = products[i];
            var credit = i >= 10;
            var command = new SaleDraftCommand
            {
                WarehouseId = warehouse, CustomerId = 51 + i, InvoiceDate = Date(day), PaymentMode = credit ? "credit" : "cash",
                Lines = new List<SaleLine> { new SaleLine { VariantId = product.VariantId, ProductUnitId = product.UnitId,
                    Quantity = "2", UnitPrice = MoneyPolicy.FormatForUser(product.Price) } }
            };
            if (credit)
            {
                var installment = Wait(sales.CreateInstallmentAsync(new InstallmentSaleCommand
                {
                    Invoice = command, DownPayment = "50", InstallmentCount = 3, IntervalMonths = 1, FirstDueOn = Date(day.AddMonths(1)),
                    InterestRate = i % 2 == 0 ? "0" : "5", InterestReturnPolicy = "keep", TreasuryAccountId = cash,
                    OperationKey = Key("installment")
                }, cashier, Ct));
                Require(installment.Succeeded, installment.ErrorCode, installment.ErrorMessage);
            }
            else
            {
                command.ImmediateMethod = instruments[i % 2].Type;
                command.PaymentInstrumentId = instruments[i % 2].Id;
                var draft = Wait(sales.CreateDraftAsync(command, cashier, Ct));
                var result = Wait(sales.PostAsync(draft.Id, Key("electronic-sale"), cashier, Ct));
                Require(result.Succeeded, result.ErrorCode, result.ErrorMessage);
            }
        }
        var current = Wait(workforce.ShiftAsync(shift, cashier, Ct));
        Work("shift-close", new WorkforceCommand { Id = shift, Amount = MoneyPolicy.FormatForUser(current.ExpectedScaled), ExpectedScaled = current.ExpectedScaled }, cashier);
        foreach (var employee in db.Query<long>("SELECT Id FROM Employees ORDER BY Id DESC LIMIT 3"))
            Work("advance-add", new WorkforceCommand { EmployeeId = employee, Amount = "600", TreasuryAccountId = cash, Notes = "سلفة معتمدة تخصم من الراتب القادم" });
    }

    private void ClassifyExpenses()
    {
        var reports = new NetProfitService(new SqliteNetProfitStore(connectionString), SimulationClock.Cairo);
        foreach (var id in db.Query<long>("SELECT Id FROM ExpenseVouchers ORDER BY Id"))
            reports.ClassifyAsync(id, new ExpenseClassificationCommand
            {
                Classification = "included", Reason = "مصروف تشغيل موثق لفرع مدينة نصر", OperationKey = Key("expense-classification")
            }, Admin, Ct).GetAwaiter().GetResult();
    }

    private long Work(string action, WorkforceCommand command, long actor = Admin)
    {
        command.OperationKey = Key(action);
        return Wait(workforce.ExecuteAsync(action, command, actor, actor == Admin, Ct));
    }

    private string Key(string kind) => "nakheel-close-of-year-" + kind + "-" + (++sequence).ToString("D4", CultureInfo.InvariantCulture);
    private static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();
    private static string Date(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static void Require(bool success, string code, string message)
    {
        if (!success) throw new InvalidOperationException(code + " " + message);
    }
    private sealed class StockItem
    {
        public long VariantId { get; set; }
        public long UnitId { get; set; }
        public long Price { get; set; }
        public string LotNumber { get; set; }
    }
}
