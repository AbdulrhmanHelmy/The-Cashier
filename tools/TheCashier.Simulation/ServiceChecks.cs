using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Customers;
using AlKhayal.Application.Delivery;
using AlKhayal.Application.Reports;
using AlKhayal.Application.Suppliers;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Customers;
using AlKhayal.Data.Delivery;
using AlKhayal.Data.Reports;
using AlKhayal.Data.Suppliers;

internal static class ServiceChecks
{
    internal static void Run(string cs, DateTime from, DateTime to)
    {
        var ct = CancellationToken.None;
        var zone = SimulationClock.Cairo;
        var catalog = new CatalogService(new SqliteCatalogStore(cs));
        var sales = new SalesReportService(new SqliteSalesReportStore(cs), zone);
        var transactions = new TransactionReportService(new SqliteTransactionReportStore(cs), zone);
        var netProfit = new NetProfitService(new SqliteNetProfitStore(cs), zone);
        var stock = new StockReportService(new SqliteStockReportStore(cs), zone);
        var operational = new OperationalReportService(new SqliteOperationalReportStore(cs), zone);
        var delivery = new DeliveryService(new SqliteDeliveryStore(cs));
        var customers = new CustomerService(new SqliteCustomerRepository(cs));
        var suppliers = new SupplierService(new SqliteSupplierRepository(cs));
        var overview = new BusinessOverviewService(sales, transactions, netProfit, operational);
        var start = Day(from);
        var end = Day(to);
        var last = Day(to.AddDays(-1));
        var lastTradingDay = to.AddDays(-1);
        var month = Day(new DateTime(lastTradingDay.Year, lastTradingDay.Month, 1));

        Time("Catalog page 2", () => catalog.ListProductsAsync(new ProductQuery { Page = 2, PageSize = 25 }, ct).GetAwaiter().GetResult().Value.Items.Count);
        Time("Catalog Arabic search", () => catalog.ListProductsAsync(new ProductQuery { Search = "أرز" }, ct).GetAwaiter().GetResult().Value.Items.Count);
        Time("Catalog category filter", () => catalog.ListProductsAsync(new ProductQuery { CategoryId = 1 }, ct).GetAwaiter().GetResult().Value.Items.Count);
        Time("Barcode lookup", () => catalog.LookupBarcodeAsync("2200000000019", ct).GetAwaiter().GetResult().Succeeded ? 1 : 0);
        Time("Customer search", () => customers.SearchAsync("CU-00001", ct).GetAwaiter().GetResult().Count);
        Time("Supplier directory", () => suppliers.GetAll().Count);
        Time("Daily sales", () => sales.QueryAsync(new SalesReportQuery { From = last, To = last }, ct).GetAwaiter().GetResult().Count);
        Time("Sales history page 2", () => sales.QueryAsync(new SalesReportQuery { From = start, To = end, Page = 2 }, ct).GetAwaiter().GetResult().Items.Count);
        Time("Purchase history", () => transactions.PurchasesAsync(new TransactionReportQuery { From = start, To = end }, ct).GetAwaiter().GetResult().Count);
        Time("Return history", () => transactions.ReturnsAsync(new TransactionReportQuery { From = start, To = end }, ct).GetAwaiter().GetResult().Count);
        Time("Financial overview", () => overview.QueryAsync(new ReportQuery { From = month, To = end },
            new HashSet<string> { "sales", "purchases", "returns", "net-profit", "payment-methods", "customers", "suppliers" }, ct).GetAwaiter().GetResult().Count);
        Time("Monthly financial report", () => ProfitEvents(netProfit, month, end, "daily", ct));
        Time("Annual financial report", () => ProfitEvents(netProfit, start, end, "monthly", ct));
        Time("Product profitability", () => stock.QueryAsync("product-profit", new StockReportQuery { From = start, To = end }, ct).GetAwaiter().GetResult().Count);
        Time("Stock valuation", () => stock.QueryAsync("inventory-valuation", new StockReportQuery(), ct).GetAwaiter().GetResult().Count);
        Time("Low-stock report", () => stock.QueryAsync("low-stock", new StockReportQuery(), ct).GetAwaiter().GetResult().Count);
        Time("Stock movement history", () => stock.QueryAsync("inventory-movements", new StockReportQuery { From = start, To = end }, ct).GetAwaiter().GetResult().Count);
        Time("Delivery orders page 2", () => delivery.OrdersAsync(null, null, null, 2, 20, ct).GetAwaiter().GetResult().Items.Count);
    }

    private static string Day(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static long ProfitEvents(NetProfitService service, string from, string to, string breakdown, CancellationToken ct)
    {
        var result = service.QueryAsync(new NetProfitQuery { From = from, To = to, Breakdown = breakdown }, ct).GetAwaiter().GetResult();
        var summary = result.Summary;
        Console.WriteLine("Net profit coverage " + from + " to " + to + ": complete=" + summary.Complete +
            ", missing revenue=" + summary.MissingRevenueCount + ", missing cost=" + summary.MissingCostCount +
            ", unclassified expenses=" + summary.UnclassifiedCount);
        return summary.EventCount;
    }

    private static void Time(string label, Func<long> read)
    {
        var measurement = Stopwatch.StartNew();
        var count = read();
        if (count <= 0) throw new InvalidOperationException(label + " returned no records.");
        Console.WriteLine(label + " | " + count + " rows or events | " + measurement.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms");
    }
}
