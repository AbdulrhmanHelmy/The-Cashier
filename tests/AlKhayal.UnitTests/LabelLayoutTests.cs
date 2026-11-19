using System;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Printing;
using AlKhayal.Printing;
using Xunit;
using ZXing;

namespace AlKhayal.UnitTests
{
    public sealed class LabelLayoutTests
    {
        [Fact]
        public void Driver_copies_are_expanded_per_label_without_repeating_whole_sheets()
        {
            var batch = LabelService.Sample();
            batch.StartCell = 2;
            batch.Items.Add(new ProductLabel { Barcode = "ABC123", Format = "code128", Quantity = 3 });
            var expanded = WindowsPrinterBackend.ExpandLabelCopies(batch, 2);
            Assert.Equal(2, expanded.StartCell);
            Assert.Equal(new[] { 4, 6 }, expanded.Items.Select(x => x.Quantity));
            Assert.Equal(new[] { 2, 3 }, batch.Items.Select(x => x.Quantity));
            var sheet = LabelService.BuiltIn(); sheet.Stock.PageWidthMm = 150; sheet.Stock.Columns = 3;
            Assert.Equal(10, new NativeLabelRenderer().Layout(expanded, sheet).Count);
        }
        [Fact]
        public void Label_form_selection_requires_both_width_and_height()
        {
            var forms = new[] { new PaperSize("2 x 4", 210, 400), new PaperSize("USER", 310, 400) };
            var request = new PrintPaper { Preset = "custom", WidthMm = 50, HeightMm = 30 };
            Assert.Null(WindowsPrinterBackend.SelectPaper(forms, request, 30));
            request.WidthMm = 53.34m; request.HeightMm = 101.6m;
            Assert.Equal("2 x 4", WindowsPrinterBackend.SelectPaper(forms, request, 101.6f).PaperName);
        }
        [Theory]
        [InlineData("code128", "ABC-123/xy")]
        [InlineData("code128", "2912345678")]
        [InlineData("ean13", "4006381333931")]
        public void Native_barcode_round_trip_preserves_checksum_content_quiet_zones_and_integer_modules(string format, string value)
        {
            var modules = NativeBarcode.Modules(value, format); var quiet = format == "ean13" ? 11 : 10;
            Assert.All(modules.Take(quiet), m => Assert.False(m)); Assert.All(modules.Reverse().Take(quiet), m => Assert.False(m));
            using (var image = new Bitmap(1000, 300))
            {
                image.SetResolution(300, 300);
                using (var g = Graphics.FromImage(image)) { g.Clear(Color.White); NativeBarcode.Draw(g, modules, new LabelElement { Width = 80, Height = 15, ModuleMm = .2m }, 2, 2); }
                var decoded = new BarcodeReader { AutoRotate = false, Options = { PossibleFormats = new[] { format == "ean13" ? BarcodeFormat.EAN_13 : BarcodeFormat.CODE_128 }, TryHarder = true } }.Decode(image);
                Assert.NotNull(decoded); Assert.Equal(value, decoded.Text);
                // Bars have no interpolated grey edges.
                for (var x = 0; x < image.Width; x++) { var c = image.GetPixel(x, 50); Assert.True(c.R == 0 || c.R == 255); }
            }
        }
        [Fact]
        public void Ean_validation_completes_12_digits_and_rejects_invalid_check_or_unsupported_code128()
        {
            Assert.Equal("4006381333931", BarcodeRules.Validate("400638133393", "ean13", true));
            Assert.Throws<ArgumentException>(() => BarcodeRules.Validate("4006381333932", "ean13"));
            Assert.Throws<ArgumentException>(() => BarcodeRules.Validate("40063813339", "ean13"));
            Assert.Throws<ArgumentException>(() => BarcodeRules.Validate("عربي", "code128"));
            Assert.Throws<ArgumentException>(() => BarcodeRules.Validate("ABC\n123", "code128"));
            Assert.Throws<ArgumentException>(() => BarcodeRules.Validate(" ABC", "code128"));
            Assert.Equal(10, BarcodeRules.GenerateInternal().Length);
            Assert.Equal(2, NativeBarcode.ModuleDots(.2m, 203)); Assert.Equal(3, NativeBarcode.ModuleDots(.2m, 300)); Assert.Equal(5, NativeBarcode.ModuleDots(.2m, 600));
        }
        [Theory]
        [InlineData(30, 20)] [InlineData(35, 25)] [InlineData(40, 30)] [InlineData(50, 25)] [InlineData(50, 30)] [InlineData(60, 40)] [InlineData(100, 50)] [InlineData(73, 37)]
        public async Task Native_preview_Arabic_mixed_text_and_real_proportions_on_presets_and_custom_stock(int width, int height)
        {
            var t = LabelService.BuiltIn("test", width, height); var backend = new WindowsPrinterBackend(new NativePrintRenderer());
            var data = await backend.PreviewLabelsAsync(LabelService.Sample(), t, CancellationToken.None); Assert.Equal(137, data[0]);
            using (var stream = new MemoryStream(data)) using (var image = Image.FromStream(stream)) Assert.InRange(Math.Abs((double)image.Width / image.Height - (double)width / height), 0, .02);
            File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "label-preview-" + width + "x" + height + ".png"), data);
        }
        [Theory]
        [InlineData("Tea")]
        [InlineData("شاي عربي")]
        [InlineData("شاي عربي English طويل جدًا للاختبار")]
        public async Task Product_name_ink_is_centered_in_35x25_printable_cell(string name)
        {
            var template = LabelService.BuiltIn("center-test", 32, 22);
            template.Stock.PageWidthMm = 35; template.Stock.PageHeightMm = 25;
            template.Stock.LeftMm = 1.5m; template.Stock.RightMm = 1.5m;
            template.Stock.TopMm = 1.5m; template.Stock.BottomMm = 1.5m;
            template.Elements.Single(x => x.Key == "productName").X = 2; // historical off-center saved box
            var batch = LabelService.Sample(); batch.Items[0].Quantity = 1;
            batch.Items[0].Fields["productName"] = name;
            var png = await new WindowsPrinterBackend(new NativePrintRenderer()).PreviewLabelsAsync(batch, template, CancellationToken.None);
            using (var stream = new MemoryStream(png)) using (var image = new Bitmap(stream))
            {
                var min = image.Width; var max = -1;
                var top = (int)Math.Ceiling(2.5 * image.VerticalResolution / 25.4);
                var bottom = (int)Math.Floor(6.8 * image.VerticalResolution / 25.4);
                for (var y = top; y < bottom; y++) for (var x = 0; x < image.Width; x++)
                {
                    var pixel = image.GetPixel(x, y);
                    if (pixel.R >= 150 || pixel.G >= 150 || pixel.B >= 150) continue;
                    min = Math.Min(min, x); max = Math.Max(max, x);
                }
                Assert.True(max >= min, "Product name must be visible in the preview.");
                var inkCenterMm = (min + max) / 2.0 * 25.4 / image.HorizontalResolution;
                Assert.InRange(inkCenterMm, 17.0, 18.0);
            }
        }
        [Fact]
        public void Sheet_grid_start_cell_partial_pages_and_printable_area_are_validated()
        {
            var t = LabelService.BuiltIn(); t.Stock.PageWidthMm = 210; t.Stock.PageHeightMm = 297; t.Stock.LeftMm = 5; t.Stock.TopMm = 5; t.Stock.Columns = 3; t.Stock.Rows = 8; t.Stock.HorizontalGapMm = 3; t.Stock.VerticalGapMm = 2;
            var batch = LabelService.Sample(); batch.StartCell = 22; batch.Items[0].Quantity = 5;
            var renderer = new NativeLabelRenderer(); var layout = renderer.Layout(batch, t);
            Assert.Equal(5, layout.Count); Assert.Equal(22, layout[0].Cell); Assert.Equal(58, layout[0].X); Assert.Equal(229, layout[0].Y); Assert.Equal(1, layout[2].Page); Assert.Equal(0, layout[2].Cell);
            using (var b = new Bitmap(10, 10))
            { b.SetResolution(300, 300); using (var g = Graphics.FromImage(b)) { renderer.Validate(g, batch, t, new RectangleF(0, 0, 210, 297)); Assert.Throws<PrintException>(() => renderer.Validate(g, batch, t, new RectangleF(20, 20, 180, 250))); } }
            t.Stock.Columns = 5; Assert.Throws<PrintException>(() => LabelService.ValidateTemplate(t));
        }
        [Fact]
        public async Task Continuous_roll_uses_driver_page_pitch_and_sheet_preview_keeps_start_cells_empty()
        {
            var t = LabelService.BuiltIn(); t.Stock.Continuous = true; t.Stock.VerticalGapMm = 2;
            Assert.Throws<PrintException>(() => LabelService.ValidateTemplate(t));
            t.Stock.PageHeightMm = 32; LabelService.ValidateTemplate(t);
            var placements = new NativeLabelRenderer().Layout(LabelService.Sample(), t);
            Assert.Equal(1, placements[1].Page); Assert.Equal(0, placements[1].Y);
            t.Stock.Continuous = false; t.Stock.PageWidthMm = 210; t.Stock.PageHeightMm = 297; t.Stock.Columns = 3; t.Stock.Rows = 8; t.Stock.LeftMm = 5; t.Stock.TopMm = 5; t.Stock.HorizontalGapMm = 3;
            var batch = LabelService.Sample(); batch.StartCell = 4; batch.Items[0].Quantity = 25;
            var png = await new WindowsPrinterBackend(new NativePrintRenderer()).PreviewLabelsAsync(batch, t, CancellationToken.None);
            File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "label-preview-A4.png"), png);
            using (var stream = new MemoryStream(png)) using (var image = new Bitmap(stream)) Assert.Equal(Color.White.ToArgb(), image.GetPixel(50, 50).ToArgb());
        }
        [Fact]
        public void Invalid_bounds_overlap_overflow_and_insufficient_barcode_width_are_rejected()
        {
            var t = LabelService.BuiltIn(); t.Elements[0].X = 49; Assert.Throws<PrintException>(() => LabelService.ValidateTemplate(t));
            t = LabelService.BuiltIn(); t.Elements[0].Y = 9; Assert.Throws<PrintException>(() => LabelService.ValidateTemplate(t));
            var bar = new LabelElement { Width = 5, Height = 10, ModuleMm = .2m }; Assert.Throws<PrintException>(() => NativeBarcode.Validate(NativeBarcode.Modules("ABC-123", "code128"), bar, 203));
            t = LabelService.BuiltIn(); t.Elements[0].Width = 1; t.Elements[0].Height = 1;
            using (var b = new Bitmap(10, 10)) using (var g = Graphics.FromImage(b)) Assert.Throws<PrintException>(() => new NativeLabelRenderer().Validate(g, LabelService.Sample(), t, new RectangleF(0, 0, 50, 30)));
        }
    }
}
