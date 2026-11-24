using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;

namespace AlKhayal.Application.Printing
{
    public sealed class LabelService
    {
        public const string DefaultBusinessName = "The Cashier";
        public static readonly string[] ElementKeys = { "productName", "company", "productCode", "sku", "barcode", "barcodeText", "price", "unit", "category", "static", "logo" };
        private readonly ILabelRepository _store;
        private readonly ILabelPrinterBackend _backend;
        private readonly PrintingService _printing;
        public LabelService(ILabelRepository store, ILabelPrinterBackend backend, PrintingService printing) { _store = store; _backend = backend; _printing = printing; }
        public Task<LabelSettings> SettingsAsync(CancellationToken ct) => Read(ct);
        public async Task<LabelSettings> AssignPrinterAsync(LabelPrinterAssignment request, long actor, CancellationToken ct)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.PrinterName) || request.PrinterName.Length > 250 ||
                (request.PaperName ?? "").Length > 100) throw Invalid("اختر طابعة ومقاس ورق صالحين.");
            var devices = await _printing.PrintersAsync(ct).ConfigureAwait(false);
            var device = devices.SingleOrDefault(x => x.Name == request.PrinterName);
            if (device == null) throw Invalid("طابعة Windows المحددة غير موجودة. حدّث القائمة.");
            if (device.Status != "available") throw Invalid("الطابعة غير متاحة الآن. تحقق من اتصالها وحالتها.");
            PrinterPaper paper = null;
            if (!string.IsNullOrEmpty(request.PaperName))
                paper = device.ActivePaper != null && device.ActivePaper.Name == request.PaperName ? device.ActivePaper :
                    device.Papers.SingleOrDefault(x => x.Name == request.PaperName);
            else if (!request.WidthMm.HasValue) paper = device.ActivePaper;
            var width = request.WidthMm ?? paper?.WidthMm;
            var height = request.HeightMm ?? paper?.HeightMm;
            if (!width.HasValue || !height.HasValue || width < 10 || width > 420 || height < 10 || height > 1000)
                throw Invalid("تعذر تحديد مقاس الورق من Windows. اختر نموذجًا مسجلًا أو أدخل مقاس الملصق الفعلي مرة واحدة.");
            if ((request.WidthMm.HasValue || request.HeightMm.HasValue) && (!request.WidthMm.HasValue || !request.HeightMm.HasValue))
                throw Invalid("أدخل العرض والارتفاع معًا.");
            if (request.WidthMm.HasValue)
                paper = device.Papers.Where(x => Math.Abs(x.WidthMm - width.Value) <= 1m && Math.Abs(x.HeightMm - height.Value) <= 1m)
                    .OrderBy(x => Math.Abs(x.WidthMm - width.Value) + Math.Abs(x.HeightMm - height.Value)).FirstOrDefault();
            var settings = await Read(ct).ConfigureAwait(false);
            if (settings.Version != request.Version) throw new PrintException("conflict", "تغيرت إعدادات الملصقات. حدّث الصفحة قبل الحفظ.");
            var profile = settings.Profiles.Single(x => x.Id == settings.DefaultProfileId);
            var id = "label-auto-" + Guid.NewGuid().ToString("N");
            var template = AssignmentTemplate(id, width.Value, height.Value, device);
            profile.PrinterName = device.Name;
            profile.PaperName = paper?.Name;
            profile.TemplateId = template.Id;
            profile.Copies = 1;
            settings.Templates.Add(template);
            var used = new HashSet<string>(settings.Profiles.Select(x => x.TemplateId));
            settings.Templates.RemoveAll(x => x.Id.StartsWith("label-auto-", StringComparison.Ordinal) && !used.Contains(x.Id));
            return await SaveAsync(settings, actor, ct).ConfigureAwait(false);
        }
        private static LabelTemplate AssignmentTemplate(string id, decimal width, decimal height, PrinterInfo device)
        {
            // Reserve the driver's nonprintable edges. The stock remains one label per page.
            var samePage = device?.ActivePaper != null && Math.Abs(device.ActivePaper.WidthMm - width) <= 3m && Math.Abs(device.ActivePaper.HeightMm - height) <= 3m;
            var left = samePage && device.PrintableWidthMm > 0 ? Math.Max(0m, device.PrintableXmm) + .3m : 1.5m;
            var top = samePage && device.PrintableHeightMm > 0 ? Math.Max(0m, device.PrintableYmm) + .3m : 1.5m;
            var right = samePage && device.PrintableWidthMm > 0 ? Math.Max(0m, width - device.PrintableXmm - device.PrintableWidthMm) + .3m : 1.5m;
            var bottom = samePage && device.PrintableHeightMm > 0 ? Math.Max(0m, height - device.PrintableYmm - device.PrintableHeightMm) + .3m : 1.5m;
            if (width - left - right < 12 || height - top - bottom < 12) throw Invalid("المساحة القابلة للطباعة صغيرة جدًا لهذا الملصق.");
            var template = BuiltIn(id, width - left - right, height - top - bottom);
            template.Stock.PageWidthMm = width; template.Stock.PageHeightMm = height;
            template.Stock.LeftMm = left; template.Stock.RightMm = right; template.Stock.TopMm = top; template.Stock.BottomMm = bottom;
            template.Name = "ملصق تلقائي " + Math.Round(width, 1) + " × " + Math.Round(height, 1) + " مم";
            return template;
        }
        private async Task<LabelSettings> Read(CancellationToken ct)
        {
            var settings = await _store.ReadAsync(ct).ConfigureAwait(false) ?? Defaults();
            // Compatibility projection only: retain the original persisted template JSON.
            foreach (var template in settings.Templates)
                template.Elements.RemoveAll(e => e.Key == "size" || e.Key == "color");
            if (settings.Design != null)
            {
                try { ValidateDesign(settings, settings.Design); }
                catch (PrintException ex) { System.Diagnostics.Trace.TraceWarning("Ignoring invalid saved label design: {0}", ex.Code); settings.Design = null; }
            }
            return settings;
        }
        public static LabelTemplate BuiltIn(string id = "label-basic", decimal width = 50, decimal height = 30)
        {
            var t = new LabelTemplate { Id = id, Name = "ملصق " + width + " × " + height, Stock = new LabelStock { LabelWidthMm = width, LabelHeightMm = height, PageWidthMm = width, PageHeightMm = height } };
            var ratio = height / 30m;
            t.Elements.Add(new LabelElement { Key = "productName", X = 1, Y = 1, Width = width - 2, Height = 6 * ratio, FontSize = height <= 20 ? 6 : 8, Bold = true });
            t.Elements.Add(new LabelElement { Key = "barcode", X = 1, Y = 8 * ratio, Width = width - 2, Height = 10 * ratio });
            t.Elements.Add(new LabelElement { Key = "barcodeText", X = 1, Y = 18 * ratio, Width = width - 2, Height = 4 * ratio, FontSize = height <= 20 ? 6 : 8, Alignment = "center" });
            t.Elements.Add(new LabelElement { Key = "price", X = 1, Y = 23 * ratio, Width = width - 2, Height = 5 * ratio, FontSize = height <= 20 ? 7 : 9, Bold = true, Alignment = "center" });
            foreach (var key in ElementKeys.Where(k => t.Elements.All(e => e.Key != k))) t.Elements.Add(new LabelElement { Key = key, Visible = false, X = 1, Y = 1, Width = width - 2, Height = Math.Min(5, height - 2) });
            return t;
        }
        public static LabelSettings Defaults()
        {
            var s = new LabelSettings(); s.Templates.Add(BuiltIn()); s.Profiles.Add(new LabelPrinterProfile { Id = "labels-default", Name = "طابعة الملصقات الافتراضية", TemplateId = "label-basic" }); return s;
        }
        public static void ValidateTemplate(LabelTemplate t)
        {
            if (t == null || !Id(t.Id) || string.IsNullOrWhiteSpace(t.Name) || t.Name.Length > 100 || (t.LogoPngBase64 ?? "").Length > 180000) throw Invalid();
            var s = t.Stock;
            if (s == null || s.LabelWidthMm < 10 || s.LabelWidthMm > 420 || s.LabelHeightMm < 10 || s.LabelHeightMm > 1000 || s.PageWidthMm < 10 || s.PageWidthMm > 420 || s.PageHeightMm < 10 || s.PageHeightMm > 3000 || s.Columns < 1 || s.Columns > 20 || s.Rows < 1 || s.Rows > 100 || s.Columns * s.Rows > 500 || s.Continuous && (s.Columns != 1 || s.Rows != 1 || s.Landscape)) throw Invalid();
            if (new[] { s.LeftMm, s.RightMm, s.TopMm, s.BottomMm, s.HorizontalGapMm, s.VerticalGapMm }.Any(n => n < 0 || n > 100)) throw Invalid();
            var width = s.Landscape ? s.PageHeightMm : s.PageWidthMm; var height = s.Landscape ? s.PageWidthMm : s.PageHeightMm;
            if (s.Continuous && s.TopMm + s.LabelHeightMm + s.VerticalGapMm + s.BottomMm > height) throw Invalid("ارتفاع صفحة تعريف الرول يجب أن يشمل الملصق والفاصل الرأسي والهوامش؛ الصفحة هي خطوة تغذية الملصق.");
            if (s.LeftMm + s.RightMm + s.Columns * s.LabelWidthMm + (s.Columns - 1) * s.HorizontalGapMm > width || s.TopMm + s.BottomMm + s.Rows * s.LabelHeightMm + (s.Rows - 1) * s.VerticalGapMm > height) throw Invalid("خلايا الملصقات والهوامش والفواصل تتجاوز أبعاد الصفحة.");
            if (t.Elements == null || t.Elements.Count != ElementKeys.Length || t.Elements.Any(e => e == null) || t.Elements.Select(e => e.Key).Distinct().Count() != ElementKeys.Length) throw Invalid();
            foreach (var e in t.Elements)
                if (!ElementKeys.Contains(e.Key) || e.X < 0 || e.Y < 0 || e.Width <= 0 || e.Height <= 0 || e.X + e.Width > s.LabelWidthMm || e.Y + e.Height > s.LabelHeightMm || e.FontSize < 5 || e.FontSize > 36 || e.ModuleMm < .15m || e.ModuleMm > 1.2m || (e.Text ?? "").Length > 300 || !new[] { "right", "left", "center" }.Contains(e.Alignment)) throw Invalid("راجع موضع العنصر وأبعاده وخطه داخل الملصق.");
            // The barcode rectangle includes mandatory quiet zones; other elements cannot cover it.
            var bar = t.Elements.Single(e => e.Key == "barcode");
            if (bar.Visible && t.Elements.Any(e => e.Visible && e != bar && e.X < bar.X + bar.Width && e.X + e.Width > bar.X && e.Y < bar.Y + bar.Height && e.Y + e.Height > bar.Y)) throw Invalid("لا تضع نصًا أو شعارًا فوق الباركود أو مساحة الهدوء المحيطة به.");
            if (t.Elements.All(e => !e.Visible)) throw Invalid();
        }
        public async Task<LabelSettings> SaveAsync(LabelSettings s, long actor, CancellationToken ct)
        {
            if (s == null || s.Version < 0 || s.Templates == null || s.Templates.Count < 1 || s.Templates.Count > 40 || s.Templates.Any(t => t == null) || s.Templates.Select(t => t.Id).Distinct().Count() != s.Templates.Count || s.Profiles == null || s.Profiles.Count < 1 || s.Profiles.Count > 20 || s.Profiles.Any(p => p == null) || s.Profiles.Select(p => p.Id).Distinct().Count() != s.Profiles.Count || !s.Profiles.Any(p => p.Id == s.DefaultProfileId)) throw Invalid();
            foreach (var t in s.Templates) ValidateTemplate(t);
            ValidateDesign(s, s.Design);
            foreach (var p in s.Profiles)
            {
                if (!Id(p.Id) || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 100 || (p.PrinterName ?? "").Length > 250 || (p.PaperName ?? "").Length > 100 || p.Copies < 1 || p.Copies > 10 || !s.Templates.Any(t => t.Id == p.TemplateId)) throw Invalid();
                if (!string.IsNullOrWhiteSpace(p.PrinterName)) await _backend.ValidateLabelsAsync(p, ApplyDesign(s.Templates.Single(t => t.Id == p.TemplateId), s.Design), ct).ConfigureAwait(false);
            }
            return await _store.SaveAsync(s, actor, ct).ConfigureAwait(false);
        }
        public async Task<byte[]> PreviewAsync(LabelTemplate t, CancellationToken ct)
        {
            ValidateTemplate(t);
            var sample = await SampleAsync(2, ct).ConfigureAwait(false);
            return await _backend.PreviewLabelsAsync(sample, t, ct).ConfigureAwait(false);
        }
        public async Task<LabelSettings> SaveDesignAsync(LabelDesignRequest request, long actor, CancellationToken ct)
        {
            if (request == null) throw Invalid();
            var settings = await Read(ct).ConfigureAwait(false);
            CheckDesignVersion(settings, request.Version);
            ValidateDesign(settings, request.Elements);
            if (request.Elements != null)
            {
                var template = settings.Templates.Single(t => t.Id == settings.Profiles.Single(p => p.Id == settings.DefaultProfileId).TemplateId);
                var sample = await SampleAsync(1, ct).ConfigureAwait(false);
                await _backend.PreviewLabelsAsync(sample, ApplyDesign(template, request.Elements), ct).ConfigureAwait(false);
            }
            settings.Design = request.Elements;
            return await _store.SaveAsync(settings, actor, ct).ConfigureAwait(false);
        }
        public async Task<byte[]> PreviewDesignAsync(LabelDesignPreviewRequest request, CancellationToken ct)
        {
            if (request == null) throw Invalid();
            var settings = await Read(ct).ConfigureAwait(false);
            CheckDesignVersion(settings, request.Version);
            var profile = settings.Profiles.Single(p => p.Id == settings.DefaultProfileId);
            var template = settings.Templates.Single(t => t.Id == profile.TemplateId);
            var stock = request.StockOverride;
            if (stock != null)
            {
                if (!stock.WidthMm.HasValue || !stock.HeightMm.HasValue || stock.WidthMm < 10 || stock.WidthMm > 420 || stock.HeightMm < 10 || stock.HeightMm > 1000)
                    throw Invalid("أدخل عرضًا وارتفاعًا صالحين بالملليمتر.");
                var device = string.IsNullOrEmpty(stock.PrinterName) ? null :
                    (await _printing.PrintersAsync(ct).ConfigureAwait(false)).SingleOrDefault(p => p.Name == stock.PrinterName);
                if (stock.WidthMm != template.Stock.PageWidthMm || stock.HeightMm != template.Stock.PageHeightMm || stock.PrinterName != (profile.PrinterName ?? ""))
                    template = AssignmentTemplate(template.Id, stock.WidthMm.Value, stock.HeightMm.Value, device);
            }
            var sample = await SampleAsync(1, ct).ConfigureAwait(false);
            return await _backend.PreviewLabelsAsync(sample, ApplyDesign(template, request.Elements), ct).ConfigureAwait(false);
        }
        private static void CheckDesignVersion(LabelSettings settings, long version)
        {
            if (settings.Version != version) throw new PrintException("conflict", "تغيرت إعدادات الملصقات. حدّث الصفحة قبل المتابعة.");
        }
        private static void ValidateDesign(LabelSettings settings, List<LabelDesignElement> design)
        {
            if (design == null) return;
            foreach (var template in settings.Templates) ApplyDesign(template, design);
        }
        private static LabelTemplate ApplyDesign(LabelTemplate template, List<LabelDesignElement> design)
        {
            if (design == null) return template;
            if (design.Count > ElementKeys.Length || design.Any(e => e == null || !ElementKeys.Contains(e.Key) ||
                e.FontSize < 5 || e.FontSize > 36 || e.FontSize % .25m != 0 || !new[] { "left", "center", "right" }.Contains(e.Alignment) ||
                (e.Text ?? "").Length > 300) || design.Select(e => e.Key).Distinct().Count() != design.Count ||
                !design.Any(e => e.Key == "barcode" && e.Visible))
                throw Invalid("راجع عناصر التصميم. يجب أن يبقى رسم الباركود ظاهرًا.");

            var visible = design.Where(e => e.Visible).ToList();
            var originalBarcode = template.Elements.Single(e => e.Key == "barcode");
            var barHeight = Math.Max(4m, originalBarcode.Height);
            const decimal padding = 1m;
            const decimal gap = .35m;
            var textSpace = template.Stock.LabelHeightMm - 2 * padding - (visible.Count - 1) * gap - barHeight;
            const decimal minimum = 2.5m;
            var textCount = visible.Count - 1;
            if (textSpace < textCount * minimum)
                throw Invalid("العناصر المفعّلة لا تتسع في هذا المقاس. قلّل عددها أو اختر مقاسًا أكبر.");
            var desired = visible.Where(e => e.Key != "barcode").Sum(e => Math.Max(0, e.FontSize * .47m - minimum));
            var scale = desired == 0m ? 0m : Math.Min(1m, (textSpace - textCount * minimum) / desired);
            var y = padding + (template.Stock.LabelHeightMm - 2 * padding - barHeight - textCount * (minimum + gap) - desired * scale) / 2;
            var result = new LabelTemplate { Id = template.Id, Name = template.Name, Stock = template.Stock,
                LogoPngBase64 = template.LogoPngBase64, UseConfiguredTextAlignment = true };
            foreach (var part in visible)
            {
                var barcode = part.Key == "barcode";
                var height = barcode ? barHeight : minimum + Math.Max(0, part.FontSize * .47m - minimum) * scale;
                result.Elements.Add(new LabelElement { Key = part.Key, X = barcode ? originalBarcode.X : padding,
                    Width = barcode ? originalBarcode.Width : template.Stock.LabelWidthMm - 2 * padding, Y = y, Height = height,
                    FontSize = part.FontSize, Alignment = part.Alignment, Bold = part.Bold, Text = part.Text, ModuleMm = originalBarcode.ModuleMm });
                y += height + gap;
            }
            foreach (var key in ElementKeys.Where(k => result.Elements.All(e => e.Key != k)))
                result.Elements.Add(new LabelElement { Key = key, Visible = false, Width = 1, Height = 1 });
            ValidateTemplate(result);
            return result;
        }
        public async Task<ProductLabel> ProductInfoAsync(long barcodeId, string format, CancellationToken ct)
        {
            if (barcodeId <= 0 || format != "code128" && format != "ean13") throw Invalid("اختر باركودًا محفوظًا صالحًا.");
            return (await _store.ProductsAsync(new[] { new LabelSelection { BarcodeId = barcodeId, Quantity = 1, Format = format } }, ct).ConfigureAwait(false)).Single();
        }
        public async Task<LabelPreviewResult> PreviewProductAsync(LabelPreviewRequest r, CancellationToken ct)
        {
            if (r == null || r.BarcodeId <= 0 || r.Format != "code128" && r.Format != "ean13" || (r.ProfileId ?? "").Length > 80 || (r.TemplateId ?? "").Length > 80) throw Invalid("اختر باركودًا وصيغة صحيحة للمعاينة.");
            var s = await Read(ct).ConfigureAwait(false);
            var p = s.Profiles.SingleOrDefault(x => x.Id == (string.IsNullOrEmpty(r.ProfileId) ? s.DefaultProfileId : r.ProfileId));
            if (p == null) throw Invalid("ملف طابعة الملصقات غير موجود.");
            var t = s.Templates.SingleOrDefault(x => x.Id == (string.IsNullOrEmpty(r.TemplateId) ? p.TemplateId : r.TemplateId));
            if (t == null) throw Invalid("قالب الملصق غير موجود.");
            t = ApplyDesign(t, s.Design);
            ValidateTemplate(t);
            var data = await _store.ProductsAsync(new[] { new LabelSelection { BarcodeId = r.BarcodeId, Quantity = 1, Format = r.Format } }, ct).ConfigureAwait(false);
            var label = data.Single();
            var png = await _backend.PreviewLabelsAsync(new LabelBatch { Items = data }, t, ct).ConfigureAwait(false);
            string printerMessage = null;
            if (string.IsNullOrWhiteSpace(p.PrinterName)) printerMessage = "لم يتم تعيين طابعة لطباعة باركود المنتجات. افتح إعدادات الطابعات، واختر طابعة Windows في ملف الملصقات الافتراضي، ثم احفظ.";
            else try { await _backend.ValidateLabelsAsync(p, t, ct).ConfigureAwait(false); } catch (PrintException ex) { printerMessage = ex.Message; }
            return new LabelPreviewResult { Png = png, Barcode = label.Barcode, ProductName = label.Fields["productName"], Unit = label.Fields["unit"], Price = label.Fields["price"], ProfileId = p.Id, TemplateId = t.Id, PrinterName = p.PrinterName, PageWidthMm = t.Stock.PageWidthMm, PageHeightMm = t.Stock.PageHeightMm, Copies = p.Copies, PrinterConfigured = printerMessage == null, PrinterMessage = printerMessage };
        }
        private async Task<LabelBatch> SampleAsync(int quantity, CancellationToken ct)
        {
            var sample = Sample();
            sample.Items[0].Quantity = quantity;
            sample.Items[0].Fields["company"] = await _store.BusinessNameAsync(ct).ConfigureAwait(false);
            return sample;
        }
        public static LabelBatch Sample() => new LabelBatch { Items = new List<ProductLabel> { new ProductLabel { ProductId = 1, VariantId = 1, BarcodeId = 1, Barcode = "2912345678", Format = "code128", Quantity = 2, Fields = new Dictionary<string, string> { ["productName"] = "قميص عربي English", ["company"] = DefaultBusinessName, ["productCode"] = "P-000001", ["sku"] = "SHIRT-XL", ["price"] = "125.50 ج.م", ["unit"] = "قطعة", ["category"] = "ملابس" } } } };
        public async Task<PrintJob> SubmitAsync(LabelBatchRequest r, long actor, CancellationToken ct)
        {
            if (r == null || string.IsNullOrWhiteSpace(r.OperationKey) || r.OperationKey.Length > 100 || r.Items == null || r.Items.Count < 1 || r.Items.Count > 200 || r.Items.Any(i => i == null || i.BarcodeId <= 0 || i.Quantity < 1 || i.Quantity > 1000 || i.Format != "code128" && i.Format != "ean13") || r.Items.Select(i => i.BarcodeId).Distinct().Count() != r.Items.Count || r.Items.Sum(i => i.Quantity) > 2000 || r.StartCell < 0 || (r.ProfileId ?? "").Length > 80 || (r.TemplateId ?? "").Length > 80) throw Invalid("اختر حتى 200 باركود وبحد أقصى 2000 ملصق، وكمية موجبة لكل بند.");
            var canonical = r.ProfileId + "|" + r.TemplateId + "|" + r.StartCell + "|" + r.Reprint + "|" + r.ExactQuantity + "|" + string.Join(";", r.Items.Select(i => i.BarcodeId + ":" + i.Quantity + ":" + i.Format));
            string hash; using (var sha = SHA256.Create()) hash = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
            var requestKey = "labels:" + actor + ":" + r.OperationKey; var replay = await _printing.ReplayAsync(requestKey, hash, ct).ConfigureAwait(false); if (replay != null) return replay;
            var s = await Read(ct).ConfigureAwait(false); var p = s.Profiles.SingleOrDefault(x => x.Id == (string.IsNullOrEmpty(r.ProfileId) ? s.DefaultProfileId : r.ProfileId)); if (p == null) throw Invalid();
            if (r.ExactQuantity) p = new LabelPrinterProfile { Id = p.Id, Name = p.Name, PrinterName = p.PrinterName, PaperName = p.PaperName, TemplateId = p.TemplateId, Copies = 1 };
            var t = s.Templates.SingleOrDefault(x => x.Id == (string.IsNullOrEmpty(r.TemplateId) ? p.TemplateId : r.TemplateId)); if (t == null || r.StartCell >= t.Stock.Columns * t.Stock.Rows || r.Items.Sum(i => i.Quantity) * p.Copies > 2000) throw Invalid();
            t = ApplyDesign(t, s.Design);
            var data = await _store.ProductsAsync(r.Items, ct).ConfigureAwait(false);
            var batch = new LabelBatch { Items = data, StartCell = r.StartCell };
            var summary = "{\"settingsVersion\":" + s.Version + ",\"startCell\":" + r.StartCell + ",\"items\":[" + string.Join(",", r.Items.Select(i => "{\"barcodeId\":" + i.BarcodeId + ",\"quantity\":" + i.Quantity + ",\"format\":\"" + i.Format + "\"}")) + "]}";
            var job = new PrintJob { RequestKey = requestKey, RequestHash = hash, BatchSummaryJson = summary, DocumentType = "labels", DocumentId = actor, ActorId = actor, Reference = "LABELS-" + r.Items.Sum(i => i.Quantity), PrinterName = p.PrinterName, TemplateId = t.Id, Copies = p.Copies, Reprint = r.Reprint, CreatedUtc = DateTime.UtcNow.ToString("o"), Status = "accepted", Message = "قُبل طلب دفعة الملصقات." };
            return await _printing.SubmitOutputAsync(job, async () => { await _backend.ValidateLabelsAsync(p, t, CancellationToken.None).ConfigureAwait(false); await _backend.SubmitLabelsAsync(batch, t, p, CancellationToken.None).ConfigureAwait(false); }, ct).ConfigureAwait(false);
        }
        public async Task<PrintJob> TestAsync(string profileId, string operationKey, long actor, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(operationKey) || operationKey.Length > 100) throw Invalid();
            var s = await Read(ct).ConfigureAwait(false); var p = s.Profiles.SingleOrDefault(x => x.Id == profileId); if (p == null) throw Invalid(); var t = ApplyDesign(s.Templates.Single(x => x.Id == p.TemplateId), s.Design);
            return await _printing.SubmitOutputAsync(new PrintJob { RequestKey = "label-test:" + actor + ":" + operationKey, RequestHash = "label-test:" + profileId, DocumentType = "label-test", DocumentId = actor, ActorId = actor, Reference = "LABEL-TEST", PrinterName = p.PrinterName, TemplateId = t.Id, Copies = p.Copies, CreatedUtc = DateTime.UtcNow.ToString("o"), Status = "accepted", Message = "اختبار ملصق غير تجاري." }, async () => { await _backend.ValidateLabelsAsync(p, t, CancellationToken.None).ConfigureAwait(false); var sample = await SampleAsync(2, CancellationToken.None).ConfigureAwait(false); await _backend.SubmitLabelsAsync(sample, t, p, CancellationToken.None).ConfigureAwait(false); }, ct).ConfigureAwait(false);
        }
        private static bool Id(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 80 && id.All(c => char.IsLetterOrDigit(c) || c == '-');
        private static PrintException Invalid(string message = "راجع قالب الملصق وأبعاد الورق وإعدادات الطابعة.") => new PrintException("validation", message);
    }
}
