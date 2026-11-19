using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Printing
{
    public sealed class PrintingService
    {
        public static readonly string[] SectionKeys = { "company", "logo", "address", "phone", "taxInfo", "header", "title", "reference", "date", "cashier", "party", "items", "variant", "unitPrice", "discount", "tax", "quantity", "lineTotal", "subtotal", "discountTotal", "taxTotal", "grandTotal", "paid", "remaining", "footer", "policy", "partyPhone", "warehouse", "paymentMethod", "paymentNotes", "signature" };
        private static readonly string[] OptionalSectionKeys = { "partyPhone", "warehouse", "paymentMethod", "paymentNotes", "signature" };
        private readonly IPrintTemplateRepository _store;
        private readonly IPrintDocumentProvider _documents;
        private readonly IPrintDocumentSearch _search;
        private readonly IPrinterBackend _backend;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, byte> _active = new System.Collections.Concurrent.ConcurrentDictionary<long, byte>();
        public PrintingService(IPrintTemplateRepository store, IPrintDocumentProvider documents, IPrinterBackend backend, IPrintDocumentSearch search = null) { _store = store; _documents = documents; _backend = backend; _search = search; }
        public Task<System.Collections.Generic.IReadOnlyList<PrintDocumentChoice>> SearchDocumentsAsync(string type, string search, CancellationToken ct)
        {
            if (!PrintTypes.All.Contains(type) || (search ?? "").Length > 100) throw Invalid("اختر نوع مستند صحيحًا واكتب بحثًا أقصر.");
            if (_search == null) throw new PrintException("search_unavailable", "البحث عن المستندات غير متاح.");
            return _search.SearchAsync(type, (search ?? "").Trim(), 25, ct);
        }
        public Task<System.Collections.Generic.IReadOnlyList<PrinterInfo>> PrintersAsync(CancellationToken ct) => _backend.PrintersAsync(ct);
        public Task<System.Collections.Generic.IReadOnlyList<PrintJob>> JobsAsync(CancellationToken ct) => _store.JobsAsync(ct);
        public Task ReviewAsync(long jobId, long actorId, CancellationToken ct)
        {
            if (_active.ContainsKey(jobId)) throw new PrintException("print_busy", "المهمة لا تزال قيد الإرسال في هذه الجلسة.");
            return _store.ReviewAsync(jobId, actorId, ct);
        }
        public async Task<PrintingSettings> SettingsAsync(CancellationToken ct)
        {
            var settings = await _store.ReadAsync(ct).ConfigureAwait(false) ?? Defaults();
            if (settings.Templates != null) foreach (var template in settings.Templates) NormalizeTemplate(template);
            return settings;
        }
        public static PrintTemplate BuiltIn(string id, string name)
        {
            return new PrintTemplate { Id = id, Name = name, LogoWidthMm = 15, LogoHeightMm = 15, Sections = SectionKeys.Select(k => new PrintSection { Key = k, Visible = !OptionalSectionKeys.Contains(k), Bold = k == "company" || k == "grandTotal" || k == "title", Alignment = k == "company" || k == "title" || k == "footer" ? "center" : "right" }).ToList() };
        }
        public static PrintingSettings Defaults()
        {
            var s = new PrintingSettings();
            for (var i = 0; i < PrintTypes.All.Length; i++)
            {
                var key = PrintTypes.All[i]; s.Templates.Add(BuiltIn("builtin-" + key, PrintTypes.Titles[i]));
                s.Profiles.Add(new PrintProfile { DocumentType = key, TemplateId = "builtin-" + key });
            }
            return s;
        }
        public async Task<PrintingSettings> SaveAsync(PrintingSettings s, long actorId, CancellationToken ct)
        {
            Validate(s);
            var previous = await SettingsAsync(ct).ConfigureAwait(false);
            foreach (var printer in s.Printers)
            {
                var old = previous.Printers.FirstOrDefault(p => p.Id == printer.Id);
                var nextTemplate = s.Templates.Single(t => t.Id == printer.TemplateId);
                var oldTemplate = old == null ? null : previous.Templates.FirstOrDefault(t => t.Id == old.TemplateId);
                if (old == null || old.WindowsPrinterName != printer.WindowsPrinterName || old.TemplateId != printer.TemplateId || oldTemplate == null || !SamePaper(oldTemplate.Paper, nextTemplate.Paper) || oldTemplate.LogoPngBase64 != nextTemplate.LogoPngBase64)
                    await _backend.ValidateAsync(new PrintProfile { PrinterName = printer.WindowsPrinterName, Copies = 1 }, nextTemplate, ct).ConfigureAwait(false);
            }
            foreach (var p in s.Profiles.Where(p => string.IsNullOrWhiteSpace(p.PrinterId) && !string.IsNullOrWhiteSpace(p.PrinterName)))
                await _backend.ValidateAsync(p, s.Templates.Single(t => t.Id == p.TemplateId), ct).ConfigureAwait(false);
            foreach (var p in s.Profiles.Where(p => p.UseDocumentTemplate && !string.IsNullOrWhiteSpace(p.PrinterId)))
            {
                var printer = s.Printers.Single(x => x.Id == p.PrinterId);
                await _backend.ValidateAsync(new PrintProfile { DocumentType = p.DocumentType, PrinterName = printer.WindowsPrinterName, Copies = p.Copies }, s.Templates.Single(t => t.Id == p.TemplateId), ct).ConfigureAwait(false);
            }
            if (s.AutoPrintSale && string.IsNullOrWhiteSpace(Resolve(s, s.Profiles.Single(p => p.DocumentType == "sale")).Item1.PrinterName)) throw Invalid("اختر طابعة البيع قبل تفعيل الطباعة التلقائية.");
            if (s.AutoPrintSale && s.AutoPrintDelivery && string.IsNullOrWhiteSpace(Resolve(s, s.Profiles.Single(p => p.DocumentType == "delivery")).Item1.PrinterName)) throw Invalid("اختر طابعة بون التوصيل أو عطّل طباعته التلقائية.");
            return await _store.SaveAsync(s, actorId, ct).ConfigureAwait(false);
        }
        public static void Validate(PrintingSettings s)
        {
            if (s == null || s.Version < 0 || s.Templates == null || s.Templates.Count < 1 || s.Templates.Count > 40 || s.Profiles == null || s.Profiles.Count != PrintTypes.All.Length || s.Printers == null || s.Printers.Count > 40) throw Invalid();
            if (s.Printers.Any(p => p == null || string.IsNullOrWhiteSpace(p.Id) || p.Id.Length > 80 || !p.Id.All(c => char.IsLetterOrDigit(c) || c == '-') || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 100 || string.IsNullOrWhiteSpace(p.WindowsPrinterName) || p.WindowsPrinterName.Length > 250 || !s.Templates.Any(t => t.Id == p.TemplateId)) || s.Printers.Select(p => p.Id).Distinct().Count() != s.Printers.Count || s.Printers.Select(p => p.WindowsPrinterName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != s.Printers.Count) throw Invalid();
            if (s.Templates.Any(t => t == null) || s.Templates.Select(t => t.Id).Distinct().Count() != s.Templates.Count) throw Invalid();
            foreach (var t in s.Templates) ValidateTemplate(t);
            if (s.Profiles.Any(p => p == null) || s.Profiles.Select(p => p.DocumentType).Distinct().Count() != PrintTypes.All.Length) throw Invalid();
            foreach (var p in s.Profiles)
                if (!PrintTypes.All.Contains(p.DocumentType) || p.Copies < 1 || p.Copies > 10 || (p.PrinterName ?? "").Length > 250 || !s.Templates.Any(t => t.Id == p.TemplateId) || !string.IsNullOrWhiteSpace(p.PrinterId) && !s.Printers.Any(x => x.Id == p.PrinterId)) throw Invalid();
        }
        public static void ValidateTemplate(PrintTemplate t)
        {
            NormalizeTemplate(t);
            if (t == null || string.IsNullOrWhiteSpace(t.Id) || t.Id.Length > 80 || !t.Id.All(c => char.IsLetterOrDigit(c) || c == '-') || string.IsNullOrWhiteSpace(t.Name) || t.Name.Length > 100 || (t.HeaderText ?? "").Length > 1000 || (t.FooterText ?? "").Length > 1000 || (t.ReturnPolicy ?? "").Length > 1000 || (t.SignatureText ?? "").Length > 80 || (t.LogoPngBase64 ?? "").Length > 180000 || t.LogoWidthMm < 1 || t.LogoWidthMm > 100 || t.LogoHeightMm < 1 || t.LogoHeightMm > 100) throw Invalid();
            var p = t.Paper;
            if (p == null || !new[] { "58mm", "80mm", "A4", "A5", "custom" }.Contains(p.Preset) || p.WidthMm < 30 || p.WidthMm > 420 || p.HeightMm < 30 || p.HeightMm > 3000 || p.Scale < .5m || p.Scale > 2 || new[] { p.MarginLeftMm, p.MarginRightMm, p.MarginTopMm, p.MarginBottomMm }.Any(m => m < 0 || m > 100) || p.MarginLeftMm + p.MarginRightMm > p.WidthMm - 20 || p.MarginTopMm + p.MarginBottomMm > p.HeightMm - 20) throw Invalid();
            if (p.Preset == "58mm" && p.WidthMm != 58 || p.Preset == "80mm" && p.WidthMm != 80 || p.Preset == "A4" && (p.WidthMm != 210 || p.HeightMm != 297 || p.Continuous) || p.Preset == "A5" && (p.WidthMm != 148 || p.HeightMm != 210 || p.Continuous) || p.Continuous && p.Landscape) throw Invalid();
            if (t.NameColumnPercent < 35 || t.NameColumnPercent > 70 || t.QuantityColumnPercent < 10 || t.QuantityColumnPercent > 25 || t.NameColumnPercent + t.QuantityColumnPercent > 85) throw Invalid();
            if (t.LogoWidthMm > (p.Landscape ? p.HeightMm : p.WidthMm) - p.MarginLeftMm - p.MarginRightMm) throw Invalid("عرض الشعار أكبر من مساحة الطباعة.");
            if (t.Sections == null || t.Sections.Count != SectionKeys.Length || t.Sections.Any(x => x == null) || t.Sections.Select(x => x.Key).Distinct().Count() != SectionKeys.Length || t.Sections.Any(x => !SectionKeys.Contains(x.Key) || (x.Label ?? "").Length > 80 || x.FontSize < 6 || x.FontSize > 24 || x.SpacingMm < 0 || x.SpacingMm > 15 || !new[] { "right", "center", "left" }.Contains(x.Alignment))) throw Invalid();
        }
        private static void NormalizeTemplate(PrintTemplate template)
        {
            if (template?.Sections == null) return;
            foreach (var key in OptionalSectionKeys)
                if (!template.Sections.Any(section => section != null && section.Key == key))
                    template.Sections.Add(new PrintSection { Key = key, Visible = false });
        }
        public async Task<byte[]> PreviewAsync(PrintTemplate template, CancellationToken ct)
        {
            ValidateTemplate(template);
            return await _backend.PreviewAsync(Sample(), template, ct).ConfigureAwait(false);
        }
        public async Task<byte[]> PreviewAsync(PrintPreviewRequest request, CancellationToken ct)
        {
            if (request == null || !PrintTypes.All.Contains(request.DocumentType) || (request.BusinessName ?? "").Length > 160 || (request.Address ?? "").Length > 500 || (request.Phone ?? "").Length > 40) throw Invalid();
            ValidateTemplate(request.Template);
            var sample = Sample();
            sample.Type = request.DocumentType;
            sample.Title = PrintTypes.Titles[Array.IndexOf(PrintTypes.All, request.DocumentType)] + " — نموذج";
            sample.Fields["company"] = request.BusinessName;
            sample.Fields["address"] = request.Address;
            sample.Fields["phone"] = request.Phone;
            if (request.DocumentType == "delivery")
            {
                sample.Title = "بون توصيل — نموذج";
                sample.Fields["deliveryFee"] = "5.00";
                sample.Fields.Remove("subtotal"); sample.Fields.Remove("discountTotal"); sample.Fields.Remove("taxTotal"); sample.Fields.Remove("grandTotal"); sample.Fields.Remove("paid");
                sample.Fields["remaining"] = "30.50"; sample.Fields["collectAmount"] = "30.50";
                sample.Fields["header"] = "عنوان التوصيل: شارع النيل\nالهاتف: 01000000000\nالمندوب: أحمد\nحالة التوصيل: قيد التجهيز";
                foreach (var item in sample.Items) { item.UnitPrice = null; item.Total = null; }
            }
            return await _backend.PreviewAsync(sample, request.Template, ct).ConfigureAwait(false);
        }
        public async Task<byte[]> PreviewDocumentAsync(PrintPreviewRequest request, long actorId, CancellationToken ct)
        {
            if (request == null || !PrintTypes.All.Contains(request.DocumentType) || !request.DocumentId.HasValue || request.DocumentId.Value <= 0) throw Invalid("اختر مستندًا محفوظًا صحيحًا.");
            ValidateTemplate(request.Template);
            var document = await _documents.GetAsync(request.DocumentType, request.DocumentId.Value, actorId, true, ct).ConfigureAwait(false);
            if (document == null) throw new PrintException("not_found", "المستند المرحّل غير موجود.");
            return await _backend.PreviewAsync(document, request.Template, ct).ConfigureAwait(false);
        }
        public async Task<byte[]> SalePdfAsync(long id, long actorId, bool administrator, CancellationToken ct)
        {
            if (id <= 0) throw Invalid("اختر فاتورة بيع صحيحة.");
            var document = await _documents.GetAsync("sale", id, actorId, administrator, ct).ConfigureAwait(false);
            if (document == null) throw new PrintException("not_found", "فاتورة البيع المرحّلة غير موجودة أو لا تملك صلاحية الوصول إليها.");
            var settings = await SettingsAsync(ct).ConfigureAwait(false);
            var template = Resolve(settings, settings.Profiles.Single(p => p.DocumentType == "sale")).Item2;
            return await _backend.PdfAsync(document, template, ct).ConfigureAwait(false);
        }
        public async Task<PrintJob> SubmitAsync(PrintRequest r, long actorId, bool administrator, CancellationToken ct)
        {
            if (r == null || !PrintTypes.All.Contains(r.DocumentType) || r.DocumentId <= 0 || string.IsNullOrWhiteSpace(r.OperationKey) || r.OperationKey.Length > 100 || r.Automatic && (r.DocumentType != "sale" && r.DocumentType != "delivery" || r.Reprint)) throw Invalid();
            var document = await _documents.GetAsync(r.DocumentType, r.DocumentId, actorId, administrator, ct).ConfigureAwait(false);
            if (document == null) throw new PrintException("not_found", "المستند المرحّل غير موجود أو لا تملك صلاحية الوصول إليه.");
            var settings = await SettingsAsync(ct).ConfigureAwait(false);
            if (r.Automatic && (!settings.AutoPrintSale || r.DocumentType == "delivery" && !settings.AutoPrintDelivery)) return new PrintJob { Status = "disabled", Message = "الطباعة التلقائية معطلة." };
            var selected = Resolve(settings, settings.Profiles.Single(p => p.DocumentType == r.DocumentType));
            var profile = selected.Item1;
            var template = selected.Item2;
            var job = new PrintJob { RequestKey = r.Automatic ? "auto:" + r.DocumentType + ":" + r.DocumentId : "manual:" + actorId + ":" + r.OperationKey,
                DocumentType = r.DocumentType, DocumentId = r.DocumentId, Reference = document.Reference, ActorId = actorId,
                PrinterName = profile.PrinterName, TemplateId = template.Id, Copies = profile.Copies, Reprint = r.Reprint,
                CreatedUtc = DateTime.UtcNow.ToString("o"), Status = "accepted", Message = "تم قبول طلب الطباعة." };
            job = await _store.ClaimAsync(job, ct).ConfigureAwait(false);
            if (job.Replayed) return job;
            return await DispatchAsync(job, document, template, profile).ConfigureAwait(false);
        }
        private async Task<PrintJob> DispatchAsync(PrintJob job, PrintDocumentData document, PrintTemplate template, PrintProfile profile)
            => await RunOutputAsync(job, async () => { await _backend.ValidateAsync(profile, template, CancellationToken.None).ConfigureAwait(false); await _backend.SubmitAsync(document, template, profile, CancellationToken.None).ConfigureAwait(false); }).ConfigureAwait(false);
        public async Task<PrintJob> ReplayAsync(string requestKey, string hash, CancellationToken ct)
        {
            var job = await _store.FindAsync(requestKey, ct).ConfigureAwait(false);
            if (job != null) { if (job.RequestHash != hash) throw new PrintException("key_conflict", "مفتاح العملية مستخدم لدفعة مختلفة."); job.Replayed = true; }
            return job;
        }
        public async Task<PrintJob> SubmitOutputAsync(PrintJob job, Func<Task> output, CancellationToken ct)
        {
            job = await _store.ClaimAsync(job, ct).ConfigureAwait(false);
            return job.Replayed ? job : await RunOutputAsync(job, output).ConfigureAwait(false);
        }
        private async Task<PrintJob> RunOutputAsync(PrintJob job, Func<Task> output)
        {
            _active.TryAdd(job.Id, 0);
            // Once claimed, finish the diagnostic record even if the HTTP caller disconnects.
            try
            {
                await output().ConfigureAwait(false);
                job.Status = "submitted_unknown"; job.Message = "تم إرسال المهمة إلى Windows؛ خروج الورق غير مؤكد.";
            }
            catch (PrintException ex) { job.Status = "failed"; job.ErrorCode = ex.Code; job.Message = ex.Message; }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Print submission {0} failed: {1}", job.Id, ex.GetType().Name);
                job.Status = "uncertain"; job.ErrorCode = "submission_unknown"; job.Message = "تعذر تأكيد إرسال المهمة. تحقق من قائمة طباعة Windows قبل إعادة الطباعة.";
            }
            try { await _store.CompleteAsync(job, CancellationToken.None).ConfigureAwait(false); }
            finally { byte ignored; _active.TryRemove(job.Id, out ignored); }
            return job;
        }
        public async Task<PrintJob> TestAsync(string type, string operationKey, long actorId, CancellationToken ct)
        {
            if (!PrintTypes.All.Contains(type) || string.IsNullOrWhiteSpace(operationKey) || operationKey.Length > 100) throw Invalid();
            var s = await SettingsAsync(ct).ConfigureAwait(false); var selected = Resolve(s, s.Profiles.Single(x => x.DocumentType == type)); var p = selected.Item1; var t = selected.Item2;
            var job = await _store.ClaimAsync(new PrintJob { RequestKey = "test:" + actorId + ":" + operationKey, DocumentType = "test-" + type, DocumentId = 0, ActorId = actorId, Reference = "TEST-0001", TemplateId = t.Id, PrinterName = p.PrinterName, Copies = p.Copies, CreatedUtc = DateTime.UtcNow.ToString("o"), Status = "accepted", Message = "طلب اختبار الطباعة." }, ct).ConfigureAwait(false);
            return job.Replayed ? job : await DispatchAsync(job, Sample(), t, p).ConfigureAwait(false);
        }
        public static PrintDocumentData Sample() => new PrintDocumentData { Type = "sample", Title = "اختبار تصميم — غير مالي", Reference = "TEST-0001", Fields = new System.Collections.Generic.Dictionary<string, string> { ["company"] = "الخيال AlKhayal", ["date"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), ["party"] = "عميل تجريبي", ["partyPhone"] = "01000000000", ["warehouse"] = "المخزن الرئيسي", ["paymentMethod"] = "نقدي", ["paymentNotes"] = "ملاحظة تجريبية", ["subtotal"] = "125.50", ["discountTotal"] = "0.00", ["taxTotal"] = "0.00", ["grandTotal"] = "125.50", ["paid"] = "100.00", ["remaining"] = "25.50" }, Items = new System.Collections.Generic.List<PrintItem> { new PrintItem { Name = "قميص عربي English طويل لاختبار التفاف النص وقراءة الأرقام 123", Variant = "أزرق / XL", Sku = "SKU-001", Quantity = "2", UnitPrice = "62.75", Total = "125.50", Discount = "0.00", Tax = "0.00" } } };
        private static Tuple<PrintProfile, PrintTemplate> Resolve(PrintingSettings settings, PrintProfile assignment)
        {
            var printer = string.IsNullOrWhiteSpace(assignment.PrinterId) ? null : settings.Printers.Single(p => p.Id == assignment.PrinterId);
            var profile = new PrintProfile { DocumentType = assignment.DocumentType, PrinterId = assignment.PrinterId,
                PrinterName = printer == null ? assignment.PrinterName : printer.WindowsPrinterName,
                TemplateId = printer == null || assignment.UseDocumentTemplate ? assignment.TemplateId : printer.TemplateId, Copies = assignment.Copies };
            return Tuple.Create(profile, settings.Templates.Single(t => t.Id == profile.TemplateId));
        }
        private static bool SamePaper(PrintPaper a, PrintPaper b) => a.Preset == b.Preset && a.WidthMm == b.WidthMm && a.HeightMm == b.HeightMm && a.Continuous == b.Continuous && a.Landscape == b.Landscape && a.MarginLeftMm == b.MarginLeftMm && a.MarginRightMm == b.MarginRightMm && a.MarginTopMm == b.MarginTopMm && a.MarginBottomMm == b.MarginBottomMm && a.Scale == b.Scale;
        private static PrintException Invalid(string message = "راجع بيانات القالب والطابعة وأبعاد الورق.") => new PrintException("validation", message);
    }
}
