using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.BusinessActivities;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Delivery;
using AlKhayal.Application.Expenses;
using AlKhayal.Application.Features;
using AlKhayal.Application.Finance;
using AlKhayal.Application.Purchasing;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Suppliers;
using AlKhayal.Application.Treasury;
using AlKhayal.Application.Workforce;
using AlKhayal.Core.Models;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.BusinessActivities;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Delivery;
using AlKhayal.Data.Expenses;
using AlKhayal.Data.Features;
using AlKhayal.Data.Purchasing;
using AlKhayal.Data.Sales;
using AlKhayal.Data.Suppliers;
using AlKhayal.Data.Treasury;
using AlKhayal.Data.Workforce;
using Dapper;

internal sealed partial class Supermarket
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private readonly Random random = new Random(5138);
    private readonly SQLiteConnection db;
    private readonly DateTime start;
    private readonly int days;
    private readonly CatalogService catalog;
    private readonly BusinessActivityService activities;
    private readonly SalesService sales;
    private readonly PurchaseService purchases;
    private readonly SupplierLedgerService supplierLedger;
    private readonly TreasuryService treasury;
    private readonly WorkforceService workforce;
    private readonly ExpenseService expenses;
    private readonly DeliveryService delivery;
    private readonly List<Item> items = new List<Item>();
    private readonly List<CollectionDue> collections = new List<CollectionDue>();
    private readonly List<long> drivers = new List<long>();
    private readonly List<long> employees = new List<long>();
    private readonly List<long> expenseCategories = new List<long>();
    private readonly List<SupplierDue> supplierDues = new List<SupplierDue>();
    private long sequence, cash, warehouse, piece, kilo, carton;
    private int saleCount;

    internal Supermarket(string cs, SQLiteConnection db, DateTime start, int days)
    {
        this.db = db;
        this.start = start;
        this.days = days;
        var activityStore = new SqliteBusinessActivityStore(cs);
        var features = new FeatureService(new SqliteFeatureStore(cs), activityStore);
        activities = new BusinessActivityService(activityStore, features);
        catalog = new CatalogService(new SqliteCatalogStore(cs), new CatalogFeatureReader(features));
        sales = new SalesService(new SqliteSalesStore(cs));
        purchases = new PurchaseService(new SqlitePurchaseRepository(cs), new SqliteSupplierRepository(cs));
        supplierLedger = new SupplierLedgerService(new SqliteSupplierLedgerRepository(cs));
        treasury = new TreasuryService(new SqliteTreasuryStore(cs));
        workforce = new WorkforceService(new SqliteWorkforceStore(cs));
        expenses = new ExpenseService(new SqliteExpenseStore(cs));
        delivery = new DeliveryService(new SqliteDeliveryStore(cs));
    }

    internal void Generate(string password)
    {
        SeedDirectories(password);
        SeedCatalog();
        Console.WriteLine("Created " + items.Count + " products and the store directories.");
        for (var index = 0; index < days; index++)
        {
            var day = start.AddDays(index);
            var date = Day(day);
            var observed = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SimulationClock.Cairo);
            if (day.AddHours(6) > observed) break;
            SimulationClock.Set(day.AddHours(6));
            if (index == 0)
            {
                foreach (var group in items.Where(i => i.StockTarget > 0).GroupBy(i => i.Supplier))
                    Buy(day, group.Key, group.ToList(), true, false);
            }
            else
            {
                foreach (var item in items.Where(item => item.Life > 0))
                    item.Stock = item.Lots.Where(lot => lot.Expires >= day).Sum(lot => lot.Quantity);
                var vendors = 2 + (index % 3 == 0 ? 1 : 0);
                for (var vendor = 0; vendor < vendors; vendor++)
                {
                    var supplier = 1 + ((index * 2 + vendor) % Families.Length);
                    var batch = items.Where(i => i.Supplier == supplier && i.StockTarget > 0 && i.Stock < i.StockTarget * 0.85m &&
                        (i.Life == 0 || i.Variant % 7 == 0 || index % 12 == 0 && i.Variant % 3 == 0)).ToList();
                    if (batch.Count == 0)
                        batch.Add(items.Where(i => i.Supplier == supplier && i.StockTarget > 0).OrderBy(i => i.Stock).First());
                    Buy(day, supplier, batch, false, vendor == 0 && index % 2 == 0);
                }
            }
            foreach (var due in supplierDues.Where(x => x.Day == day))
            {
                if (MainCash() < due.Amount + 200000m)
                {
                    due.Day = day.AddDays(7);
                    continue;
                }
                Wait(supplierLedger.AddTreasuryPaymentAsync(due.Supplier, null, due.Invoice, Amount(due.Amount), date, "cash", "الخزنة الرئيسية", "دفعة على توريد سابق", Key("supplier-payment"), cash, Program.Admin, Ct));
            }

            var weekend = day.DayOfWeek == DayOfWeek.Friday || day.DayOfWeek == DayOfWeek.Saturday;
            var monthPremium = day.Month == 12 || day.Month == 3 ? 8 : 0;
            var daily = random.Next(89, 115) + (weekend ? 19 : 0) + monthPremium + (day.Day < 7 ? 8 : 0);
            for (var shift = 0; shift < 2; shift++)
            {
                var cashier = 2 + (index * 2 + shift) % 9;
                var open = day.AddHours(shift == 0 ? 8 : 15);
                var close = day.AddHours(shift == 0 ? 15 : 23);
                if (close > observed) close = observed.AddSeconds(-5);
                if (close <= open.AddMinutes(2)) break;
                SimulationClock.Set(open);
                var shiftId = Work("shift-open", new WorkforceCommand { CashierId = cashier, Amount = "500" }, cashier);
                var attendance = Work("attendance-in", new WorkforceCommand { EmployeeId = employees[cashier - 1] });
                var count = shift == 0 ? daily * 2 / 5 : daily - daily * 2 / 5;
                for (var order = 0; order < count; order++)
                {
                    SimulationClock.Set(open.AddTicks((close - open).Ticks * (order + 1) / (count + 1)));
                    if (shift == 0 && order == 1)
                        Collect(day, cashier);
                    Sell(day, cashier);
                }
                SimulationClock.Set(close);
                var current = Wait(workforce.ShiftAsync(shiftId, cashier, Ct));
                Work("shift-close", new WorkforceCommand { Id = shiftId, Amount = MoneyPolicy.FormatForUser(current.ExpectedScaled), ExpectedScaled = current.ExpectedScaled }, cashier);
                Work("attendance-out", new WorkforceCommand { Id = attendance });
            }
            if (index % 14 == 0 || index == days - 1)
                Console.WriteLine(date + " | " + saleCount + " sales posted");
            SweepTill(date);
            PostExpenses(day, index);
        }
    }

    private void SeedDirectories(string password)
    {
        var tokens = new SecureTokenGenerator();
        var hasher = new PasswordHasher();
        var jobs = new[] { "مدير المتجر", "رئيس الكاشير", "كاشير", "كاشير", "كاشير", "كاشير", "كاشير", "كاشير", "كاشير", "كاشير", "أمين مخزن", "مسؤول مشتريات", "محاسب", "مندوب توصيل", "مندوب توصيل", "مندوب توصيل" };
        using (var tx = db.BeginTransaction())
        {
            for (var index = 0; index < 10; index++)
            {
                var id = index + 1;
                db.Execute(@"INSERT INTO Users(Id,Username,DisplayName,PasswordHash,Role,CreatedUtc)
                    VALUES(@id,@username,@name,@hash,@role,@stamp)", new
                {
                    id, username = index == 0 ? Program.AdminName : "nakheel-cashier-" + index.ToString("D2"),
                    name = Person(index + 1301), hash = hasher.Hash(index == 0 ? password : tokens.CreateToken()),
                    role = index == 0 ? "administrator" : "cashier", stamp = SimulationClock.Stamp
                }, tx);
                if (index == 0) continue;
                var permissions = new List<string> { "pos", "shift-add", "shifts-list" };
                if (index <= 3) permissions.AddRange(new[] { "sales-management", "returns-overview" });
                foreach (var screen in permissions)
                    db.Execute("INSERT INTO UserScreenPermissions VALUES(@id,@screen,@stamp,1)", new { id, screen, stamp = SimulationClock.Stamp }, tx);
            }
            db.Execute(@"INSERT INTO CompanyProfile(Id,BusinessName,DisplayName,Address,FooterText,UpdatedUtc,ChangedByUserId)
                VALUES(1,'النخبة ماركت','النخبة ماركت','حي مدينة نصر، القاهرة','شكرًا لتسوقكم من النخبة ماركت',@stamp,1);
                INSERT INTO Warehouses(Code,NormalizedCode,Name,Description,Location,IsDefault,CreatedUtc,UpdatedUtc)
                VALUES('MAIN','MAIN','مخزن النخبة الرئيسي','مخزن البيع والتوريد','مدينة نصر',1,@stamp,@stamp),
                      ('BACK','BACK','مخزن احتياطي','مخزن الخدمة الخلفي','مدينة نصر',0,@stamp,@stamp);", new { stamp = SimulationClock.Stamp }, tx);
            for (var i = 0; i < 1250; i++)
            {
                db.Execute(@"INSERT INTO Customers(Code,Name,Phone,Address,CreditLimit,CreatedAt,UpdatedAt)
                    VALUES(@code,@name,@phone,@address,@limit,@stamp,@stamp)", new
                {
                    code = "CU-" + (i + 1).ToString("D5"), name = Person(i), phone = Phone(i), address = Address(i),
                    limit = 0, stamp = SimulationClock.Stamp
                }, tx);
            }
            for (var i = 0; i < Families.Length; i++)
            {
                var family = Families[i].Split('|');
                db.Execute(@"INSERT INTO Suppliers(Code,Name,Contact,Phone,Address,CreatedAt,UpdatedAt)
                    VALUES(@code,@name,@contact,@phone,@address,@stamp,@stamp)", new
                {
                    code = "SU-" + (i + 1).ToString("D3"), name = family[2].Split('/')[i % 6] + " لتوريد " + family[0],
                    contact = Person(i + 1701), phone = Phone(i + 2000), address = Address(i + 1250), stamp = SimulationClock.Stamp
                }, tx);
            }
            tx.Commit();
        }
        var activity = Wait(activities.SaveAsync(new SaveBusinessActivitiesCommand
        {
            ActivityKeys = new[] { "grocery" }, PrimaryActivityKey = "grocery", ChangedByUserId = Program.Admin, ApplyRecommendations = true
        }, Ct));
        Require(activity.Succeeded, activity.ErrorCode, activity.ErrorMessage);
        warehouse = db.ExecuteScalar<long>("SELECT Id FROM Warehouses WHERE IsDefault=1");
        cash = Wait(treasury.SaveAsync(null, new TreasuryAccountCommand { Name = "الخزنة الرئيسية", Code = "MAIN", Type = "cash" }, Program.Admin, Ct)).Id;
        Wait(treasury.SaveAsync(null, new TreasuryAccountCommand { Name = "حساب التسويات البنكية", Code = "BANK", Type = "bank" }, Program.Admin, Ct));
        Wait(treasury.PostAsync(new TreasuryMovementCommand { AccountId = cash, Amount = "6500000", Notes = "رأس المال الافتتاحي", OperationKey = Key("opening") }, "deposit", Program.Admin, Ct));
        for (var i = 0; i < jobs.Length; i++)
            employees.Add(Work("employee-save", new WorkforceCommand
            {
                Name = Person(i + 1301), JobTitle = jobs[i], Phone = Phone(i + 1301), Address = Address(i + 1301),
                HireDate = Day(start), IsActive = true, AttendanceEnabled = true, SalaryFrequency = "monthly",
                Payday = 25, Salary = (i == 0 ? 16000 : 6500 + i * 115).ToString(Invariant), LinkedUserId = i < 10 ? (long?)(i + 1) : null
            }));
        for (var i = 13; i < 16; i++)
            drivers.Add(Wait(delivery.SaveDriverAsync(new Driver { Name = Person(i + 1301), Phone = Phone(i + 1301), IsActive = true }, Ct)).Id);
        foreach (var name in new[] { "نقل وتوزيع", "أكياس ولوازم تعبئة", "نظافة المتجر", "صيانة معدات", "الكهرباء والمياه", "اتصالات وإنترنت", "إيجار المحل" })
            expenseCategories.Add(Wait(expenses.AddCategoryAsync(new ExpenseCategoryCommand { Name = name }, Program.Admin, Ct)).Id);
    }

    private void SeedCatalog()
    {
        piece = db.ExecuteScalar<long>("SELECT Id FROM Units WHERE IsSystem=1 ORDER BY Id LIMIT 1");
        kilo = Unit("KG", "كيلوجرام", "كجم");
        carton = Unit("CASE", "كرتونة", "كرتونة");
        for (var f = 0; f < Families.Length; f++)
        {
            var family = Families[f].Split('|');
            var styles = family[1].Split('/');
            var brands = family[2].Split('/');
            var sizes = family[3].Split('/');
            var price = int.Parse(family[4], Invariant);
            var life = int.Parse(family[5], Invariant);
            var category = Result(Wait(catalog.SaveCategoryAsync(new SaveCategoryCommand { Name = family[0], IsActive = true, SortOrder = f }, Ct))).Id;
            for (var flavor = 0; flavor < styles.Length; flavor++)
            for (var brand = 0; brand < brands.Length; brand++)
            for (var size = 0; size < sizes.Length; size++)
            {
                var index = items.Count + 1;
                var stockTarget = index % 47 == 0 ? 0 : life > 0 ? 9 : index % 6 == 0 ? 85 : size == 0 ? 45 : 24;
                var weight = f == 0 && size == 0;
                var unit = weight ? kilo : piece;
                var bulk = !weight && life == 0 && index % 8 == 0;
                var retail = Math.Round(price * (0.85m + size * 0.63m + brand * 0.055m + flavor * 0.12m) / 0.25m) * 0.25m;
                var cost = Math.Round(retail * (0.64m + random.Next(4, 15) / 100m), 2);
                var barcode = Barcode(index);
                var product = Result(Wait(catalog.CreateProductAsync(new CreateProductCommand
                {
                    Name = styles[flavor] + " " + brands[brand] + " - " + (weight ? "يباع بالكيلو" : sizes[size]),
                    Description = family[0] + "، " + (life > 0 ? "يراجع تاريخ الصلاحية عند الاستلام" : "توريد تجزئة منتظم"),
                    UserId = Program.Admin, OperationKey = Key("catalog"), CategoryId = category, BaseUnitId = unit,
                    ProductType = "stock", SellingType = weight ? "weight" : "piece", IsActive = true,
                    AllowSaleWhenOutOfStock = false, ReorderPoint = weight ? "4" : (index % 13 + 4).ToString(Invariant),
                    SalePrice = Amount(retail), PurchasePrice = Amount(cost),
                    Configuration = new ProductConfigurationCommand
                    {
                        CapabilityKeys = life > 0 ? new[] { "batch-tracking", "expiry-dates" } : null,
                        Units = bulk ? new[] { ProductUnit(unit, 1, true), ProductUnit(carton, 12, false) } : null,
                        Variants = new[] { new ProductVariantCommand
                        {
                            Name = "افتراضي", IsDefault = true, IsActive = true, Sku = "NK-" + index.ToString("D5"),
                            Barcodes = new[] { new ProductBarcodeCommand { Code = barcode, Type = "gtin", IsPrimary = true } }
                        } }
                    }
                }, Ct)));
                var keys = db.QuerySingle<Item>(@"SELECT v.Id Variant,u.Id Unit FROM ProductVariants v JOIN ProductUnits u
                    ON u.ProductId=v.ProductId AND u.IsBase=1 WHERE v.ProductId=@id", new { id = product.Id });
                keys.Supplier = f + 1;
                keys.Retail = retail;
                keys.Cost = cost;
                keys.Weight = weight;
                keys.StockTarget = stockTarget;
                keys.Life = life;
                items.Add(keys);
            }
        }
    }

    private static ProductUnitCommand ProductUnit(long id, long ratio, bool isBase) => new ProductUnitCommand
    {
        UnitId = id, Numerator = ratio, Denominator = 1, IsBase = isBase, IsActive = true, IsSaleUnit = true, IsPurchaseUnit = true
    };

    private long Unit(string code, string name, string symbol) =>
        Result(Wait(catalog.SaveUnitAsync(new SaveUnitCommand { Code = code, Name = name, Symbol = symbol, IsActive = true }, Ct))).Id;

    private void Buy(DateTime day, int supplier, List<Item> batch, bool opening, bool returned)
    {
        var key = Key("purchase");
        var mode = random.Next(4) == 0 ? "cash" : "credit";
        var lines = new List<PurchaseInvoiceLine>();
        foreach (var item in batch)
        {
            var quantity = Math.Max(random.Next(5, 13), item.StockTarget - (int)item.Stock);
            var cost = Math.Round(item.Cost * (1 + (day - start).Days / 4200m + random.Next(-4, 5) / 100m), 2);
            var expiry = item.Life == 0 ? null : Day(day.AddDays(item.Life + random.Next(0, 7)));
            lines.Add(new PurchaseInvoiceLine
            {
                VariantId = item.Variant, ProductUnitId = item.Unit, Quantity = Amount(quantity),
                PurchaseUnitPrice = Amount(cost), SaleUnitPrice = Amount(item.Retail),
                LotNumber = expiry == null ? null : "NK-" + day.ToString("yyMMdd") + "-" + item.Variant,
                ExpirationDate = expiry
            });
            item.Stock += quantity;
            if (expiry != null)
                item.Lots.Add(new Lot { Expires = DateTime.ParseExact(expiry, "yyyy-MM-dd", Invariant), Quantity = quantity });
        }
        var invoiceValue = lines.Sum(line => decimal.Parse(line.Quantity, Invariant) * decimal.Parse(line.PurchaseUnitPrice, Invariant));
        if (mode == "cash" && MainCash() < invoiceValue + 200000m)
            mode = "credit";
        var draft = Wait(purchases.CreateDraftAsync(new PurchaseDraftCommand
        {
            InvoiceNumber = "PO-" + day.ToString("yyMMdd") + "-" + sequence.ToString("D6"), SupplierId = supplier, WarehouseId = warehouse,
            InvoiceDate = Day(day), PaymentMode = mode, TreasuryAccountId = mode == "cash" ? cash : (long?)null,
            Notes = opening ? "توريد التأسيس" : "إعادة توريد حسب حركة البيع", Lines = lines
        }, Program.Admin, Ct));
        var post = Wait(purchases.PostAsync(draft.Id, key, Program.Admin, Ct));
        Require(post.Succeeded, post.ErrorCode, post.ErrorMessage);
        if (returned)
        {
            var line = post.Invoice.Lines[0];
            var result = Wait(purchases.PostReturnAsync(draft.Id, new PurchaseReturnCommand
            {
                WarehouseId = warehouse, ReturnMode = mode == "cash" ? "cash_refund" : "supplier_credit",
                Lines = new List<PurchaseReturnLine> { new PurchaseReturnLine { PurchaseInvoiceLineId = line.Id, Quantity = "1" } }
            }, Key("purchase-return"), Program.Admin, Ct));
            Require(result.Succeeded, result.ErrorCode, result.ErrorMessage);
            batch[0].Stock -= 1;
            if (batch[0].Life > 0)
                batch[0].Lots.Last().Quantity--;
        }
        if (mode == "credit")
        {
            var amount = Math.Round(post.Invoice.TotalScaled / 10000m * (opening ? 0.25m : 0.50m), 2);
            var lag = opening ? random.Next(25, 65) : random.Next(8, 34);
            supplierDues.Add(new SupplierDue { Day = day.AddDays(lag), Supplier = supplier, Invoice = draft.Id, Amount = amount });
        }
    }

    private void Sell(DateTime day, long cashier)
    {
        var basketSize = random.Next(3, 11);
        var credit = random.Next(100) < 10;
        var toDeliver = random.Next(100) < 7;
        long? customer = credit || toDeliver || random.Next(100) < 27
            ? (long?)(1 + random.Next(random.Next(100) < 60 ? 320 : 1250))
            : null;
        var chosen = new Dictionary<Item, decimal>();
        while (chosen.Count < basketSize)
        {
            var index = random.Next(items.Count);
            if (random.Next(100) < 44) index = index / 6 * 6;
            var item = items[index];
            var quantity = item.Weight ? new[] { 0.25m, 0.5m, 0.75m, 1m }[random.Next(4)] : random.Next(100) < 75 ? 1m : random.Next(2, 5);
            if (item.Stock >= quantity && !chosen.ContainsKey(item))
                chosen.Add(item, quantity);
        }
        var draft = Wait(sales.CreateDraftAsync(new SaleDraftCommand
        {
            InvoiceDate = Day(day), CustomerId = customer, WarehouseId = warehouse, PaymentMode = credit ? "credit" : "cash",
            Discount = random.Next(13) == 0 ? "5" : "0",
            Delivery = toDeliver ? new DeliveryDetails { DriverId = drivers[random.Next(drivers.Count)], PrimaryPhone = Phone((int)customer.Value - 1),
                Address = Address((int)customer.Value - 1), Fee = random.Next(2) == 0 ? "18" : "25" } : null,
            Lines = chosen.Select(x => new SaleLine
            {
                VariantId = x.Key.Variant, ProductUnitId = x.Key.Unit, Quantity = Amount(x.Value), UnitPrice = Amount(x.Key.Retail)
            }).ToList()
        }, cashier, Ct));
        var posted = Wait(sales.PostAsync(draft.Id, Key("sale"), cashier, Ct));
        Require(posted.Succeeded, posted.ErrorCode, posted.ErrorMessage);
        saleCount++;
        foreach (var pair in chosen)
        {
            pair.Key.Stock -= pair.Value;
            if (pair.Key.Life == 0) continue;
            var left = pair.Value;
            foreach (var lot in pair.Key.Lots.Where(lot => lot.Expires >= day && lot.Quantity > 0))
            {
                var quantity = Math.Min(lot.Quantity, left);
                lot.Quantity -= quantity;
                left -= quantity;
                if (left == 0) break;
            }
        }
        if (toDeliver)
        {
            Wait(delivery.SetStatusAsync(draft.Id, "out_for_delivery", Ct));
            Wait(delivery.SetStatusAsync(draft.Id, saleCount % 97 == 0 ? "failed" : "delivered", Ct));
        }
        if (!toDeliver && saleCount % 23 == 0 && chosen.Keys.Any(item => item.Life == 0))
        {
            var full = saleCount % 7 == 0 && chosen.Keys.All(item => item.Life == 0);
            var damaged = !full && saleCount % 3 == 0;
            var refundLines = posted.Invoice.Lines.Where(line => chosen.Keys.Any(item => item.Variant == line.VariantId && item.Life == 0))
                .Take(full ? basketSize : saleCount % 5 == 0 ? 2 : 1).ToList();
            var refund = Wait(sales.ReturnAsync(draft.Id, new SaleReturnCommand
            {
                OperationKey = Key("sale-return"), Reason = damaged ? "عبوة تالفة عند الاستلام" : full ? "إلغاء المشتريات بعد الدفع" : "استبدال اختيار غير مناسب",
                Lines = refundLines.Select(line => new SaleReturnLineCommand
                    { InvoiceLineId = line.Id, Quantity = line.Quantity, IsDamaged = damaged }).ToList()
            }, cashier, Ct));
            Require(refund.Succeeded, refund.ErrorCode, refund.ErrorMessage);
            if (!damaged)
                foreach (var line in refundLines)
                    chosen.Keys.Single(x => x.Variant == line.VariantId).Stock += decimal.Parse(line.Quantity, Invariant);
            if (credit && posted.Invoice.TotalScaled > refund.CreditReductionScaled)
                ScheduleCollection(day, draft.Id, posted.Invoice.TotalScaled - refund.CreditReductionScaled);
        }
        else if (credit) ScheduleCollection(day, draft.Id, posted.Invoice.TotalScaled);
    }

    private void ScheduleCollection(DateTime day, long invoice, long amount)
    {
        if (random.Next(100) >= 70) return;
        var share = random.Next(3) == 0 ? amount : amount / 200 * 100;
        if (share > 0)
            collections.Add(new CollectionDue { Invoice = invoice, Day = day.AddDays(random.Next(2, 22)), Amount = share });
    }

    private void Collect(DateTime day, long cashier)
    {
        foreach (var due in collections.Where(x => x.Day == day))
        {
            var result = Wait(sales.AddPaymentAsync(due.Invoice, new SalePaymentCommand
            {
                Amount = MoneyPolicy.FormatForUser(due.Amount), OccurredOn = Day(day), TreasuryAccountId = cash,
                Method = "cash", OperationKey = Key("sale-collection"), Notes = "تحصيل رصيد فاتورة آجلة"
            }, cashier, Ct));
            Require(result.Succeeded, result.ErrorCode, result.ErrorMessage);
        }
    }

    private void PostExpenses(DateTime day, int index)
    {
        var categories = new List<int> { index % 4, (index + 1) % 3 };
        if (day.Day == 7) categories.Add(4);
        if (day.Day == 15) categories.Add(5);
        if (day.Day == 1) categories.Add(6);
        // Use an operating timestamp no later than the last activity already simulated.
        foreach (var category in categories)
            Wait(expenses.PostAsync(new ExpenseCommand
            {
                CategoryId = expenseCategories[category], TreasuryAccountId = cash, OperationKey = Key("expense"),
                Amount = Amount(category == 6 ? 16000 : category == 4 ? random.Next(2100, 3800) : random.Next(75, 660)),
                ExpenseDate = Day(day), Notes = "مصروف تشغيل فرع مدينة نصر"
            }, Program.Admin, Ct));
        if (day.Day == 25)
            foreach (var employee in employees)
                Work("payroll-pay", new WorkforceCommand { EmployeeId = employee, Period = day.ToString("yyyy-MM", Invariant), TreasuryAccountId = cash });
    }

    private void SweepTill(string date)
    {
        var account = db.ExecuteScalar<long?>("SELECT AccountId FROM ShiftRevenueTreasury WHERE Id=1");
        if (!account.HasValue) return;
        var balance = db.ExecuteScalar<long>("SELECT BalanceAfterScaled FROM TreasuryMovements WHERE AccountId=@account ORDER BY Id DESC LIMIT 1", new { account });
        var transfer = balance / 10000000 * 10000000;
        if (transfer <= 0) return;
        var key = Key("till-transfer");
        var notes = "تحويل إيراد اليومية إلى الخزنة الرئيسية " + date;
        var amount = MoneyPolicy.FormatForUser(transfer);
        using (var tx = db.BeginTransaction())
        {
            Wait(SqliteTreasuryStore.PostWithinTransactionAsync(db, tx, new TreasuryMovementCommand
                { AccountId = account.Value, Amount = amount, Notes = notes, OperationKey = key + "-out" },
                "withdraw", transfer, key, Program.Admin, Ct));
            Wait(SqliteTreasuryStore.PostWithinTransactionAsync(db, tx, new TreasuryMovementCommand
                { AccountId = cash, Amount = amount, Notes = notes, OperationKey = key + "-in" },
                "deposit", transfer, key, Program.Admin, Ct));
            tx.Commit();
        }
    }

    private decimal MainCash() => db.ExecuteScalar<long>(
        "SELECT BalanceAfterScaled FROM TreasuryMovements WHERE AccountId=@cash ORDER BY Id DESC LIMIT 1", new { cash }) / 10000m;

    private long Work(string action, WorkforceCommand command, long actor = Program.Admin)
    {
        command.OperationKey = Key(action);
        return Wait(workforce.ExecuteAsync(action, command, actor, actor == Program.Admin, Ct));
    }

    private string Key(string prefix) => "nakheel-" + prefix + "-" + (++sequence).ToString("D8", Invariant);
    private static string Day(DateTime day) => day.ToString("yyyy-MM-dd", Invariant);
    private static string Amount(decimal amount) => amount.ToString("0.##", Invariant);
    private static string Phone(int id) => "000" + (21000000 + id).ToString(Invariant);
    private static string Address(int id) => (1 + id * 7 % 89) + " شارع " + Streets[(id * 13) % Streets.Length] + "، " + Areas[(id / 3) % Areas.Length] + "، القاهرة";
    private static string Person(int id) =>
        FirstNames[id % FirstNames.Length] + " " + Surnames[id / FirstNames.Length % Surnames.Length] + " " +
        Surnames[(id * 7 + 5) % Surnames.Length];
    private static string Barcode(int index)
    {
        var value = "220" + index.ToString("D9", Invariant);
        var sum = value.Select((digit, position) => (digit - '0') * (position % 2 == 0 ? 1 : 3)).Sum();
        return value + (10 - sum % 10) % 10;
    }
    private static T Wait<T>(System.Threading.Tasks.Task<T> task) => task.GetAwaiter().GetResult();
    private static void Wait(System.Threading.Tasks.Task task) => task.GetAwaiter().GetResult();
    private static T Result<T>(CatalogResult<T> result) { Require(result.Succeeded, result.ErrorCode, result.ErrorMessage); return result.Value; }
    private static void Require(bool passed, string code, string message)
    {
        if (!passed) throw new InvalidOperationException(code + " " + message + " at " + SimulationClock.Stamp);
    }
    private sealed class Item
    {
        public long Variant { get; set; }
        public long Unit { get; set; }
        public int Supplier, Life, StockTarget;
        public decimal Retail, Cost, Stock;
        public bool Weight;
        public readonly List<Lot> Lots = new List<Lot>();
    }
    private sealed class Lot { public DateTime Expires; public decimal Quantity; }
    private sealed class CollectionDue { public long Invoice, Amount; public DateTime Day; }
    private sealed class SupplierDue { public long Supplier, Invoice; public decimal Amount; public DateTime Day; }
}
