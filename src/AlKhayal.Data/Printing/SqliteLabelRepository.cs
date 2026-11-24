using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Printing;
using AlKhayal.Application.Finance;
using Dapper;

namespace AlKhayal.Data.Printing
{
    public sealed class SqliteLabelRepository : ILabelRepository
    {
        private readonly string _connectionString;
        public SqliteLabelRepository(string connectionString) { _connectionString = connectionString; }
        private static JavaScriptSerializer Serializer() => new JavaScriptSerializer { MaxJsonLength = 8000000, RecursionLimit = 20 };
        public async Task<LabelSettings> ReadAsync(CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                var json = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition("SELECT Json FROM LabelConfiguration WHERE Id=1", cancellationToken: ct)).ConfigureAwait(false);
                if (json == null) return null;
                var serializer = Serializer();
                var parts = serializer.Deserialize<Dictionary<string, object>>(json);
                object design;
                parts.TryGetValue(nameof(LabelSettings.Design), out design);
                parts.Remove(nameof(LabelSettings.Design));
                var settings = serializer.ConvertToType<LabelSettings>(parts);
                if (design != null)
                {
                    try { settings.Design = serializer.ConvertToType<List<LabelDesignElement>>(design); }
                    catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex is OverflowException)
                    { System.Diagnostics.Trace.TraceWarning("Ignoring unreadable label design: {0}", ex.GetType().Name); }
                }
                return settings;
            }
        }
        public async Task<LabelSettings> SaveAsync(LabelSettings s, long actorId, CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                using (var tx = c.BeginTransaction(System.Data.IsolationLevel.Serializable))
                {
                    var version = await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COALESCE((SELECT Version FROM LabelConfiguration WHERE Id=1),0)", transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                    if (s.Version != version) throw new PrintException("conflict", "تغيرت قوالب الملصقات. أعد تحميل المحفوظ قبل الحفظ.");
                    s.Version = version + 1; var now = DateTime.UtcNow.ToString("o"); var json = Serializer().Serialize(s);
                    await c.ExecuteAsync(new CommandDefinition("INSERT OR REPLACE INTO LabelConfiguration(Id,Version,Json,ActorId,UpdatedUtc) VALUES(1,@Version,@json,@actorId,@now); INSERT INTO LabelConfigurationAudit(Version,ActorId,CreatedUtc) VALUES(@Version,@actorId,@now)", new { s.Version, json, actorId, now }, tx, cancellationToken: ct)).ConfigureAwait(false);
                    tx.Commit(); return s;
                }
            }
        }
        public async Task<string> BusinessNameAsync(CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                var name = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                    "SELECT BusinessName FROM CompanyProfile WHERE Id=1", cancellationToken: ct)).ConfigureAwait(false);
                return ResolveBusinessName(name);
            }
        }
        public async Task<List<ProductLabel>> ProductsAsync(IReadOnlyList<LabelSelection> selected, CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                using (var tx = c.BeginTransaction(System.Data.IsolationLevel.Serializable))
                {
                    var rows = (await c.QueryAsync<Row>(new CommandDefinition(@"SELECT b.Id BarcodeId,b.Barcode,p.Id ProductId,p.Name ProductName,v.Id VariantId,
                        COALESCE(v.SellPriceOverrideScaled,d.SellPriceScaled) PriceScaled,u.ToBaseNumerator Numerator,u.ToBaseDenominator Denominator,
                        un.Name Unit,c.Name Category,sku.Sku,bs.Format
                        FROM ProductBarcodes b JOIN Products p ON p.Id=b.ProductId JOIN ProductDetails d ON d.ProductId=p.Id
                        JOIN ProductVariants v ON v.Id=b.VariantId JOIN ProductUnits u ON u.Id=b.ProductUnitId JOIN Units un ON un.Id=u.UnitId
                        LEFT JOIN ProductCategories c ON c.Id=d.CategoryId LEFT JOIN ProductSkus sku ON sku.VariantId=v.Id AND sku.RetiredUtc IS NULL
                        LEFT JOIN BarcodeSymbologies bs ON bs.BarcodeId=b.Id
                        WHERE b.Id IN @ids AND b.RetiredUtc IS NULL AND d.IsActive=1 AND d.ArchivedUtc IS NULL AND d.MigrationState='ready' AND v.IsActive=1 AND v.ArchivedUtc IS NULL AND u.IsActive=1 AND u.IsSaleUnit=1", new { ids = selected.Select(s => s.BarcodeId).ToArray() }, tx, cancellationToken: ct)).ConfigureAwait(false)).ToDictionary(r => r.BarcodeId);
                    if (rows.Count != selected.Count) throw new PrintException("not_found", "بعض الأكواد متقاعدة أو منتجاتها/متغيراتها/وحداتها غير متاحة للبيع. أعد تحميلها.");
                    var settings = await c.QuerySingleAsync<Company>(new CommandDefinition("SELECT p.BusinessName,s.CurrencySymbol FROM CompanyProfile p CROSS JOIN ApplicationSettings s WHERE p.Id=1 AND s.Id=1", transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                    var businessName = ResolveBusinessName(settings.BusinessName);
                    var result = new List<ProductLabel>();
                    foreach (var item in selected)
                    {
                        var r = rows[item.BarcodeId];
                        var format = r.Format ?? "code128";
                        if (!string.IsNullOrEmpty(item.Format) && item.Format != format) throw new PrintException("invalid_barcode", "صيغة الباركود المحددة لا تطابق الصيغة المحفوظة.");
                        string code; try { code = BarcodeRules.Validate(r.Barcode, format); } catch (ArgumentException ex) { throw new PrintException("invalid_barcode", ex.Message); }
                        // Same base-price/unit-ratio policy as the existing POS. Never use frontend prices.
                        var price = checked((long)(decimal.Round((decimal)r.PriceScaled * r.Numerator / r.Denominator / 100m, 0, MidpointRounding.AwayFromZero) * 100m));
                        result.Add(new ProductLabel { ProductId = r.ProductId, VariantId = r.VariantId, BarcodeId = r.BarcodeId, Barcode = code, Format = format, Quantity = item.Quantity,
                            Fields = new Dictionary<string, string> { ["productName"] = r.ProductName, ["company"] = businessName, ["productCode"] = "P-" + r.ProductId.ToString("D6"), ["sku"] = r.Sku, ["price"] = MoneyPolicy.FormatForUser(price) + " " + settings.CurrencySymbol, ["unit"] = r.Unit, ["category"] = r.Category } });
                    }
                    tx.Commit(); return result;
                }
            }
        }
        private static string ResolveBusinessName(string name) => string.IsNullOrWhiteSpace(name) ? LabelService.DefaultBusinessName : name;
        private sealed class Company { public string BusinessName { get; set; } public string CurrencySymbol { get; set; } }
        private sealed class Row
        {
            public long BarcodeId { get; set; } public string Barcode { get; set; } public long ProductId { get; set; } public string ProductName { get; set; } public long VariantId { get; set; }
            public long PriceScaled { get; set; } public long Numerator { get; set; } public long Denominator { get; set; } public string Unit { get; set; } public string Category { get; set; } public string Sku { get; set; } public string Format { get; set; }
        }
    }
}
