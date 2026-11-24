using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Finance;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Purchasing;
using AlKhayal.Core.Models;

namespace AlKhayal.Application.Returns
{
    public sealed class ReturnRecord
    {
        public long Id { get; set; }
        public string Type { get; set; }
        public string Number { get; set; }
        public long InvoiceId { get; set; }
        public string InvoiceNumber { get; set; }
        public string PartyName { get; set; }
        public string WarehouseName { get; set; }
        public string CreatedUtc { get; set; }
        public string ActorName { get; set; }
        public string Reason { get; set; }
        public long TotalScaled { get; set; }
        public string Total => MoneyPolicy.FormatForUser(TotalScaled);
        public long CashScaled { get; set; }
        public string Cash => MoneyPolicy.FormatForUser(CashScaled);
        public long ElectronicScaled { get; set; }
        public string Electronic => MoneyPolicy.FormatForUser(ElectronicScaled);
        public string AccountAdjustment => MoneyPolicy.FormatForUser(TotalScaled - CashScaled - ElectronicScaled);
        public string Settlement { get; set; }
        public string ElectronicMethod { get; set; }
        public string ElectronicInstrument { get; set; }
        public string OriginalPaymentKey { get; set; }
        public string ElectronicRefundStatus { get; set; }
        public string ExternalRefundReference { get; set; }
        public List<ReturnRecordLine> Lines { get; set; } = new List<ReturnRecordLine>();
        public List<ReturnMoneyMovement> Movements { get; set; } = new List<ReturnMoneyMovement>();
    }
    public sealed class ReturnRecordLine
    {
        public string ProductName { get; set; }
        public string UnitName { get; set; }
        public long QuantityScaled { get; set; }
        public string Quantity => (QuantityScaled / 1000000m).ToString("0.######", CultureInfo.InvariantCulture);
        public long AmountScaled { get; set; }
        public string Amount => MoneyPolicy.FormatForUser(AmountScaled);
        public string UnitPrice => QuantityScaled <= 0 ? "0.00" : MoneyPolicy.FormatForUser((long)Math.Round(AmountScaled * 1000000m / QuantityScaled, MidpointRounding.AwayFromZero));
        public bool? IsSellable { get; set; }
        public string InventoryStatus { get; set; }
        public List<string> Serials { get; set; } = new List<string>();
    }
    public sealed class ReturnMoneyMovement
    {
        public long Id { get; set; }
        public string AccountName { get; set; }
        public string Type { get; set; }
        public string Role { get; set; }
        public string CreatedUtc { get; set; }
        public string Reference { get; set; }
        public long AmountScaled { get; set; }
        public string Amount => MoneyPolicy.FormatForUser(AmountScaled);
    }
    public sealed class ReturnInvoice
    {
        public long Id { get; set; }
        public string Type { get; set; }
        public string Number { get; set; }
        public string InvoiceDate { get; set; }
        public string PartyName { get; set; }
        public long WarehouseId { get; set; }
        public string PaymentMode { get; set; }
        public long? InstallmentContractId { get; set; }
        public string ContractNumber { get; set; }
        public string OriginalTotal { get; set; }
        public string Collected { get; set; }
        public string Outstanding { get; set; }
        public string PaymentInstrumentName { get; set; }
        public List<ReturnInstallment> Installments { get; set; } = new List<ReturnInstallment>();
        public List<ReturnInvoiceLine> Lines { get; set; } = new List<ReturnInvoiceLine>();
    }
    public sealed class ReturnInstallment { public int Number { get; set; } public string DueOn { get; set; } public long RemainingScaled { get; set; } public string Remaining => MoneyPolicy.FormatForUser(RemainingScaled); }
    public sealed class ReturnInvoiceLine
    {
        public long Id { get; set; }
        public string ProductName { get; set; }
        public string UnitName { get; set; }
        public string Quantity { get; set; }
        public string Available { get; set; }
        public string UnitPrice { get; set; }
        public string LineTotal { get; set; }
        public List<ReturnUnit> Units { get; set; } = new List<ReturnUnit>();
    }
    public sealed class ReturnUnit { public string Serial { get; set; } public List<string> Imeis { get; set; } = new List<string>(); }
    public sealed class ReturnInvoicePickerQuery
    {
        public string Type { get; set; }
        public string Search { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string Status { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
    public sealed class ReturnInvoicePickerItem
    {
        public long Id { get; set; }
        public string Type { get; set; }
        public string InvoiceNumber { get; set; }
        public string InvoiceDate { get; set; }
        public string PartyName { get; set; }
        public long TotalScaled { get; set; }
        public string Total => MoneyPolicy.FormatForUser(TotalScaled);
        public string Status { get; set; }
        public long OriginalQuantityScaled { get; set; }
        public long ReturnedQuantityScaled { get; set; }
        public long RemainingQuantityScaled { get; set; }
        public bool IsEligible { get; set; }
        public string Eligibility { get; set; }
    }
    public sealed class ReturnInvoicePickerPage
    {
        public List<ReturnInvoicePickerItem> Items { get; set; } = new List<ReturnInvoicePickerItem>();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages => TotalCount == 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;
    }
    public sealed class ReturnHistoryPage
    {
        public IReadOnlyList<ReturnRecord> Items { get; set; }
        public long Page { get; set; }
        public int PageSize { get; set; }
        public long TotalCount { get; set; }
    }

    public interface IReturnReadStore
    {
        Task<ReturnHistoryPage> ListPageAsync(string search, long page, int pageSize, CancellationToken ct);
        Task<IReadOnlyList<ReturnRecord>> ListAsync(string search, CancellationToken ct, int? searchLimit = null);
        Task<ReturnRecord> GetAsync(string type, long id, CancellationToken ct);
        Task<long?> FindInvoiceAsync(string type, string number, CancellationToken ct);
        Task<bool> InvoiceExistsAsync(string type, long id, CancellationToken ct);
        Task<ReturnInvoicePickerPage> SearchInvoicesAsync(ReturnInvoicePickerQuery query, CancellationToken ct);
        Task<string> GetContractNumberAsync(long contractId, CancellationToken ct);
        Task<List<ReturnInstallment>> GetInstallmentsAsync(long contractId, CancellationToken ct);
    }
    public sealed class ReturnService
    {
        private readonly IReturnReadStore _store;
        private readonly SalesService _sales;
        private readonly PurchaseService _purchases;
        public ReturnService(IReturnReadStore store, SalesService sales, PurchaseService purchases) { _store = store; _sales = sales; _purchases = purchases; }
        public Task<ReturnHistoryPage> ListPageAsync(string search, long page, int pageSize, CancellationToken ct)
        {
            if (page < 1 || (pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200))
                throw new ArgumentException("رقم الصفحة أو عدد المرتجعات في الصفحة غير صالح.");
            return _store.ListPageAsync((search ?? "").Trim(), page, pageSize, ct);
        }

        public Task<IReadOnlyList<ReturnRecord>> ListAsync(string search, CancellationToken ct, int? searchLimit = null) => _store.ListAsync((search ?? "").Trim(), ct, searchLimit.HasValue ? Math.Max(1, Math.Min(20, searchLimit.Value)) : (int?)null);
        public Task<ReturnInvoicePickerPage> SearchInvoicesAsync(ReturnInvoicePickerQuery query, CancellationToken ct)
        {
            query = query ?? new ReturnInvoicePickerQuery();
            query.Type = string.IsNullOrWhiteSpace(query.Type) ? "all" : query.Type.Trim().ToLowerInvariant();
            if (query.Type != "all") ValidateType(query.Type);
            query.Status = string.IsNullOrWhiteSpace(query.Status) ? "all" : query.Status.Trim().ToLowerInvariant();
            if (query.Status != "all" && query.Status != "eligible" && query.Status != "partial" && query.Status != "full") throw new ArgumentException("اختر حالة فاتورة صحيحة.");
            query.Search = (query.Search ?? "").Trim();
            if (query.Search.Length > 120) throw new ArgumentException("نص البحث طويل جدًا.");
            DateTime parsed;
            if (!string.IsNullOrWhiteSpace(query.From) && !DateTime.TryParseExact(query.From, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) throw new ArgumentException("تاريخ البداية غير صالح.");
            if (!string.IsNullOrWhiteSpace(query.To) && !DateTime.TryParseExact(query.To, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) throw new ArgumentException("تاريخ النهاية غير صالح.");
            if (!string.IsNullOrWhiteSpace(query.From) && !string.IsNullOrWhiteSpace(query.To) && string.CompareOrdinal(query.From, query.To) > 0) throw new ArgumentException("تاريخ البداية يجب ألا يتجاوز تاريخ النهاية.");
            query.Page = Math.Max(1, query.Page);
            query.PageSize = Math.Max(5, Math.Min(50, query.PageSize));
            return _store.SearchInvoicesAsync(query, ct);
        }
        public Task<ReturnRecord> GetAsync(string type, long id, CancellationToken ct) { ValidateType(type); if (id <= 0) throw new ArgumentException("اختر مرتجعًا صحيحًا."); return _store.GetAsync(type, id, ct); }
        public async Task<ReturnInvoice> LookupAsync(string type, string number, CancellationToken ct)
        {
            ValidateType(type); number = (number ?? "").Trim(); if (number.Length == 0 || number.Length > 120) throw new ArgumentException("أدخل رقم الفاتورة الصحيح.");
            var id = await _store.FindInvoiceAsync(type, number, ct).ConfigureAwait(false); if (!id.HasValue) return null;
            if (type == "sales")
            {
                var sale = await _sales.GetAsync(id.Value, ct).ConfigureAwait(false); if (sale == null || sale.Status != "posted") return null;
                return new ReturnInvoice { Id = sale.Id, Type = type, Number = sale.InvoiceNumber, InvoiceDate = sale.InvoiceDate, PartyName = sale.CustomerName, WarehouseId = sale.WarehouseId, PaymentMode = sale.PaymentMode,
                    InstallmentContractId=sale.InstallmentContractId,ContractNumber=sale.InstallmentContractId.HasValue?await _store.GetContractNumberAsync(sale.InstallmentContractId.Value,ct).ConfigureAwait(false):null,
                    Installments=sale.InstallmentContractId.HasValue?await _store.GetInstallmentsAsync(sale.InstallmentContractId.Value,ct).ConfigureAwait(false):new List<ReturnInstallment>(),
                    OriginalTotal=MoneyPolicy.FormatForUser(sale.TotalScaled),Collected=MoneyPolicy.FormatForUser(sale.PaidScaled),Outstanding=MoneyPolicy.FormatForUser(sale.OutstandingScaled),PaymentInstrumentName=sale.PaymentInstrumentName,
                    Lines = sale.Lines.Select(x => new ReturnInvoiceLine { Id = x.Id, ProductName = x.ProductName + " — " + x.VariantName, UnitName = x.UnitName, Quantity = x.Quantity, UnitPrice = x.UnitPrice, LineTotal = x.LineTotal,
                        Available = Math.Max(0, decimal.Parse(x.Quantity, CultureInfo.InvariantCulture) - decimal.Parse(x.ReturnedQuantity ?? "0", CultureInfo.InvariantCulture)).ToString("0.######", CultureInfo.InvariantCulture),
                        Units = x.AvailableTrackingUnits.Select(u => new ReturnUnit { Serial = u.Serial, Imeis = u.Imeis }).ToList() }).ToList() };
            }
            var purchase = await _purchases.GetAsync(id.Value, ct).ConfigureAwait(false); if (purchase == null || purchase.Status != "posted") return null;
            var lines = await _purchases.ReturnableLinesAsync(purchase.Id, purchase.WarehouseId, ct).ConfigureAwait(false);
            return new ReturnInvoice { Id = purchase.Id, Type = type, Number = purchase.InvoiceNumber, InvoiceDate = purchase.InvoiceDate, PartyName = purchase.SupplierName, WarehouseId = purchase.WarehouseId, PaymentMode = purchase.PaymentMode,
                Lines = lines.Select(x => new ReturnInvoiceLine { Id = x.PurchaseInvoiceLineId, ProductName = x.ProductName + " — " + x.VariantName, UnitName = x.UnitName, Quantity = x.Quantity, Available = x.AvailableQuantity,
                    UnitPrice = purchase.Lines.FirstOrDefault(p => p.Id == x.PurchaseInvoiceLineId)?.PurchaseUnitPrice, LineTotal = purchase.Lines.FirstOrDefault(p => p.Id == x.PurchaseInvoiceLineId)?.LineTotalFormatted,
                    Units = x.PhysicalUnits.Select(u => new ReturnUnit { Serial = u.Serial, Imeis = u.Imeis }).ToList() }).ToList() };
        }
        public async Task<ReturnInvoice> LookupAsync(string type, long id, CancellationToken ct)
        {
            ValidateType(type); if (id <= 0) throw new ArgumentException("اختر فاتورة صحيحة.");
            if (!await _store.InvoiceExistsAsync(type, id, ct).ConfigureAwait(false)) return null;
            if (type == "sales")
            {
                var sale = await _sales.GetAsync(id, ct).ConfigureAwait(false); if (sale == null || sale.Status != "posted") return null;
                return await MapSaleAsync(sale, type, ct).ConfigureAwait(false);
            }
            var purchase = await _purchases.GetAsync(id, ct).ConfigureAwait(false); if (purchase == null || purchase.Status != "posted") return null;
            return await MapPurchaseAsync(purchase, type, ct).ConfigureAwait(false);
        }
        private async Task<ReturnInvoice> MapSaleAsync(SaleInvoice sale, string type, CancellationToken ct)
        {
            return new ReturnInvoice { Id = sale.Id, Type = type, Number = sale.InvoiceNumber, InvoiceDate = sale.InvoiceDate, PartyName = sale.CustomerName, WarehouseId = sale.WarehouseId, PaymentMode = sale.PaymentMode,
                InstallmentContractId=sale.InstallmentContractId,ContractNumber=sale.InstallmentContractId.HasValue?await _store.GetContractNumberAsync(sale.InstallmentContractId.Value,ct).ConfigureAwait(false):null,
                Installments=sale.InstallmentContractId.HasValue?await _store.GetInstallmentsAsync(sale.InstallmentContractId.Value,ct).ConfigureAwait(false):new List<ReturnInstallment>(),
                OriginalTotal=MoneyPolicy.FormatForUser(sale.TotalScaled),Collected=MoneyPolicy.FormatForUser(sale.PaidScaled),Outstanding=MoneyPolicy.FormatForUser(sale.OutstandingScaled),PaymentInstrumentName=sale.PaymentInstrumentName,
                Lines = sale.Lines.Select(x => new ReturnInvoiceLine { Id = x.Id, ProductName = x.ProductName + " — " + x.VariantName, UnitName = x.UnitName, Quantity = x.Quantity, UnitPrice = x.UnitPrice, LineTotal = x.LineTotal,
                    Available = Math.Max(0, decimal.Parse(x.Quantity, CultureInfo.InvariantCulture) - decimal.Parse(x.ReturnedQuantity ?? "0", CultureInfo.InvariantCulture)).ToString("0.######", CultureInfo.InvariantCulture),
                    Units = x.AvailableTrackingUnits.Select(u => new ReturnUnit { Serial = u.Serial, Imeis = u.Imeis }).ToList() }).ToList() };
        }
        private async Task<ReturnInvoice> MapPurchaseAsync(PurchaseInvoice purchase, string type, CancellationToken ct)
        {
            var lines = await _purchases.ReturnableLinesAsync(purchase.Id, purchase.WarehouseId, ct).ConfigureAwait(false);
            return new ReturnInvoice { Id = purchase.Id, Type = type, Number = purchase.InvoiceNumber, InvoiceDate = purchase.InvoiceDate, PartyName = purchase.SupplierName, WarehouseId = purchase.WarehouseId, PaymentMode = purchase.PaymentMode,
                Lines = lines.Select(x => new ReturnInvoiceLine { Id = x.PurchaseInvoiceLineId, ProductName = x.ProductName + " — " + x.VariantName, UnitName = x.UnitName, Quantity = x.Quantity, Available = x.AvailableQuantity,
                    UnitPrice = purchase.Lines.FirstOrDefault(p => p.Id == x.PurchaseInvoiceLineId)?.PurchaseUnitPrice, LineTotal = purchase.Lines.FirstOrDefault(p => p.Id == x.PurchaseInvoiceLineId)?.LineTotalFormatted,
                    Units = x.PhysicalUnits.Select(u => new ReturnUnit { Serial = u.Serial, Imeis = u.Imeis }).ToList() }).ToList() };
        }
        private static void ValidateType(string type) { if (type != "sales" && type != "purchase") throw new ArgumentException("اختر فاتورة بيع أو شراء."); }
    }
}
