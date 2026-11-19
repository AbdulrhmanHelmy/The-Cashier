using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Exports
{
    public sealed class ExportService
    {
        private readonly IExportDataSource source;
        private readonly IDictionary<string, IExportFileWriter> writers;
        private readonly IDictionary<string, ExportDatasetDefinition> definitions;

        public ExportService(IExportDataSource source, IEnumerable<IExportFileWriter> writers)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.writers = (writers ?? throw new ArgumentNullException(nameof(writers))).ToDictionary(x => x.Format, StringComparer.OrdinalIgnoreCase);
            definitions = CreateDefinitions().ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyCollection<ExportDatasetDefinition> Definitions => definitions.Values.ToList();
        public ExportDatasetDefinition Find(string key) { ExportDatasetDefinition value; return key != null && definitions.TryGetValue(key, out value) ? value : null; }

        public async Task<ExportResult> ExportAsync(string dataset, string format, IReadOnlyDictionary<string, string> filters, CancellationToken ct)
        {
            var definition = Find(dataset);
            if (definition == null) throw new ArgumentException("مجموعة بيانات التصدير غير مدعومة.");
            IExportFileWriter writer;
            if (string.IsNullOrWhiteSpace(format) || !writers.TryGetValue(format, out writer)) throw new ArgumentException("صيغة ملف التصدير غير مدعومة.");
            var clean = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in filters ?? new Dictionary<string, string>())
            {
                if (!definition.SupportedFilters.Contains(item.Key, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("مرشح التصدير غير مدعوم: " + item.Key);
                if (!string.IsNullOrWhiteSpace(item.Value)) clean[item.Key] = item.Value.Trim();
            }
            var rows = await source.QueryAsync(new ExportQuery { Dataset = definition.Key, Filters = clean }, ct).ConfigureAwait(false);
            var bytes = writer.Write(new ExportDocument { Definition = definition, Rows = rows });
            return new ExportResult { Content = bytes, ContentType = writer.ContentType, FileName = definition.Key + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + writer.Extension };
        }

        private static IEnumerable<ExportDatasetDefinition> CreateDefinitions()
        {
            yield return D("products", "المنتجات", "products-list", null, new[] { "search", "categoryId", "isActive" },
                C("name","اسم المنتج",ExportValueType.Text,28), C("sku","كود المنتج / SKU",ExportValueType.Text,20), C("barcode","الباركود الأساسي",ExportValueType.Text,22), C("category","التصنيف",ExportValueType.Text,20), C("unit","وحدة البيع الرئيسية",ExportValueType.Text,18), C("purchasePrice","سعر الشراء",ExportValueType.Number,15), C("sellingPrice","سعر البيع",ExportValueType.Number,15), C("status","الحالة",ExportValueType.Text,13));
            yield return D("customers", "العملاء", "customers-list", "customers", new[] { "search", "status" }, C("name","الاسم",ExportValueType.Text,28), C("phone","الهاتف",ExportValueType.Text,18), C("address","العنوان",ExportValueType.Text,35), C("balance","الرصيد الحالي",ExportValueType.Number,16), C("status","الحالة",ExportValueType.Text,13));
            yield return D("suppliers", "الموردون", "suppliers-list", "suppliers", new[] { "search", "status" }, C("name","الاسم",ExportValueType.Text,28), C("phone","الهاتف",ExportValueType.Text,18), C("address","العنوان",ExportValueType.Text,35), C("balance","الرصيد المستحق",ExportValueType.Number,16), C("status","الحالة",ExportValueType.Text,13));
            yield return D("warehouses", "المخازن", "warehouse-directory", "inventory", new[] { "search", "isActive" }, C("name","اسم المخزن",ExportValueType.Text,28), C("code","الكود",ExportValueType.Text,18), C("status","الحالة",ExportValueType.Text,13));
            yield return D("sales", "فواتير البيع", "sales-list", "sales", new[] { "search", "from", "to", "status", "warehouseId" }, C("number","رقم الفاتورة",ExportValueType.Text,20), C("date","التاريخ",ExportValueType.Date,15), C("party","العميل",ExportValueType.Text,26), C("total","الإجمالي",ExportValueType.Number,16), C("paid","المدفوع",ExportValueType.Number,16), C("outstanding","المتبقي",ExportValueType.Number,16), C("status","الحالة",ExportValueType.Text,14));
            yield return D("purchases", "فواتير الشراء", "purchases-list", "purchasing", new[] { "search", "from", "to", "status", "warehouseId" }, C("number","رقم الفاتورة",ExportValueType.Text,20), C("date","التاريخ",ExportValueType.Date,15), C("party","المورد",ExportValueType.Text,26), C("total","الإجمالي",ExportValueType.Number,16), C("status","الحالة",ExportValueType.Text,14));
        }
        private static ExportColumn C(string key,string header,ExportValueType type,double width)=>new ExportColumn(key,header,type,width);
        private static ExportDatasetDefinition D(string key,string name,string permission,string feature,string[] filters,params ExportColumn[] columns)=>new ExportDatasetDefinition{Key=key,ArabicName=name,PermissionRoute=permission,FeatureKey=feature,SupportedFilters=filters,Columns=columns};
    }
}
