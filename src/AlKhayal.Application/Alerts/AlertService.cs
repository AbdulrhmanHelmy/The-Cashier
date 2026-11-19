using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Alerts
{
    public sealed class AlertRecord
    {
        public string Id { get; set; }
        public string Type { get; set; } = "low-stock";
        public string Title { get; set; }
        public string Detail { get; set; }
        public string Severity { get; set; }
        public string Date { get; set; }
        public bool Read { get; set; }
        public long ProductId { get; set; }
        public string ProductName { get; set; }
        public string Unit { get; set; }
        public string TotalQuantity { get; set; }
        public string ReorderPoint { get; set; }
        public string RelatedRoute { get; set; }
        public long RelatedId { get; set; }
        public long RelatedParentId { get; set; }
        public long BatchId { get; set; }
        public string LotNumber { get; set; }
        public long WarehouseId { get; set; }
        public string WarehouseName { get; set; }
    }
    public sealed class StockAlert
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public long ProductId { get; set; }
        public string ProductName { get; set; }
        public string Unit { get; set; }
        public string TotalQuantity { get; set; }
        public string ReorderPoint { get; set; }
    }
    public interface IAlertStore
    {
        Task<IReadOnlyList<AlertRecord>> ActiveAsync(long userId, DateTime asOfUtc, CancellationToken ct);
        Task SetReadAsync(long userId, string alertId, bool read, DateTime asOfUtc, CancellationToken ct);
        Task MarkAllReadAsync(long userId, DateTime asOfUtc, bool stockOnly, CancellationToken ct);
    }
    public sealed class AlertService
    {
        private readonly IAlertStore _store;
        public AlertService(IAlertStore store) { _store=store; }
        public Task<IReadOnlyList<AlertRecord>> ActiveAsync(long userId, CancellationToken ct)
        { return ActiveAsync(userId, System.DateTime.UtcNow, ct); }
        public Task<IReadOnlyList<AlertRecord>> ActiveAsync(long userId, DateTime asOfUtc, CancellationToken ct)
        { return _store.ActiveAsync(userId, asOfUtc, ct); }
        public async Task<IReadOnlyList<AlertRecord>> ActiveAsync(long userId, bool stockOnly, CancellationToken ct)
        {
            var alerts=await ActiveAsync(userId,ct).ConfigureAwait(false);
            return stockOnly?alerts.Where(IsStock).ToList():alerts;
        }
        public async Task<IReadOnlyList<StockAlert>> ActiveStockAsync(CancellationToken ct)
        {
            var alerts=await ActiveAsync(0,ct).ConfigureAwait(false);
            return alerts.Where(x=>x.Type=="low-stock" || x.Type=="reorder").Select(x=>new StockAlert
            { Id=x.Id,Type=x.Type,ProductId=x.ProductId,ProductName=x.ProductName,Unit=x.Unit,TotalQuantity=x.TotalQuantity,ReorderPoint=x.ReorderPoint }).ToList();
        }
        public Task SetReadAsync(long userId, string alertId, bool read, CancellationToken ct)
        { return _store.SetReadAsync(userId, alertId, read, System.DateTime.UtcNow, ct); }
        public Task MarkAllReadAsync(long userId, bool stockOnly, CancellationToken ct)
        { return _store.MarkAllReadAsync(userId, System.DateTime.UtcNow, stockOnly, ct); }
        public static bool IsLowStock(long totalQuantityScaled, long reorderPointScaled)
        { return reorderPointScaled>0 && totalQuantityScaled<=reorderPointScaled; }
        private static bool IsStock(AlertRecord alert)
        { return alert.Type=="low-stock" || alert.Type=="reorder"; }
    }
}
