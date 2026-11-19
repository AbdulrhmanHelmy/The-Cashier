using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Finance;
using AlKhayal.Application.Purchasing;
using AlKhayal.Application.Returns;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Settings;
using AlKhayal.Application.Workforce;

namespace AlKhayal.Application.Printing
{
    public interface IPrintPaymentReader
    {
        Task<PrintDocumentData> GetAsync(string type, long id, CancellationToken ct);
    }
    public interface IPrintContactReader
    {
        Task<string> PhoneAsync(string type, long id, CancellationToken ct);
    }
    public sealed class PrintDocumentProvider : IPrintDocumentProvider
    {
        private readonly SalesService _sales;
        private readonly PurchaseService _purchases;
        private readonly ReturnService _returns;
        private readonly WorkforceService _workforce;
        private readonly SettingsService _settings;
        private readonly IPrintPaymentReader _payments;
        private readonly IPrintContactReader _contacts;
        public PrintDocumentProvider(SalesService sales, PurchaseService purchases, ReturnService returns, WorkforceService workforce, SettingsService settings, IPrintPaymentReader payments, IPrintContactReader contacts = null)
        { _sales = sales; _purchases = purchases; _returns = returns; _workforce = workforce; _settings = settings; _payments = payments; _contacts = contacts; }
        public async Task<PrintDocumentData> GetAsync(string type, long id, long actorId, bool administrator, CancellationToken ct)
        {
            var d = new PrintDocumentData { Type = type, Id = id, Title = PrintTypes.Titles[Array.IndexOf(PrintTypes.All, type)] };
            if (type == "sale" || type == "delivery")
            {
                var s = await _sales.GetAsync(id, ct).ConfigureAwait(false); if (s == null || s.Status != "posted") return null;
                if(type=="delivery"&&s.Delivery==null)return null;
                d.Reference = s.InvoiceNumber; d.Fields["date"] = DocumentDate(s.InvoiceDate, s.PostedUtc); d.Fields["cashier"] = s.CashierName; d.Fields["party"] = s.CustomerName ?? "نقدي مباشر";
                d.Fields["warehouse"] = s.WarehouseName;
                if (s.CustomerId.HasValue && _contacts != null) d.Fields["partyPhone"] = await _contacts.PhoneAsync("customer", s.CustomerId.Value, ct).ConfigureAwait(false);
                d.Fields["paymentMethod"] = PaymentMethod(s.PaymentMode, s.ImmediateMethod, s.PaymentInstrumentName);
                d.Fields["subtotal"] = Money(s.SubtotalScaled); d.Fields["discountTotal"] = Money(s.DiscountScaled); d.Fields["taxTotal"] = Money(s.TaxScaled); d.Fields["grandTotal"] = Money(s.TotalScaled); d.Fields["paid"] = Money(s.PaidScaled); d.Fields["remaining"] = Money(s.OutstandingScaled);
                if (s.Delivery != null) d.Fields["deliveryFee"] = Money(s.Delivery.FeeScaled);
                if(type=="delivery")
                {
                    var x=s.Delivery;
                    d.Title="بون توصيل";
                    d.Fields["party"]=string.IsNullOrWhiteSpace(x.CustomerName)?d.Fields["party"]:x.CustomerName;
                    d.Fields["partyPhone"] = x.PrimaryPhone;
                    d.Fields["paymentStatus"]=s.OutstandingScaled>0?"متبقٍ على الفاتورة":"مدفوع بالكامل";
                    d.Fields["collectAmount"]=Money(Math.Max(0,s.OutstandingScaled));
                    d.Fields["header"]="عنوان التوصيل: "+x.Address+"\nالهاتف: "+x.PrimaryPhone+(string.IsNullOrWhiteSpace(x.SecondaryPhone)?"":" / "+x.SecondaryPhone)+"\nالمندوب: "+(string.IsNullOrWhiteSpace(x.DriverName)?"تعيين لاحقًا":x.DriverName+" — "+x.DriverPhone)+(string.IsNullOrWhiteSpace(x.Notes)?"":"\nتعليمات التوصيل: "+x.Notes)+"\nحالة التوصيل: "+x.Status;
                    d.Fields.Remove("subtotal");d.Fields.Remove("discountTotal");d.Fields.Remove("taxTotal");d.Fields.Remove("grandTotal");d.Fields.Remove("paid");d.Fields["remaining"]=d.Fields["collectAmount"];
                }
                d.Items = s.Lines.Select(l => new PrintItem { Name = l.ProductName, Variant = UnitDescription(l.VariantName, l.UnitName), Sku = l.Sku, Quantity = l.Quantity, UnitPrice = l.UnitPrice, Discount = l.Discount, Tax = Money(l.TaxScaled), Total = Money(l.LineTotalScaled) }).ToList();
                if (type == "delivery") foreach (var item in d.Items) { item.UnitPrice = null; item.Discount = null; item.Tax = null; item.Total = null; }
            }
            else if (type == "purchase")
            {
                var p = await _purchases.GetAsync(id, ct).ConfigureAwait(false); if (p == null || p.Status != "posted") return null;
                d.Reference = p.InvoiceNumber; d.Fields["party"] = p.SupplierName; d.Fields["date"] = DocumentDate(p.InvoiceDate, p.PostedUtc); d.Fields["cashier"] = p.CreatedByName; d.Fields["paymentMethod"] = PaymentMethod(p.PaymentMode, null, null); d.Fields["grandTotal"] = Money(p.TotalScaled);
                d.Fields["warehouse"] = p.WarehouseName; d.Fields["paymentNotes"] = p.Notes;
                if (_contacts != null) d.Fields["partyPhone"] = await _contacts.PhoneAsync("supplier", p.SupplierId, ct).ConfigureAwait(false);
                d.Fields["header"] = "مخزن الاستلام: " + p.WarehouseName;
                d.Items = p.Lines.Select(l => new PrintItem { Name = l.ProductName, Variant = UnitDescription(l.VariantName, l.UnitName), Sku = l.Sku, Quantity = l.Quantity, UnitPrice = l.PurchaseUnitPrice, Total = Money(l.LineTotalScaled) }).ToList();
            }
            else if (type == "sales-return" || type == "purchase-return")
            {
                var r = await _returns.GetAsync(type == "sales-return" ? "sales" : "purchase", id, ct).ConfigureAwait(false); if (r == null) return null;
                d.Reference = r.Number; d.Fields["party"] = r.PartyName; d.Fields["date"] = Local(r.CreatedUtc); d.Fields["cashier"] = r.ActorName; d.Fields["paymentMethod"] = r.Settlement;
                d.Fields["warehouse"] = r.WarehouseName;
                d.Fields["header"] = "الفاتورة الأصلية: " + r.InvoiceNumber + "\n" + r.Reason;
                d.Fields["grandTotal"] = r.Total; d.Fields["paid"] = r.Cash; d.Fields["remaining"] = r.AccountAdjustment;
                d.Items = r.Lines.Select(l => new PrintItem { Name = l.ProductName, Variant = l.UnitName, Quantity = l.Quantity, Total = l.Amount }).ToList();
            }
            else if (type.StartsWith("shift-", StringComparison.Ordinal))
            {
                CashierShift s;
                try { s = await _workforce.ShiftAsync(id, administrator ? (long?)null : actorId, ct).ConfigureAwait(false); }
                catch (WorkforceException ex) when (ex.Code == "not_found") { return null; }
                if (s == null || type == "shift-closing" && s.ClosedUtc == null) return null;
                d.Reference = "SHIFT-" + s.Id.ToString(CultureInfo.InvariantCulture); d.Fields["cashier"] = s.CashierName;
                d.Fields["warehouse"] = s.RegisterId;
                d.Fields["date"] = Local(s.OpenedUtc) + (s.ClosedUtc == null ? " — مفتوحة" : " — " + Local(s.ClosedUtc));
                d.Fields["party"] = "نقطة البيع: " + s.RegisterId + " / عدد المبيعات: " + s.SalesCount;
                d.Fields["grandTotal"] = s.Expected; d.Fields["paid"] = s.Counted; d.Fields["remaining"] = s.Difference;
                var labels = new[] { "افتتاح الدرج", "المبيعات النقدية", "المبيعات الآجلة", "التحصيلات", "إدخال نقد", "إخراج نقد" };
                var amounts = new[] { s.Opening, s.CashSales, s.CreditSales, s.Collections, s.CashIn, s.CashOut };
                for (var i = 0; i < labels.Length; i++) d.Items.Add(new PrintItem { Name = labels[i], Quantity = "1", Total = amounts[i] });
            }
            else
            {
                d = await _payments.GetAsync(type, id, ct).ConfigureAwait(false); if (d == null) return null;
                d.Fields["date"] = Local(d.Fields["date"]);
            }
            var company = await _settings.GetCompanyAsync(ct).ConfigureAwait(false);
            d.Fields["company"] = company.BusinessName; d.Fields["address"] = company.Address; d.Fields["phone"] = company.Phone;
            d.Fields["taxInfo"] = company.TaxRegistrationNumber; d.Fields["footer"] = company.FooterText;
            var general = await _settings.GetGeneralAsync(ct).ConfigureAwait(false);
            string header; d.Fields.TryGetValue("header", out header);
            string paymentMethod; d.Fields.TryGetValue("paymentMethod", out paymentMethod);
            d.Fields["header"] = (string.IsNullOrWhiteSpace(header) ? "" : header + "\n") +
                (string.IsNullOrWhiteSpace(paymentMethod) ? "" : "طريقة الدفع: " + paymentMethod + "\n") +
                "العملة: " + general.CurrencyCode + " " + general.CurrencySymbol;
            return d;
        }
        private static string PaymentMethod(string mode, string immediateMethod, string instrument)
        {
            var method = string.IsNullOrWhiteSpace(instrument) ? immediateMethod : instrument;
            if (string.Equals(mode, "credit", StringComparison.OrdinalIgnoreCase)) return "آجل";
            if (string.Equals(method, "cash", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "cash", StringComparison.OrdinalIgnoreCase)) return "نقدي";
            return method;
        }
        private static string UnitDescription(string variant, string unit)
        {
            var parts = new[] { variant, unit }.Where(x => !string.IsNullOrWhiteSpace(x) && x.Trim() != "افتراضي" && !x.Trim().Equals("Default", StringComparison.OrdinalIgnoreCase));
            return string.Join(" / ", parts);
        }
        private static string DocumentDate(string date, string posted) => Local(date) + (string.IsNullOrWhiteSpace(posted) ? "" : "\nترحيل: " + Local(posted));
        private static string Money(long scaled) => MoneyPolicy.FormatForUser(scaled);
        private static string Local(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= 10) return value;
            DateTimeOffset time; return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out time) ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : value;
        }
    }
}
