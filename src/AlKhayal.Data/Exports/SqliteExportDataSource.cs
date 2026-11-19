using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Exports;
using Dapper;

namespace AlKhayal.Data.Exports
{
    public sealed class SqliteExportDataSource : IExportDataSource
    {
        private readonly string connectionString;
        public SqliteExportDataSource(string connectionString) { this.connectionString = connectionString; }

        public async Task<IReadOnlyList<ExportRow>> QueryAsync(ExportQuery query, CancellationToken ct)
        {
            string sql; var args = new DynamicParameters();
            switch (query.Dataset)
            {
                case "products": sql = Products(query.Filters, args); break;
                case "customers": sql = Customers(query.Filters, args); break;
                case "suppliers": sql = Suppliers(query.Filters, args); break;
                case "warehouses": sql = Warehouses(query.Filters, args); break;
                case "sales": sql = Sales(query.Filters, args); break;
                case "purchases": sql = Purchases(query.Filters, args); break;
                default: throw new ArgumentException("مجموعة بيانات التصدير غير مدعومة.");
            }
            using (var connection = new SQLiteConnection(connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var pragma = new SQLiteCommand("PRAGMA query_only=ON; PRAGMA busy_timeout=5000;", connection)) pragma.ExecuteNonQuery();
                var rows = await connection.QueryAsync(new CommandDefinition(sql, args, cancellationToken: ct)).ConfigureAwait(false);
                return rows.Cast<object>().Select(x => new ExportRow { Values = ((IDictionary<string, object>)x).ToDictionary(p => p.Key, p => Normalize(p.Value), StringComparer.OrdinalIgnoreCase) }).ToList();
            }
        }

        private static object Normalize(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            if (value is long || value is int || value is short || value is decimal || value is double || value is float) return value;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        private static string Like(IReadOnlyDictionary<string,string> f, DynamicParameters a, string[] columns)
        {
            string value; if (!f.TryGetValue("search", out value) || string.IsNullOrWhiteSpace(value)) return null;
            a.Add("Search", "%" + value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
            return "(" + string.Join(" OR ", columns.Select(c => c + " LIKE @Search ESCAPE '\\'")) + ")";
        }
        private static void AddDateAndCommon(IReadOnlyDictionary<string,string> f, DynamicParameters a, IList<string> where, string dateColumn, string statusColumn, string warehouseColumn)
        {
            string value;
            if (f.TryGetValue("from",out value)) { DateTime d; if(!DateTime.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out d)) throw new ArgumentException("تاريخ البداية غير صالح."); a.Add("From",value); where.Add(dateColumn+">=@From"); }
            if (f.TryGetValue("to",out value)) { DateTime d; if(!DateTime.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out d)) throw new ArgumentException("تاريخ النهاية غير صالح."); a.Add("To",value); where.Add(dateColumn+"<=@To"); }
            if (f.TryGetValue("status",out value)) { if(new[]{"draft","posted","canceled"}.Contains(value,StringComparer.OrdinalIgnoreCase)==false)throw new ArgumentException("حالة الفاتورة غير صالحة."); a.Add("Status",value.ToLowerInvariant()); where.Add(statusColumn+"=@Status"); }
            if (f.TryGetValue("warehouseId",out value)) { long id; if(!long.TryParse(value,out id)||id<=0)throw new ArgumentException("المخزن غير صالح."); a.Add("WarehouseId",id); where.Add(warehouseColumn+"=@WarehouseId"); }
        }
        private static string Where(IList<string> terms)=>terms.Count==0?"":" WHERE "+string.Join(" AND ",terms);

        private static string Products(IReadOnlyDictionary<string,string> f, DynamicParameters a)
        {
            var w=new List<string>{"d.ArchivedUtc IS NULL"}; var like=Like(f,a,new[]{"p.Name","d.Description"});if(like!=null)w.Add("("+like+" OR EXISTS(SELECT 1 FROM ProductSkus ps WHERE ps.ProductId=p.Id AND ps.RetiredUtc IS NULL AND ps.Sku LIKE @Search ESCAPE '\\') OR EXISTS(SELECT 1 FROM ProductBarcodes pb WHERE pb.ProductId=p.Id AND pb.RetiredUtc IS NULL AND pb.Barcode LIKE @Search ESCAPE '\\'))");
            string v;if(f.TryGetValue("categoryId",out v)){long id;if(!long.TryParse(v,out id)||id<=0)throw new ArgumentException("التصنيف غير صالح.");a.Add("CategoryId",id);w.Add("d.CategoryId=@CategoryId");}
            if(f.TryGetValue("isActive",out v)){bool active;if(!bool.TryParse(v,out active))throw new ArgumentException("حالة المنتج غير صالحة.");a.Add("IsActive",active?1:0);w.Add("d.IsActive=@IsActive");}
            return @"SELECT p.Name name,
(SELECT ps.Sku FROM ProductSkus ps JOIN ProductVariants pv ON pv.Id=ps.VariantId WHERE pv.ProductId=p.Id AND pv.IsDefault=1 AND ps.RetiredUtc IS NULL ORDER BY ps.Id LIMIT 1) sku,
(SELECT pb.Barcode FROM ProductBarcodes pb JOIN ProductVariants pv ON pv.Id=pb.VariantId WHERE pv.ProductId=p.Id AND pv.IsDefault=1 AND pb.IsPrimary=1 AND pb.RetiredUtc IS NULL ORDER BY pb.Id LIMIT 1) barcode,
c.Name category,u.Name unit,d.PurchasePriceScaled/10000.0 purchasePrice,d.SellPriceScaled/10000.0 sellingPrice,CASE WHEN d.IsActive=1 THEN 'نشط' ELSE 'غير نشط' END status
FROM Products p JOIN ProductDetails d ON d.ProductId=p.Id LEFT JOIN ProductCategories c ON c.Id=d.CategoryId JOIN Units u ON u.Id=d.BaseUnitId"+Where(w)+" ORDER BY p.Name COLLATE NOCASE,p.Id";
        }
        private static string Customers(IReadOnlyDictionary<string,string> f,DynamicParameters a){var w=new List<string>();var like=Like(f,a,new[]{"Name","Code","Phone","SecondaryPhone","Address","Status","CAST(Balance AS TEXT)"});if(like!=null)w.Add(like);string v;if(f.TryGetValue("status",out v)){if(v!="نشط"&&v!="متوقف")throw new ArgumentException("حالة العميل غير صالحة.");a.Add("Status",v);w.Add("Status=@Status");}return "SELECT Name name,Phone phone,Address address,Balance balance,Status status FROM Customers"+Where(w)+" ORDER BY Name COLLATE NOCASE,Id";}
        private static string Suppliers(IReadOnlyDictionary<string,string> f,DynamicParameters a){var w=new List<string>();var like=Like(f,a,new[]{"Name","Code","Phone","Contact"});if(like!=null)w.Add(like);string v;if(f.TryGetValue("status",out v)){if(v!="نشط"&&v!="غير نشط")throw new ArgumentException("حالة المورد غير صالحة.");a.Add("Status",v);w.Add("Status=@Status");}return "SELECT Name name,Phone phone,Address address,(Payable-SupplierCredit) balance,Status status FROM Suppliers"+Where(w)+" ORDER BY Name COLLATE NOCASE,Id";}
        private static string Warehouses(IReadOnlyDictionary<string,string> f,DynamicParameters a){var w=new List<string>();var like=Like(f,a,new[]{"Name"});if(like!=null)w.Add(like);string v;if(f.TryGetValue("isActive",out v)){bool active;if(!bool.TryParse(v,out active))throw new ArgumentException("حالة المخزن غير صالحة.");a.Add("IsActive",active?1:0);w.Add("IsActive=@IsActive");}return "SELECT Name name,Code code,CASE WHEN IsActive=1 THEN 'نشط' ELSE 'غير نشط' END status FROM Warehouses"+Where(w)+" ORDER BY IsDefault DESC,Name COLLATE NOCASE,Id";}
        private static string Sales(IReadOnlyDictionary<string,string> f,DynamicParameters a){var w=new List<string>{"NOT EXISTS(SELECT 1 FROM SalesInvoiceCorrections scx WHERE scx.PreviousInvoiceId=si.Id)"};var like=Like(f,a,new[]{"si.InvoiceNumber","c.Name"});if(like!=null)w.Add(like);AddDateAndCommon(f,a,w,"si.InvoiceDate","CASE WHEN EXISTS(SELECT 1 FROM SalesCancellations sx WHERE sx.SalesInvoiceId=si.Id) THEN 'canceled' ELSE si.Status END","si.WarehouseId");return @"SELECT COALESCE(root.InvoiceNumber,si.InvoiceNumber) number,si.InvoiceDate date,COALESCE(c.Name,'عميل نقدي') party,si.TotalScaled/10000.0 total,si.PaidScaled/10000.0 paid,CASE WHEN EXISTS(SELECT 1 FROM SalesCancellations sx WHERE sx.SalesInvoiceId=si.Id) THEN 0 ELSE MAX(0,si.TotalScaled-COALESCE((SELECT SUM(sr.TotalScaled) FROM SalesReturns sr WHERE sr.SalesInvoiceId=si.Id),0)-si.PaidScaled)/10000.0 END outstanding,CASE WHEN EXISTS(SELECT 1 FROM SalesCancellations sx WHERE sx.SalesInvoiceId=si.Id) THEN 'ملغاة' WHEN si.Status='posted' THEN 'مرحلة' ELSE 'مسودة' END status FROM SalesInvoices si LEFT JOIN Customers c ON c.Id=si.CustomerId LEFT JOIN SalesInvoices root ON root.Id=si.RootInvoiceId"+Where(w)+" ORDER BY si.InvoiceDate DESC,si.Id DESC";}
        private static string Purchases(IReadOnlyDictionary<string,string> f,DynamicParameters a){var w=new List<string>();var like=Like(f,a,new[]{"p.InvoiceNumber","s.Name"});if(like!=null)w.Add(like);AddDateAndCommon(f,a,w,"p.InvoiceDate","p.Status","p.WarehouseId");return @"SELECT p.InvoiceNumber number,p.InvoiceDate date,s.Name party,p.TotalScaled/10000.0 total,CASE WHEN p.Status='posted' THEN 'مرحلة' ELSE 'مسودة' END status FROM PurchaseInvoices p JOIN Suppliers s ON s.Id=p.SupplierId"+Where(w)+" ORDER BY p.InvoiceDate DESC,p.Id DESC";}
    }
}
