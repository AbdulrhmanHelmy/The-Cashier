using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Printing;

namespace AlKhayal.Printing
{
    public sealed partial class WindowsPrinterBackend
    {
        private readonly ILabelRenderer _labels = new NativeLabelRenderer();
        public static LabelBatch ExpandLabelCopies(LabelBatch batch, int copies)
        {
            if (batch == null || batch.Items == null || copies < 1 || copies > 10)
                throw new PrintException("validation", "عدد نسخ الملصقات غير صالح.");
            var expanded = new LabelBatch { StartCell = batch.StartCell };
            foreach (var item in batch.Items)
            {
                var count = checked(item.Quantity * copies);
                if (count > 2000) throw new PrintException("validation", "دفعة الملصقات تتجاوز 2000 ملصق.");
                expanded.Items.Add(new ProductLabel { ProductId = item.ProductId, VariantId = item.VariantId,
                    BarcodeId = item.BarcodeId, Barcode = item.Barcode, Format = item.Format,
                    Quantity = count, Fields = item.Fields });
            }
            if (expanded.Items.Sum(x => x.Quantity) > 2000)
                throw new PrintException("validation", "دفعة الملصقات تتجاوز 2000 ملصق.");
            return expanded;
        }
        private static PrintPaper LabelPaper(LabelTemplate t) => new PrintPaper { Preset = "custom", WidthMm = t.Stock.PageWidthMm, HeightMm = t.Stock.PageHeightMm, Landscape = t.Stock.Landscape, MarginLeftMm = t.Stock.LeftMm, MarginRightMm = t.Stock.RightMm, MarginTopMm = t.Stock.TopMm, MarginBottomMm = t.Stock.BottomMm };
        private static PrintDocument CreateLabel(LabelPrinterProfile profile, LabelTemplate template)
        {
            if (!string.IsNullOrEmpty(profile.PaperName))
            {
                var settings = new PrinterSettings { PrinterName = profile.PrinterName };
                if (!settings.IsValid) throw new PrintException("printer_not_found", "طابعة الباركود المحفوظة غير موجودة.");
                var active = ActivePaper(settings, settings.DefaultPageSettings);
                var form = active != null && active.Name == profile.PaperName ? active :
                    settings.PaperSizes.Cast<PaperSize>().Where(x => x.PaperName == profile.PaperName).Select(x => new PrinterPaper { Name = x.PaperName, WidthMm = (decimal)Mm(x.Width), HeightMm = (decimal)Mm(x.Height) }).SingleOrDefault();
                if (form == null)
                    throw new PrintException("unsupported_paper", "تغيّر مقاس نموذج الورق المحفوظ أو اختفى. حدّث معلومات الطابعة وأعد حفظ تعيين الباركود.");
            }
            var doc = Create(profile.PrinterName, 1, LabelPaper(template), (float)template.Stock.PageHeightMm, profile.PaperName);
            try
            {
                var page = doc.DefaultPageSettings;
                var actual = ActivePaper(doc.PrinterSettings, page);
                if (actual == null || Math.Abs(actual.WidthMm - template.Stock.PageWidthMm) > 1m || Math.Abs(actual.HeightMm - template.Stock.PageHeightMm) > 1m ||
                    page.PrinterResolution.X <= 0 || page.PrinterResolution.Y <= 0 || page.PrintableArea.Width <= 0 || page.PrintableArea.Height <= 0)
                    throw new PrintException("unsupported_paper", "تعريف الطابعة لا يؤكد مقاس صفحة الملصق أو دقتها (" + (actual == null ? "مقاس غير معلن" : actual.WidthMm + "×" + actual.HeightMm + " مم") + "; " + page.PrinterResolution.X + "×" + page.PrinterResolution.Y + " DPI). أضف مقاسًا مطابقًا في تفضيلات طابعة Windows ثم احفظ التعيين مجددًا.");
                return doc;
            }
            catch { doc.Dispose(); throw; }
        }
        public Task ValidateLabelsAsync(LabelPrinterProfile p, LabelTemplate t, CancellationToken ct) => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested(); LabelService.ValidateTemplate(t);
            using (var doc = CreateLabel(p, t)) { CheckStatus(p.PrinterName); }
            if (!string.IsNullOrEmpty(t.LogoPngBase64)) using (var image = NativePrintRenderer.DecodeLogo(t.LogoPngBase64)) { }
        }, ct);
        public Task SubmitLabelsAsync(LabelBatch batch, LabelTemplate t, LabelPrinterProfile p, CancellationToken ct) => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            // Repeat individual labels in the application. Driver copies repeat whole sheets,
            // which multiplies every batch quantity and reprints empty start cells.
            var expanded = ExpandLabelCopies(batch, p.Copies);
            using (var doc = CreateLabel(p, t))
            {
                CheckStatus(p.PrinterName); var placements = _labels.Layout(expanded, t); var pages = placements.Max(x => x.Page) + 1;
                using (var measure = doc.PrinterSettings.CreateMeasurementGraphics(doc.DefaultPageSettings))
                { var a = doc.DefaultPageSettings.PrintableArea; _labels.Validate(measure, expanded, t, new RectangleF(Mm(a.X), Mm(a.Y), Mm(a.Width), Mm(a.Height))); }
                var page = 0; doc.DocumentName = "The Cashier Product Labels"; doc.PrintController = new StandardPrintController();
                doc.PrintPage += (sender, e) =>
                {
                    foreach (var cell in placements.Where(x => x.Page == page)) _labels.Draw(e.Graphics, cell.Label, t, cell.X - Mm(e.PageSettings.HardMarginX), cell.Y - Mm(e.PageSettings.HardMarginY));
                    page++; e.HasMorePages = page < pages;
                };
                try { doc.Print(); }
                catch (Exception ex) { throw new InvalidOperationException("Label submission may have partially reached the spooler.", ex); }
            }
        }, ct);
        public Task<byte[]> PreviewLabelsAsync(LabelBatch batch, LabelTemplate t, CancellationToken ct) => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested(); var s = t.Stock; var width = (float)(s.Landscape ? s.PageHeightMm : s.PageWidthMm); var height = (float)(s.Landscape ? s.PageWidthMm : s.PageHeightMm);
            const float dpi = 300; var pixelWidth = (int)Math.Ceiling(width / 25.4f * dpi); var pixelHeight = (int)Math.Ceiling(height / 25.4f * dpi);
            if ((long)pixelWidth * pixelHeight > 16000000) throw new PrintException("preview_too_large", "الصفحة أكبر من حد المعاينة الآمن. استخدم صفحة أقصر للمعاينة؛ الطباعة تعتمد إمكانات التعريف.");
            using (var image = new Bitmap(pixelWidth, pixelHeight))
            {
                image.SetResolution(dpi, dpi);
                using (var g = Graphics.FromImage(image))
                {
                    g.Clear(Color.White); _labels.Validate(g, batch, t, new RectangleF(0, 0, width, height));
                    foreach (var placement in _labels.Layout(batch, t).Where(x => x.Page == 0)) _labels.Draw(g, placement.Label, t, placement.X, placement.Y);
                }
                using (var stream = new MemoryStream()) { image.Save(stream, ImageFormat.Png); return stream.ToArray(); }
            }
        }, ct);
    }
}
