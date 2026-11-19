using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Printing;
using AlKhayal.Printing;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class PrintingLayoutTests
    {
        [Fact]
        public void Legacy_templates_gain_hidden_optional_fields_without_changing_existing_sections()
        {
            var template = PrintingService.BuiltIn("legacy", "قديم");
            template.Sections.RemoveAll(s => new[] { "partyPhone", "warehouse", "paymentMethod", "paymentNotes", "signature" }.Contains(s.Key));
            PrintingService.ValidateTemplate(template);
            Assert.All(template.Sections.Where(s => new[] { "partyPhone", "warehouse", "paymentMethod", "paymentNotes", "signature" }.Contains(s.Key)), s => Assert.False(s.Visible));
            Assert.Equal(PrintingService.SectionKeys.Length, template.Sections.Count);
        }
        [Fact]
        public void Thermal_optional_fields_columns_and_typography_use_the_saved_template()
        {
            var template = PrintingService.BuiltIn("options", "إيصال"); template.Paper.Preset = "80mm"; template.Paper.WidthMm = 80;
            template.Sections.Single(s => s.Key == "cashier").Visible = false;
            template.Sections.Single(s => s.Key == "partyPhone").Visible = true;
            template.Sections.Single(s => s.Key == "paymentMethod").Visible = true;
            template.Sections.Single(s => s.Key == "quantity").Visible = false;
            template.Sections.Single(s => s.Key == "grandTotal").UseTypography = true;
            template.Sections.Single(s => s.Key == "grandTotal").FontSize = 15;
            template.ShowSku = true; template.EnableExtendedItemDetails = true;
            var document = PrintingService.Sample(); document.Type = "sale"; document.Fields["cashier"] = "كاشير محفوظ";
            using (var bitmap = new Bitmap(10, 10)) using (var graphics = Graphics.FromImage(bitmap))
            {
                var rows = new NativePrintRenderer().Layout(graphics, document, template, 74);
                var texts = rows.SelectMany(row => row.Cells).Select(cell => cell.Text).ToList();
                Assert.Contains(texts, value => value.Contains("01000000000"));
                Assert.Contains(texts, value => value.Contains("SKU-001"));
                Assert.Contains(texts, value => value.Contains("نقدي"));
                Assert.DoesNotContain(texts, value => value.Contains("كاشير محفوظ") || value == "الكمية");
                Assert.Contains(rows, row => row.FontPoints >= 15 && row.Cells.Any(cell => cell.Text.Contains("125.50")));
            }
        }
        [Fact]
        public void Thermal_section_order_moves_content_in_native_output()
        {
            var template = PrintingService.BuiltIn("ordered", "إيصال"); template.UseSectionOrder = true;
            var company = template.Sections.Single(s => s.Key == "company");
            template.Sections.Remove(company); template.Sections.Add(company);
            using (var bitmap = new Bitmap(10, 10)) using (var graphics = Graphics.FromImage(bitmap))
            {
                var rows = new NativePrintRenderer().Layout(graphics, PrintingService.Sample(), template, 74);
                Assert.True(rows.FindIndex(row => row.Cells.Any(cell => cell.Text.Contains("TEST-0001"))) < rows.FindIndex(row => row.Cells.Any(cell => cell.Text.Contains("الخيال AlKhayal"))));
            }
        }
        [Fact]
        public void Wide_invoice_discount_and_tax_columns_follow_visibility()
        {
            var template = PrintingService.BuiltIn("wide-columns", "فاتورة"); template.Paper.Preset = "A4"; template.Paper.WidthMm = 210; template.Paper.HeightMm = 297; template.EnableExtendedItemDetails = true;
            using (var bitmap = new Bitmap(10, 10)) using (var graphics = Graphics.FromImage(bitmap))
            {
                var renderer = new NativePrintRenderer();
                var rows = renderer.Layout(graphics, PrintingService.Sample(), template, 204);
                Assert.Contains(rows.Where(row => row.SectionKey == "items").SelectMany(row => row.Cells), cell => cell.Text == "الخصم");
                Assert.Contains(rows.Where(row => row.SectionKey == "items").SelectMany(row => row.Cells), cell => cell.Text == "الضريبة");
                template.Sections.Single(s => s.Key == "discount").Visible = false;
                template.Sections.Single(s => s.Key == "tax").Visible = false;
                rows = renderer.Layout(graphics, PrintingService.Sample(), template, 204);
                Assert.DoesNotContain(rows.Where(row => row.SectionKey == "items").SelectMany(row => row.Cells), cell => cell.Text == "الخصم" || cell.Text == "الضريبة");
            }
        }
        [Fact]
        public void Saved_section_label_and_visibility_apply_to_native_80mm_layout()
        {
            var template = PrintingService.BuiltIn("custom-label", "إيصال");
            template.Sections.Single(s => s.Key == "grandTotal").Label = "المبلغ المستحق";
            template.Sections.Single(s => s.Key == "cashier").Visible = false;
            var document = PrintingService.Sample(); document.Type = "sale"; document.Fields["cashier"] = "اسم الكاشير الحقيقي";
            using (var bitmap = new Bitmap(10, 10)) using (var graphics = Graphics.FromImage(bitmap))
            {
                var cells = new NativePrintRenderer().Layout(graphics, document, template, 74).SelectMany(row => row.Cells).Select(cell => cell.Text).ToList();
                Assert.Contains(cells, value => value.Contains("المبلغ المستحق"));
                Assert.DoesNotContain(cells, value => value.Contains("اسم الكاشير الحقيقي"));
            }
        }
        [Theory]
        [InlineData("sale", "العميل")]
        [InlineData("sales-return", "العميل")]
        [InlineData("purchase", "المورد")]
        [InlineData("purchase-return", "المورد")]
        public void Product_documents_share_table_and_use_the_correct_party_label(string type, string party)
        {
            var template = PrintingService.BuiltIn("types", "فاتورة");
            var document = PrintingService.Sample(); document.Type = type;
            document.Items[0].Variant = "افتراضي / Default / كرتونة";
            using (var bitmap = new Bitmap(10, 10)) using (var graphics = Graphics.FromImage(bitmap))
            {
                var rows = new NativePrintRenderer().Layout(graphics, document, template, 74);
                var cells = rows.SelectMany(row => row.Cells).ToList();
                Assert.Contains(cells, cell => cell.Text == "الصنف");
                Assert.Contains(cells, cell => cell.Text == "الكمية");
                Assert.Contains(cells, cell => cell.Text == "السعر");
                Assert.Contains(cells, cell => cell.Text == "الإجمالي");
                Assert.Contains(cells, cell => cell.Text == party);
                Assert.DoesNotContain(cells, cell => cell.Text.Contains("افتراضي") || cell.Text.Contains("Default"));
                Assert.Contains(cells, cell => cell.Text.Contains("كرتونة"));
            }
        }
        [Theory]
        [InlineData("58mm", 58, 297)]
        [InlineData("80mm", 80, 297)]
        [InlineData("A4", 210, 297)]
        [InlineData("A5", 148, 210)]
        [InlineData("custom", 100, 150)]
        public async Task Arabic_mixed_text_exact_amounts_and_long_receipts_layout_and_rasterize_without_browser(string preset, int width, int height)
        {
            var template = PrintingService.BuiltIn("layout-test", "قالب عربي"); template.Paper.Preset = preset; template.Paper.WidthMm = width; template.Paper.HeightMm = height;
            PrintingService.ValidateTemplate(template);
            var doc = PrintingService.Sample(); doc.Fields["grandTotal"] = "123456789.12";
            for (var i = 0; i < 100; i++) doc.Items.Add(new PrintItem { Name = "وصف صنف طويل Arabic English SKU-123 مع ألوان ومقاسات متعددة " + new string('س', 100), Variant = "XL / أزرق", Quantity = "1.25", UnitPrice = "25.50", Total = "31.88" });
            var renderer = new NativePrintRenderer();
            using (var bitmap = new Bitmap(10, 10)) using (var g = Graphics.FromImage(bitmap))
            {
                g.PageUnit = GraphicsUnit.Millimeter;
                var availableWidth = width - 6; var rows = renderer.Layout(g, doc, template, availableWidth);
                Assert.Contains(rows.SelectMany(r => r.Cells), c => c.Text == "الصنف");
                Assert.Contains(rows.SelectMany(r => r.Cells), c => c.Text == "السعر");
                Assert.Contains(rows.SelectMany(r => r.Cells), c => c.Text.Contains("English"));
                Assert.Contains(rows.SelectMany(r => r.Cells), c => c.Text.Contains("125.50"));
                Assert.All(rows.SelectMany(r => r.Cells), c => { Assert.True(c.Xmm >= -.01); Assert.True(c.Xmm + c.WidthMm <= availableWidth + .01); });
                var pages = NativePrintRenderer.Paginate(rows, height - 6); Assert.True(pages.Count > 1);
                Assert.Equal(rows.Count, pages.Sum(p => p.Count)); Assert.All(pages, p => Assert.True(p.Sum(r => r.HeightMm) <= height - 6 + .01));
                Assert.All(rows.Where(r => !r.Logo && !r.Divider).SelectMany(r => r.Cells), c => Assert.NotNull(c.Text));
            }
            var bytes = await new WindowsPrinterBackend(renderer).PreviewAsync(PrintingService.Sample(), template, CancellationToken.None);
            Assert.Equal(137, bytes[0]); Assert.Equal(80, bytes[1]);
            using (var stream = new MemoryStream(bytes)) using (var image = Image.FromStream(stream)) { Assert.True(image.Width > 100); Assert.True(image.Height > image.Width); }
            File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "printing-preview-" + preset + ".png"), bytes);
        }
        [Theory]
        [InlineData("58mm", 58)]
        [InlineData("80mm", 80)]
        public async Task Thermal_receipt_hierarchy_totals_delivery_branding_and_preview_share_native_rows(string preset, int width)
        {
            var template = PrintingService.BuiltIn("receipt-visual", "إيصال حراري");
            template.LogoPngBase64 = TestLogo();
            template.Paper.Preset = preset; template.Paper.WidthMm = width;
            var document = PrintingService.Sample(); document.Type = "sale"; document.Title = "إيصال توصيل";
            document.Fields["company"] = "متجر الخيال";
            document.Fields["address"] = "شارع النيل، القاهرة";
            document.Fields["phone"] = "01000000000";
            document.Fields["deliveryFee"] = "5.00";
            document.Fields["discountTotal"] = "5.00";
            document.Fields["taxTotal"] = "10.00";
            document.Fields["grandTotal"] = "135.50";
            document.Fields["remaining"] = "35.50";
            document.Fields["header"] = "عنوان التوصيل: شارع النيل\nالطيار المسؤول: أحمد";
            document.Items[0].Name = "قميص قطني عربي باسم طويل يحتاج التفافًا واضحًا على ورق الإيصال الحراري";
            var renderer = new NativePrintRenderer();
            using (var bitmap = new Bitmap(10, 10)) using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.PageUnit = GraphicsUnit.Millimeter;
                var rows = renderer.Layout(graphics, document, template, width - 6);
                var text = rows.SelectMany(r => r.Cells).Select(c => c.Text).ToList();
                Assert.True(rows[0].Logo);
                Assert.Contains(text, x => x.Contains("متجر الخيال"));
                Assert.Contains(text, x => x.Contains("إيصال توصيل"));
                Assert.Contains(text, x => x.Contains("رسوم التوصيل"));
                Assert.Contains(text, x => x.Contains("135.50"));
                Assert.Contains(text, x => x.Contains("125.50"));
                Assert.Contains(text, x => x == "10.00");
                Assert.Contains(text, x => x == "5.00");
                Assert.Contains(text, x => x == "الضريبة");
                Assert.Contains(text, x => x == "الخصم");
                Assert.Contains(text, x => x.Contains("عنوان التوصيل"));
                Assert.Contains(rows, r => r.Divider);
                Assert.True(text.FindIndex(x => x.Contains("إيصال توصيل")) < text.FindIndex(x => x == "الصنف"));
                Assert.True(text.FindIndex(x => x == "الصنف") < text.FindLastIndex(x => x.Contains("الإجمالي النهائي")));
                Assert.All(rows.SelectMany(r => r.Cells), c => { Assert.True(c.Xmm >= 0); Assert.True(c.Xmm + c.WidthMm <= width - 6 + .01); });
            }
            var backend = new WindowsPrinterBackend(renderer);
            var bytes = await backend.PreviewAsync(document, template, CancellationToken.None);
            File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "thermal-receipt-" + preset + ".png"), bytes);
            template.Sections.Single(s => s.Key == "logo").Visible = false;
            using (var bitmap = new Bitmap(10, 10)) using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.PageUnit = GraphicsUnit.Millimeter;
                Assert.DoesNotContain(renderer.Layout(graphics, document, template, width - 6), row => row.Logo);
            }
            bytes = await backend.PreviewAsync(document, template, CancellationToken.None);
            File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "thermal-receipt-no-logo-" + preset + ".png"), bytes);
        }
        [Theory]
        [InlineData("58mm", 58)]
        [InlineData("80mm", 80)]
        public async Task Thermal_preview_includes_totals_after_a_long_receipt(string preset, int width)
        {
            var template = PrintingService.BuiltIn("long-preview", "إيصال طويل"); template.Paper.Preset = preset; template.Paper.WidthMm = width;
            var document = PrintingService.Sample();
            for (var i = 0; i < 39; i++) document.Items.Add(new PrintItem { Name = "صنف عربي طويل للتأكد من ظهور الإجمالي بعد جميع البنود " + i, Quantity = "2", UnitPrice = "62.75", Total = "125.50" });
            var renderer = new NativePrintRenderer();
            using (var bitmap = new Bitmap(10, 10)) using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.PageUnit = GraphicsUnit.Millimeter;
                var cells = renderer.Layout(graphics, document, template, width - 6).SelectMany(row => row.Cells).Select(cell => cell.Text).ToList();
                Assert.True(cells.FindLastIndex(value => value.Contains("125.50")) < cells.FindLastIndex(value => value.Contains("المدفوع")));
            }
            var bytes = await new WindowsPrinterBackend(renderer).PreviewAsync(document, template, CancellationToken.None);
            using (var stream = new MemoryStream(bytes)) using (var image = Image.FromStream(stream))
                Assert.True((double)image.Height / image.Width > 297d / width, "The preview must extend beyond the first 297mm page.");
        }
        [Fact]
        public async Task Unsaved_logo_and_paper_values_change_the_native_branding_preview()
        {
            var service = new PrintingService(null, null, new WindowsPrinterBackend(new NativePrintRenderer()));
            var template = PrintingService.BuiltIn("live", "معاينة حية"); template.Paper.Preset = "58mm"; template.Paper.WidthMm = 58;
            var request = new PrintPreviewRequest { Template = template, DocumentType = "sale", BusinessName = "متجر الخيال", Address = "القاهرة", Phone = "01000000000" };
            var fallback = await service.PreviewAsync(request, CancellationToken.None);
            using (var logo = new Bitmap(8, 8)) using (var graphics = Graphics.FromImage(logo)) using (var stream = new MemoryStream())
            {
                graphics.Clear(Color.Black); logo.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                template.LogoPngBase64 = Convert.ToBase64String(stream.ToArray());
            }
            var uploaded = await service.PreviewAsync(request, CancellationToken.None);
            Assert.False(fallback.SequenceEqual(uploaded));
            template.LogoPngBase64 = null; template.Sections.Single(s => s.Key == "logo").Visible = false;
            var hidden = await service.PreviewAsync(request, CancellationToken.None);
            Assert.False(uploaded.SequenceEqual(hidden));
            template.Paper.Preset = "80mm"; template.Paper.WidthMm = 80;
            var wide = await service.PreviewAsync(request, CancellationToken.None);
            using (var narrowStream = new MemoryStream(hidden)) using (var wideStream = new MemoryStream(wide))
            using (var narrowImage = Image.FromStream(narrowStream)) using (var wideImage = Image.FromStream(wideStream))
                Assert.True(wideImage.Width > narrowImage.Width);
        }
        [Fact]
        public void Configured_logo_does_not_distort_thermal_width()
        {
            var template = PrintingService.BuiltIn("branding", "فاتورة بيع");
            template.LogoPngBase64 = TestLogo();
            template.Paper.Preset = "58mm";
            template.Paper.WidthMm = 58;
            using (var bitmap = new Bitmap(10, 10))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.PageUnit = GraphicsUnit.Millimeter;
                var rows = new NativePrintRenderer().Layout(graphics, PrintingService.Sample(), template, 52);
                var logo = Assert.Single(rows, row => row.Logo);
                Assert.Equal(15, template.LogoWidthMm);
                Assert.Equal(15, template.LogoHeightMm);
                Assert.True(logo.HeightMm <= 18);
                Assert.True(logo.Cells[0].WidthMm <= 52);
            }
        }
        private static string TestLogo()
        {
            using (var image = new Bitmap(8, 8)) using (var stream = new MemoryStream())
            { image.Save(stream, System.Drawing.Imaging.ImageFormat.Png); return Convert.ToBase64String(stream.ToArray()); }
        }

        [Fact]
        public void Invalid_templates_and_unrenderable_logo_are_rejected()
        {
            var t = PrintingService.BuiltIn("safe", "آمن"); t.Paper.WidthMm = 1;
            Assert.Throws<PrintException>(() => PrintingService.ValidateTemplate(t));
            t = PrintingService.BuiltIn("safe", "آمن"); t.Sections.RemoveAt(0); Assert.Throws<PrintException>(() => PrintingService.ValidateTemplate(t));
            t = PrintingService.BuiltIn("safe", "آمن"); t.Sections[0].Key = "<script>"; Assert.Throws<PrintException>(() => PrintingService.ValidateTemplate(t));
            Assert.Throws<PrintException>(() => NativePrintRenderer.DecodeLogo("not-a-png"));
            Assert.Throws<PrintException>(() => NativePrintRenderer.Paginate(new System.Collections.Generic.List<PrintRow> { new PrintRow { HeightMm = 100 } }, 20));
        }
        [Fact]
        public void Narrow_receipts_preserve_long_exact_quantities_and_amounts_in_stacked_fields()
        {
            var t = PrintingService.BuiltIn("numeric", "قيم دقيقة"); t.Paper.Preset = "58mm"; t.Paper.WidthMm = 58;
            var d = PrintingService.Sample(); d.Items[0].Quantity = "123456.123456"; d.Items[0].Total = "922337203685477.58";
            using (var b = new Bitmap(10, 10)) using (var g = Graphics.FromImage(b))
            {
                g.PageUnit = GraphicsUnit.Millimeter; var rows = new NativePrintRenderer().Layout(g, d, t, 52);
                var columns = rows.Where(r => r.Cells.Count == 4).SelectMany(r => r.Cells.Select((c, i) => new { c.Text, Column = i })).ToList();
                Assert.Contains("123456.123456", string.Concat(columns.Where(c => c.Column == 1).Select(c => c.Text)));
                Assert.Contains("922337203685477.58", string.Concat(columns.Where(c => c.Column == 3).Select(c => c.Text)));
            }
        }
        [Fact]
        public void Driver_status_distinguishes_normal_queue_activity_from_device_errors()
        {
            WindowsPrinterBackend.ValidateReportedStatus(0);
            WindowsPrinterBackend.ValidateReportedStatus(0x100 | 0x200 | 0x400 | 0x4000);
            Assert.Equal("printer_offline", Assert.Throws<PrintException>(() => WindowsPrinterBackend.ValidateReportedStatus(0x80)).Code);
            foreach (var status in new[] { 1, 2, 8, 16, 0x800, 0x100000, 0x400000 }) Assert.Equal("printer_error", Assert.Throws<PrintException>(() => WindowsPrinterBackend.ValidateReportedStatus(status)).Code);
        }
        [Fact]
        public void Physical_units_round_trip_within_driver_resolution()
        {
            foreach (var mm in new[] { 58, 80, 148, 210, 297 }) Assert.InRange(Math.Abs(WindowsPrinterBackend.Mm(WindowsPrinterBackend.Hundredths(mm)) - mm), 0, .13);
        }
        [Fact]
        public void Thermal_driver_accepts_registered_receipt_length_and_printable_width()
        {
            var paper = new PrintPaper { Preset = "80mm", WidthMm = 80, HeightMm = 297 };
            // XP-80C (copy 2) advertises this custom form for an 80 mm roll.
            var driver = new[] { new System.Drawing.Printing.PaperSize("80(72.1) x 297 mm", 283, 1169) };
            Assert.Same(driver[0], WindowsPrinterBackend.SelectPaper(driver, paper, 297));
            paper.Preset = "58mm"; paper.WidthMm = 58;
            var narrow = new[] { new System.Drawing.Printing.PaperSize("Receipt 58", WindowsPrinterBackend.Hundredths(52), WindowsPrinterBackend.Hundredths(100)) };
            Assert.Same(narrow[0], WindowsPrinterBackend.SelectPaper(narrow, paper, 297));
        }
        [Fact]
        public void Exact_custom_form_is_preferred_and_incompatible_width_is_rejected()
        {
            var paper = new PrintPaper { Preset = "80mm", WidthMm = 80, HeightMm = 297 };
            var forms = new[] {
                new System.Drawing.Printing.PaperSize("Receipt", WindowsPrinterBackend.Hundredths(72), WindowsPrinterBackend.Hundredths(200)),
                new System.Drawing.Printing.PaperSize("Custom", WindowsPrinterBackend.Hundredths(80), WindowsPrinterBackend.Hundredths(297)) };
            Assert.Same(forms[1], WindowsPrinterBackend.SelectPaper(forms, paper, 297));
            Assert.Null(WindowsPrinterBackend.SelectPaper(new[] { new System.Drawing.Printing.PaperSize("Narrow", WindowsPrinterBackend.Hundredths(58), WindowsPrinterBackend.Hundredths(297)) }, paper, 297));
            paper.Preset = "A4"; paper.WidthMm = 210;
            Assert.Null(WindowsPrinterBackend.SelectPaper(forms, paper, 297));
        }
        [Fact]
        public async Task Configured_reference_barcode_uses_native_receipt_preview_and_print_layout()
        {
            var template = PrintingService.BuiltIn("receipt-barcode", "إيصال");
            template.ShowReferenceBarcode = true;
            var document = PrintingService.Sample();
            var renderer = new NativePrintRenderer();
            using (var bitmap = new Bitmap(600, 900))
            {
                bitmap.SetResolution(144, 144);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                graphics.Clear(Color.White);
                var rows = renderer.Layout(graphics, document, template, 65);
                var barcode = Assert.Single(rows, row => row.BarcodeValue == document.Reference);
                renderer.Draw(graphics, barcode, 4, 10, template);
                Assert.Contains(Enumerable.Range(0, bitmap.Width), x => bitmap.GetPixel(x, 75).ToArgb() != Color.White.ToArgb());
                }
            }
            var png = await new WindowsPrinterBackend(renderer).PreviewAsync(document, template, CancellationToken.None);
            Assert.Equal(137, png[0]);
        }
        [Theory]
        [InlineData("58mm", 58, 52)]
        [InlineData("80mm", 80, 74)]
        public void Receipt_branding_uses_saved_text_once_and_places_footer_after_barcode(string preset, int paperWidth, int printableWidth)
        {
            var template = PrintingService.BuiltIn("branding-order", "فاتورة");
            template.Paper.Preset = preset; template.Paper.WidthMm = paperWidth;
            template.HeaderText = "مرحبا\nEnglish header";
            template.FooterText = "شكرا\nEnglish footer";
            template.LogoPngBase64 = TestLogo(); template.ShowReferenceBarcode = true;
            var document = PrintingService.Sample(); document.Type = "delivery"; document.Fields["collectAmount"] = "0.00";
            document.Fields["header"] = "عنوان التوصيل: شارع النيل";
            document.Fields["footer"] = "نص شركة قديم";
            using (var bitmap = new Bitmap(10, 10)) using (var graphics = Graphics.FromImage(bitmap))
            {
                var rows = new NativePrintRenderer().Layout(graphics, document, template, printableWidth);
                Assert.Single(rows, row => row.Logo);
                Assert.Equal(1, rows.SelectMany(row => row.Cells).Count(cell => cell.Text == "English header"));
                Assert.Equal(1, rows.SelectMany(row => row.Cells).Count(cell => cell.Text == "English footer"));
                Assert.DoesNotContain(rows.SelectMany(row => row.Cells), cell => cell.Text.Contains("نص شركة قديم"));
                Assert.True(rows.FindIndex(row => row.BarcodeValue != null) < rows.FindIndex(row => row.Cells.Any(cell => cell.Text == "English footer")));
                Assert.Contains(rows.SelectMany(row => row.Cells), cell => cell.Text == "الأصناف المطلوبة");
                Assert.DoesNotContain(rows, row => row.Cells.Count == 4 && row.Cells[0].Text == "الصنف");
                template.FooterText = ""; template.HeaderText = ""; template.LogoPngBase64 = null;
                rows = new NativePrintRenderer().Layout(graphics, document, template, printableWidth);
                Assert.DoesNotContain(rows, row => row.Logo || row.Cells.Any(cell => cell.Text == "English footer"));
            }
        }
        [Theory]
        [InlineData(52)]
        [InlineData(71.88)]
        public void Optional_receipt_barcode_fits_or_falls_back_without_label_width_error(double printableWidth)
        {
            var template = PrintingService.BuiltIn("receipt-fit", "إيصال");
            template.Paper.Preset = printableWidth < 60 ? "58mm" : "80mm";
            template.Paper.WidthMm = printableWidth < 60 ? 58 : 80;
            template.ShowReferenceBarcode = true;
            var document = PrintingService.Sample(); document.Reference = printableWidth < 60 ? "S-000123" : "SALE-2026-000123";
            using (var bitmap = new Bitmap(1000, 1000)) using (var graphics = Graphics.FromImage(bitmap))
            {
                bitmap.SetResolution(203, 203);
                var renderer = new NativePrintRenderer();
                var rows = renderer.Layout(graphics, document, template, (float)printableWidth);
                Assert.True(NativePrintRenderer.ReceiptBarcodeFits(document.Reference, (float)printableWidth, 203));
                renderer.Draw(graphics, Assert.Single(rows, row => row.BarcodeValue != null), 3, 3, template);
                document.Reference = new string('X', 250);
                rows = renderer.Layout(graphics, document, template, (float)printableWidth);
                Assert.False(NativePrintRenderer.ReceiptBarcodeFits(document.Reference, (float)printableWidth, 203));
                Assert.DoesNotContain(rows, row => row.BarcodeValue != null);
                Assert.Contains(rows, row => row.Cells.Any(cell => cell.Text.Contains("رقم المستند:")));
                template.ShowReferenceBarcode = false;
                rows = renderer.Layout(graphics, document, template, (float)printableWidth);
                Assert.DoesNotContain(rows, row => row.BarcodeValue != null);
            }
        }
        [Theory]
        [InlineData("58mm", 58)]
        [InlineData("80mm", 80)]
        public void Delivery_ticket_uses_separate_product_and_collection_layout(string preset, int paperWidth)
        {
            var template = PrintingService.BuiltIn("ticket", "بون"); template.Paper.Preset = preset; template.Paper.WidthMm = paperWidth;
            template.HeaderText = "رأس محفوظ\nسطر ثان"; template.FooterText = "ذيل محفوظ";
            var document = PrintingService.Sample(); document.Type = "delivery"; document.Title = "بون توصيل";
            document.Fields.Remove("subtotal"); document.Fields.Remove("discountTotal"); document.Fields.Remove("taxTotal"); document.Fields.Remove("grandTotal"); document.Fields.Remove("paid");
            document.Fields["collectAmount"] = "0.0000";
            document.Fields["header"] = "عنوان التوصيل: شارع النيل\nالهاتف: 010000\nالمندوب: أحمد\nتعليمات التوصيل: الدور الثاني";
            using (var bitmap = new Bitmap(10, 10)) using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.PageUnit = GraphicsUnit.Millimeter;
                var rows = new NativePrintRenderer().Layout(graphics, document, template, paperWidth - 6);
                var text = rows.SelectMany(row => row.Cells).Select(cell => cell.Text).ToList();
                Assert.Contains("بون توصيل", text); Assert.Contains("الأصناف المطلوبة", text);
                Assert.Contains(text, value => value.Contains("المتبقي للتحصيل")); Assert.Contains(text, value => value.Contains("0.00"));
                Assert.Contains(text, value => value.Contains("تعليمات التوصيل"));
                Assert.Contains("سطر ثان", text); Assert.Contains("ذيل محفوظ", text);
                Assert.DoesNotContain("السعر", text); Assert.DoesNotContain("الإجمالي النهائي", text);
            }
        }
    }
}
