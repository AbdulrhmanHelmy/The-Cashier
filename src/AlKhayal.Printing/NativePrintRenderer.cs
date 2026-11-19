using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Globalization;
using System.Linq;
using AlKhayal.Application.Printing;

namespace AlKhayal.Printing
{
    public sealed class PrintCell
    {
        public string Text { get; set; }
        public float Xmm { get; set; }
        public float WidthMm { get; set; }
        public bool Numeric { get; set; }
        public float FontPoints { get; set; }
        public bool? Bold { get; set; }
        public string Alignment { get; set; }
    }
    public sealed class PrintRow
    {
        public List<PrintCell> Cells { get; } = new List<PrintCell>();
        public float HeightMm { get; set; }
        public float FontPoints { get; set; }
        public bool Bold { get; set; }
        public string Alignment { get; set; }
        public bool Divider { get; set; }
        public bool Logo { get; set; }
        public string BarcodeValue { get; set; }
        public string SectionKey { get; set; }
    }
    public interface IPrintRenderer
    {
        List<PrintRow> Layout(Graphics graphics, PrintDocumentData data, PrintTemplate template, float widthMm);
        void Draw(Graphics graphics, PrintRow row, float xMm, float yMm, PrintTemplate template);
    }
    // GDI+ shapes Arabic through the installed Windows font; no printer-resident text fonts are used.
    public sealed class NativePrintRenderer : IPrintRenderer
    {
        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string> { ["reference"] = "رقم المستند", ["date"] = "التاريخ", ["cashier"] = "المستخدم", ["party"] = "الطرف", ["partyPhone"] = "هاتف الطرف", ["warehouse"] = "المخزن", ["paymentMethod"] = "طريقة الدفع", ["paymentNotes"] = "ملاحظات الدفع", ["taxInfo"] = "الرقم الضريبي", ["subtotal"] = "قبل الخصم", ["discountTotal"] = "إجمالي الخصم", ["taxTotal"] = "الضريبة", ["grandTotal"] = "الإجمالي", ["paid"] = "المدفوع", ["remaining"] = "المتبقي / التسوية" };
        public List<PrintRow> Layout(Graphics g, PrintDocumentData d, PrintTemplate t, float width)
        {
            g.PageUnit = GraphicsUnit.Millimeter;
            if ((t.Paper.Preset == "58mm" || t.Paper.Preset == "80mm") && !d.Type.StartsWith("shift-", StringComparison.Ordinal)) return LayoutThermal(g, d, t, width);
            var rows = new List<PrintRow>();
            foreach (var s in t.Sections.Where(s => s.Visible))
            {
                var size = (float)(s.FontSize * t.Paper.Scale); var gap = (float)s.SpacingMm;
                if (s.Key == "logo")
                {
                    if (HasLogo(t)) { using (var logo = DecodeLogo(t.LogoPngBase64)) { } rows.Add(new PrintRow { Logo = true, Alignment = s.Alignment, HeightMm = (float)t.LogoHeightMm + gap, Cells = { new PrintCell { Text = "", WidthMm = width } } }); }
                    continue;
                }
                if (s.Key == "items")
                {
                    var columns = InvoiceColumns(t, width);
                    AddInvoiceRow(rows, g, columns.Item1, columns.Item2, size, true, gap);
                    foreach (var item in d.Items)
                    {
                        AddInvoiceRow(rows, g, ItemValues(item, t, width), columns.Item2, size, s.Bold, gap);
                        AddItemDetails(rows, g, item, t, width);
                        if (t.Dividers) rows.Add(new PrintRow { Divider = true, HeightMm = 1, Cells = { new PrintCell { Text = "", WidthMm = width } } });
                    }
                    continue;
                }
                if (new[] { "variant", "unitPrice", "discount", "tax", "quantity", "lineTotal" }.Contains(s.Key)) continue;
                string value; d.Fields.TryGetValue(s.Key, out value);
                if (s.Key == "header") value = Join(t.HeaderText, FilterHeader(t, value));
                if (s.Key == "footer") value = t.FooterText;
                if (s.Key == "policy") value = t.ReturnPolicy;
                if (s.Key == "signature") value = (string.IsNullOrWhiteSpace(t.SignatureText) ? "التوقيع" : t.SignatureText) + ": ____________________";
                if (s.Key == "reference") value = d.Reference;
                if (s.Key == "title") value = d.Title;
                if (string.IsNullOrWhiteSpace(value)) continue;
                var label = Labels.ContainsKey(s.Key) ? Labels[s.Key] : null;
                if (d.Type.StartsWith("shift-", StringComparison.Ordinal))
                { if (s.Key == "grandTotal") label = "المتوقع"; if (s.Key == "paid") label = "المعدود"; if (s.Key == "remaining") label = "الفرق"; }
                if (!string.IsNullOrWhiteSpace(s.Label)) label = s.Label;
                if (label != null) value = label + ": " + value;
                var lines = Wrap(g, value, width, size, s.Bold);
                for (var i = 0; i < lines.Count; i++) rows.Add(new PrintRow { FontPoints = size, Bold = s.Bold, Alignment = s.Alignment, HeightMm = LineHeight(size) + (i == lines.Count - 1 ? gap : 0), Cells = { new PrintCell { Text = lines[i], WidthMm = width } } });
            }
            if (rows.Count == 0) throw new PrintException("empty_template", "القالب لا يحتوي محتوى ظاهرًا.");
            return rows;
        }
        private List<PrintRow> LayoutThermal(Graphics g, PrintDocumentData d, PrintTemplate t, float width)
        {
            var rows = new List<PrintRow>();
            if (Visible(t, "logo") && HasLogo(t))
            {
                using (var logo = DecodeLogo(t.LogoPngBase64)) { }
                rows.Add(new PrintRow { Logo = true, SectionKey = "logo", Alignment = "center", HeightMm = (float)t.LogoHeightMm + 2, Cells = { new PrintCell { Text = "", WidthMm = width } } });
            }
            AddThermalText(rows, g, Field(d, "company"), width, 12, true, "center", 1.4f, t, "company");
            AddThermalText(rows, g, Field(d, "address"), width, 8, false, "center", .4f, t, "address");
            AddThermalText(rows, g, Field(d, "phone"), width, 8, false, "center", 1.4f, t, "phone");
            if (Visible(t, "title") && !string.IsNullOrWhiteSpace(d.Title))
            {
                AddDivider(rows, width, 1.5f, "title");
                AddThermalText(rows, g, d.Title, width, 11, true, "center", 1.5f, t, "title");
            }
            AddDivider(rows, width, 1, "reference");
            AddThermalPair(rows, g, Label(t, "reference", "رقم الفاتورة"), d.Reference, width, t, "reference");
            AddThermalPair(rows, g, Label(t, "date", "التاريخ"), Field(d, "date"), width, t, "date");
            AddThermalPair(rows, g, Label(t, "cashier", "أمين الصندوق"), Field(d, "cashier"), width, t, "cashier");
            AddThermalPair(rows, g, Label(t, "party", d.Type == "purchase" || d.Type == "purchase-return" || d.Type == "supplier-payment" ? "المورد" : "العميل"), Field(d, "party"), width, t, "party");
            AddThermalPair(rows, g, Label(t, "partyPhone", "هاتف الطرف"), Field(d, "partyPhone"), width, t, "partyPhone");
            AddThermalPair(rows, g, Label(t, "warehouse", "المخزن"), Field(d, "warehouse"), width, t, "warehouse");
            AddThermalPair(rows, g, Label(t, "paymentMethod", "طريقة الدفع"), Field(d, "paymentMethod"), width, t, "paymentMethod");
            if (!string.IsNullOrWhiteSpace(Field(d, "paymentNotes"))) AddThermalText(rows, g, Label(t, "paymentNotes", "ملاحظات الدفع") + ": " + Field(d, "paymentNotes"), width, 8, false, "right", .3f, t, "paymentNotes");
            AddThermalPair(rows, g, Label(t, "taxInfo", "الرقم الضريبي"), Field(d, "taxInfo"), width, t, "taxInfo");
            AddThermalText(rows, g, t.HeaderText, width, 8, false, "right", .3f, t, "header");
            var header = Field(d, "header");
            var deliveryLines = new List<string>();
            foreach (var line in (header ?? "").Split('\n'))
            {
                var value = line.Trim();
                if (string.IsNullOrEmpty(value)) continue;
                if (Visible(t, "paymentMethod") && value.StartsWith("طريقة الدفع:")) continue;
                if (Visible(t, "warehouse") && value.StartsWith("مخزن الاستلام:")) continue;
                if (d.Type == "delivery" && (value.StartsWith("عنوان التوصيل:") || value.StartsWith("الهاتف:") || value.StartsWith("المندوب:") || value.StartsWith("تعليمات التوصيل:") || value.StartsWith("حالة التوصيل:"))) deliveryLines.Add(value);
                else AddThermalText(rows, g, value, width, 8, false, "right", .3f, t, "header");
            }
            if (Visible(t, "items") && d.Items.Count > 0)
            {
                AddDivider(rows, width, 1.5f, "items");
                if (d.Type == "delivery")
                {
                    AddThermalText(rows, g, "الأصناف المطلوبة", width, 9, true, "right", .7f, t, "items");
                    foreach (var item in d.Items) AddThermalText(rows, g, ItemName(item, t) + (Visible(t, "quantity") ? " × " + item.Quantity : ""), width, 8, false, "right", .5f, t, "items");
                }
                else
                {
                    var columns = InvoiceColumns(t, width);
                    var size = (t.Paper.Preset == "58mm" ? 6.2f : 7.3f) * (float)t.Paper.Scale;
                    var itemSection = t.Sections.FirstOrDefault(s => s.Key == "items");
                    if (itemSection != null && itemSection.UseTypography) size = (float)(itemSection.FontSize * t.Paper.Scale);
                    AddInvoiceRow(rows, g, columns.Item1, columns.Item2, size, true, .8f);
                    for (var i = 0; i < d.Items.Count; i++)
                    {
                        var item = d.Items[i];
                        AddInvoiceRow(rows, g, ItemValues(item, t, width), columns.Item2, size, itemSection != null && itemSection.UseTypography && itemSection.Bold, itemSection != null && itemSection.UseTypography ? (float)itemSection.SpacingMm : .45f);
                        AddItemDetails(rows, g, item, t, width);
                        if (t.Dividers && i < d.Items.Count - 1) AddDivider(rows, width, .6f, "items");
                    }
                }
            }
            AddDivider(rows, width, 1.5f, "subtotal");
            AddThermalPair(rows, g, Label(t, "subtotal", "إجمالي المنتجات"), Field(d, "subtotal"), width, t, "subtotal");
            AddThermalPair(rows, g, Label(t, "discountTotal", "الخصم"), Field(d, "discountTotal"), width, t, "discountTotal");
            AddThermalPair(rows, g, Label(t, "taxTotal", "الضريبة"), Field(d, "taxTotal"), width, t, "taxTotal");
            if (d.Fields.ContainsKey("deliveryFee")) AddThermalPair(rows, g, "رسوم التوصيل", Field(d, "deliveryFee"), width, t, "grandTotal");
            if (Visible(t, "grandTotal") && !string.IsNullOrWhiteSpace(Field(d, "grandTotal")))
            {
                AddDivider(rows, width, 1, "grandTotal");
                AddThermalPair(rows, g, Label(t, "grandTotal", "الإجمالي النهائي"), Field(d, "grandTotal"), width, t, "grandTotal", true);
            }
            AddThermalPair(rows, g, Label(t, "paid", "المدفوع"), Field(d, "paid"), width, t, "paid");
            if (d.Type != "delivery") AddThermalPair(rows, g, Label(t, "remaining", "المتبقي"), Field(d, "remaining"), width, t, "remaining");
            if (d.Type == "delivery") AddThermalPair(rows, g, "المتبقي للتحصيل", Field(d, "collectAmount"), width, t, "grandTotal", true);
            if (deliveryLines.Count > 0)
            {
                AddDivider(rows, width, 2, "header");
                AddThermalText(rows, g, "بيانات التوصيل", width, 9, true, "right", 1, t, "header");
                foreach (var line in deliveryLines) if (!line.StartsWith("رسوم التوصيل:")) AddThermalText(rows, g, line, width, 8, false, "right", .4f, t, "header");
            }
            var footer = t.FooterText;
            if (t.ShowReferenceBarcode && !string.IsNullOrWhiteSpace(d.Reference))
            {
                if (ReceiptBarcodeFits(d.Reference, width, 203f))
                    rows.Add(new PrintRow { BarcodeValue = d.Reference, SectionKey = "reference", HeightMm = 18, Cells = { new PrintCell { Text = d.Reference, WidthMm = width } } });
                else
                {
                    System.Diagnostics.Trace.TraceWarning("Optional receipt barcode omitted: reference cannot fit the printable width ({0} mm).", width);
                    AddThermalText(rows, g, "رقم المستند: " + d.Reference, width, 8, false, "center", 1, t, "reference");
                }
            }
            if (!string.IsNullOrWhiteSpace(footer) || !string.IsNullOrWhiteSpace(t.ReturnPolicy)) AddDivider(rows, width, 2, "footer");
            AddThermalText(rows, g, footer, width, 8, false, "center", .5f, t, "footer");
            AddThermalText(rows, g, t.ReturnPolicy, width, 8, false, "center", .5f, t, "policy");
            if (Visible(t, "signature")) AddThermalText(rows, g, (string.IsNullOrWhiteSpace(t.SignatureText) ? "التوقيع" : t.SignatureText) + ": ____________________", width, 8, false, "right", 1, t, "signature");
            if (rows.Count == 0) throw new PrintException("empty_template", "القالب لا يحتوي محتوى ظاهرًا.");
            if (t.UseSectionOrder)
            {
                var order = t.Sections.Select((section, index) => new { section.Key, index }).ToDictionary(x => x.Key, x => x.index);
                rows = rows.OrderBy(row => row.SectionKey != null && order.ContainsKey(row.SectionKey) ? order[row.SectionKey] : int.MaxValue).ToList();
            }
            return rows;
        }
        private static string Field(PrintDocumentData document, string key) { string value; return document.Fields.TryGetValue(key, out value) ? value : null; }
        private static string Label(PrintTemplate template, string key, string fallback) { var section = template.Sections.FirstOrDefault(s => s.Key == key); return section == null || string.IsNullOrWhiteSpace(section.Label) ? fallback : section.Label; }
        private static string FilterHeader(PrintTemplate template, string value)
        {
            return string.Join("\n", (value ?? "").Split('\n').Where(line =>
                !(Visible(template, "paymentMethod") && line.TrimStart().StartsWith("طريقة الدفع:", StringComparison.Ordinal)) &&
                !(Visible(template, "warehouse") && line.TrimStart().StartsWith("مخزن الاستلام:", StringComparison.Ordinal))));
        }
        private static string ItemName(PrintItem item, PrintTemplate template)
        {
            if (!Visible(template, "variant")) return item.Name;
            var detail = (item.Variant ?? "").Split('/').Select(x => x.Trim()).Where(x => x.Length > 0 && x != "افتراضي" && !x.Equals("Default", StringComparison.OrdinalIgnoreCase));
            return item.Name + (detail.Any() ? " (" + string.Join(" / ", detail) + ")" : "");
        }
        private static Tuple<string[], float[]> InvoiceColumns(PrintTemplate template, float width)
        {
            var narrow = template.Paper.Preset == "58mm";
            var wide = width >= 100 && template.EnableExtendedItemDetails;
            var labels = new List<string> { Label(template, "items", "الصنف") };
            var weights = new List<float> { narrow ? .39f : .43f };
            if (Visible(template, "quantity")) { labels.Add(Label(template, "quantity", "الكمية")); weights.Add(.17f); }
            if (Visible(template, "unitPrice")) { labels.Add(Label(template, "unitPrice", "السعر")); weights.Add(.20f); }
            if (wide && Visible(template, "discount")) { labels.Add(Label(template, "discount", "الخصم")); weights.Add(.10f); }
            if (wide && Visible(template, "tax")) { labels.Add(Label(template, "tax", "الضريبة")); weights.Add(.10f); }
            if (Visible(template, "lineTotal")) { labels.Add(Label(template, "lineTotal", "الإجمالي")); weights.Add(narrow ? .24f : .20f); }
            if (wide) { weights[0] = Math.Max(.30f, 1 - weights.Skip(1).Sum()); var sum = weights.Sum(); for (var i = 0; i < weights.Count; i++) weights[i] /= sum; }
            else weights[0] += 1 - weights.Sum();
            return Tuple.Create(labels.ToArray(), weights.Select(weight => width * weight).ToArray());
        }
        private static string[] ItemValues(PrintItem item, PrintTemplate template, float width)
        {
            var values = new List<string> { ItemName(item, template) };
            if (Visible(template, "quantity")) values.Add(item.Quantity);
            if (Visible(template, "unitPrice")) values.Add(item.UnitPrice);
            if (template.EnableExtendedItemDetails && width >= 100 && Visible(template, "discount")) values.Add(item.Discount);
            if (template.EnableExtendedItemDetails && width >= 100 && Visible(template, "tax")) values.Add(item.Tax);
            if (Visible(template, "lineTotal")) values.Add(item.Total);
            return values.ToArray();
        }
        private static void AddItemDetails(List<PrintRow> rows, Graphics graphics, PrintItem item, PrintTemplate template, float width)
        {
            if (!template.EnableExtendedItemDetails) return;
            if (template.ShowSku && !string.IsNullOrWhiteSpace(item.Sku)) AddThermalText(rows, graphics, "SKU: " + item.Sku, width, 7, false, "right", .2f, template, "items");
            if (width < 100 && Visible(template, "discount") && !string.IsNullOrWhiteSpace(item.Discount)) AddThermalText(rows, graphics, Label(template, "discount", "خصم البند") + ": " + item.Discount, width, 7, false, "right", .2f, template, "items");
            if (width < 100 && Visible(template, "tax") && !string.IsNullOrWhiteSpace(item.Tax)) AddThermalText(rows, graphics, Label(template, "tax", "ضريبة البند") + ": " + item.Tax, width, 7, false, "right", .2f, template, "items");
        }
        private static void AddInvoiceRow(List<PrintRow> rows, Graphics graphics, string[] values, float[] widths, float points, bool bold, float gap)
        {
            var size = points;
            // Keep every saved digit inside its column, including exceptional long values.
            for (var i = 1; i < values.Length; i++)
                if (!string.IsNullOrWhiteSpace(values[i]))
                    try { size = Math.Min(size, NumericSize(graphics, values[i], widths[i], points, bold)); }
                    catch (PrintException ex) when (ex.Code == "numeric_column_too_narrow") { size = Math.Min(size, 5f); }
            var nameLines = Wrap(graphics, values[0], widths[0], size, bold);
            var cellLines = new List<string>[values.Length]; cellLines[0] = nameLines;
            for (var i = 1; i < values.Length; i++) cellLines[i] = Wrap(graphics, values[i], widths[i], size, bold);
            var count = cellLines.Max(lines => lines.Count);
            var height = LineHeight(size);
            for (var line = 0; line < count; line++)
            {
                var row = new PrintRow { FontPoints = size, Bold = bold, Alignment = "right", SectionKey = "items", HeightMm = height + (line == count - 1 ? gap : 0) };
                var x = widths.Sum();
                for (var i = 0; i < values.Length; i++)
                {
                    x -= widths[i];
                    row.Cells.Add(new PrintCell { Text = line < cellLines[i].Count ? cellLines[i][line] : "", Xmm = Math.Max(0, x), WidthMm = widths[i], Numeric = i > 0 && !bold, Alignment = i == 0 ? "right" : "left" });
                }
                rows.Add(row);
            }
        }
        private static void AddDivider(List<PrintRow> rows, float width, float spacing, string section = null)
            => rows.Add(new PrintRow { Divider = true, SectionKey = section, HeightMm = spacing, Cells = { new PrintCell { Text = "", WidthMm = width } } });
        private static void AddThermalText(List<PrintRow> rows, Graphics graphics, string text, float width, float points, bool bold, string alignment, float gap, PrintTemplate template, string section)
        {
            if (!Visible(template, section) || string.IsNullOrWhiteSpace(text)) return;
            var saved = template.Sections.FirstOrDefault(s => s.Key == section);
            if (saved != null && saved.UseTypography) { points = (float)saved.FontSize; bold = saved.Bold; alignment = saved.Alignment; gap = (float)saved.SpacingMm; }
            var size = points * (float)template.Paper.Scale;
            var lines = Wrap(graphics, text, width, size, bold);
            for (var i = 0; i < lines.Count; i++) rows.Add(new PrintRow { FontPoints = size, Bold = bold, Alignment = alignment, SectionKey = section, HeightMm = LineHeight(size) + (i == lines.Count - 1 ? gap : 0), Cells = { new PrintCell { Text = lines[i], WidthMm = width } } });
        }
        private static void AddThermalPair(List<PrintRow> rows, Graphics graphics, string label, string value, float width, PrintTemplate template, string section, bool prominent = false)
        {
            if (!Visible(template, section) || string.IsNullOrWhiteSpace(value)) return;
            var saved = template.Sections.FirstOrDefault(s => s.Key == section);
            var customized = saved != null && saved.UseTypography;
            var bold = customized ? saved.Bold : prominent;
            var alignment = customized ? saved.Alignment : "right";
            var gap = customized ? (float)saved.SpacingMm : prominent ? 1.5f : .5f;
            var size = (customized ? (float)saved.FontSize : prominent ? 11f : 8.5f) * (float)template.Paper.Scale;
            var valueWidth = width * (prominent ? .54f : .58f);
            var labelWidth = width - valueWidth;
            var isNumber = section == "date" || section == "reference" || value.All(c => char.IsDigit(c) || c == '.' || c == ',' || c == '-');
            if (value.IndexOf('\n') < 0 && Fits(graphics, label, labelWidth, size, bold) && Fits(graphics, value, valueWidth, size, bold))
            {
                rows.Add(new PrintRow { FontPoints = size, Bold = bold, Alignment = alignment, SectionKey = section, HeightMm = LineHeight(size) + gap, Cells = {
                    new PrintCell { Text = value, Xmm = 0, WidthMm = valueWidth, Numeric = isNumber, Alignment = "left" },
                    new PrintCell { Text = label, Xmm = valueWidth, WidthMm = labelWidth, Alignment = "right" } } });
            }
            else
            {
                AddThermalText(rows, graphics, label + ": " + value, width, prominent ? 10 : 8, bold, alignment, gap, template, section);
            }
        }
        private static bool Fits(Graphics graphics, string value, float width, float size, bool bold)
        {
            using (var font = Font(size, bold)) using (var format = Format("right")) return graphics.MeasureString(value, font, new PointF(0, 0), format).Width <= width - 1;
        }
        private static void AddItemAmounts(List<PrintRow> rows, Graphics graphics, string quantity, string unitPrice, string total, float width, PrintTemplate template)
        {
            var description = string.IsNullOrWhiteSpace(unitPrice) ? quantity : string.IsNullOrWhiteSpace(quantity) ? "سعر " + unitPrice : quantity + " × " + unitPrice;
            if (string.IsNullOrWhiteSpace(description) && string.IsNullOrWhiteSpace(total)) return;
            var size = 8.5f * (float)template.Paper.Scale;
            var totalWidth = width * .40f;
            if (!string.IsNullOrWhiteSpace(total) && Fits(graphics, total, totalWidth, size, true) && Fits(graphics, description ?? "", width - totalWidth, size, false))
                rows.Add(new PrintRow { FontPoints = size, HeightMm = LineHeight(size) + .6f, Cells = {
                    new PrintCell { Text = total, Xmm = 0, WidthMm = totalWidth, Bold = true, Numeric = true, Alignment = "left" },
                    new PrintCell { Text = description, Xmm = totalWidth, WidthMm = width - totalWidth, Alignment = "right" } } });
            else
            {
                if (!string.IsNullOrWhiteSpace(quantity))
                {
                    AddThermalText(rows, graphics, "الكمية", width, 8, false, "right", .2f, template, "items");
                    AddThermalText(rows, graphics, quantity, width, 8, false, "left", .2f, template, "items");
                }
                if (!string.IsNullOrWhiteSpace(unitPrice)) AddThermalText(rows, graphics, "سعر الوحدة: " + unitPrice, width, 8, false, "right", .2f, template, "items");
                if (!string.IsNullOrWhiteSpace(total))
                {
                    AddThermalText(rows, graphics, "قيمة البند", width, 8, true, "right", .2f, template, "items");
                    AddThermalText(rows, graphics, total, width, 8, true, "left", .6f, template, "items");
                }
            }
        }
        public static bool Visible(PrintTemplate t, string key) => t.Sections.Any(s => s.Key == key && s.Visible);
        public static bool ReceiptBarcodeFits(string reference, float widthMm, float dpi)
        {
            if (string.IsNullOrWhiteSpace(reference) || widthMm <= 0 || dpi <= 0) return false;
            try
            {
                var modules = NativeBarcode.Modules(reference, "code128");
                // Include Code 128 quiet zones, two device dots per module, and 1 mm at each edge.
                var dots = NativeBarcode.ModuleDots(.25m, dpi);
                var available = (int)Math.Floor((widthMm - 2f) * dpi / 25.4f);
                return (long)modules.Length * dots <= available;
            }
            catch (ArgumentException) { return false; }
            catch (PrintException) { return false; }
        }
        private static string Join(string a, string b) => string.IsNullOrWhiteSpace(a) ? b : string.IsNullOrWhiteSpace(b) ? a : a + "\n" + b;
        public static float LineHeight(float points) => points * 25.4f / 72 * 1.5f;
        private static Font Font(float size, bool bold) => new Font("Tahoma", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
        private static StringFormat Format(string align, bool numeric = false)
        {
            var f = new StringFormat(StringFormat.GenericTypographic);
            f.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces | (numeric ? 0 : StringFormatFlags.DirectionRightToLeft);
            f.Alignment = align == "center" ? StringAlignment.Center : align == "left" ? StringAlignment.Far : StringAlignment.Near;
            // DirectionRightToLeft reverses Near/Far. Numeric cells explicitly align to their right edge.
            if (numeric) f.Alignment = align == "center" ? StringAlignment.Center : align == "left" ? StringAlignment.Near : StringAlignment.Far;
            f.LineAlignment = StringAlignment.Near; return f;
        }
        private static List<string> Wrap(Graphics g, string text, float widthMm, float size, bool bold)
        {
            var result = new List<string>();
            using (var font = Font(size, bold)) using (var fmt = Format("right"))
            {
                foreach (var paragraph in (text ?? "").Replace("\r", "").Split('\n'))
                {
                    var remaining = paragraph; if (remaining.Length == 0) { result.Add(""); continue; }
                    while (remaining.Length > 0)
                    {
                        // Binary-search a measured line. Graphics must use millimetres, as Draw does.
                        var lo = 1; var hi = remaining.Length; var fit = 0;
                        while (lo <= hi)
                        { var mid = (lo + hi) / 2; if (g.MeasureString(remaining.Substring(0, mid), font, new PointF(0, 0), fmt).Width <= widthMm - 1) { fit = mid; lo = mid + 1; } else hi = mid - 1; }
                        if (fit == 0) throw new PrintException("layout_too_narrow", "حجم الخط أكبر من عرض العمود. صغّر الخط أو وسّع العمود.");
                        if (fit < remaining.Length) { var space = remaining.LastIndexOf(' ', fit - 1, fit); if (space > 0) fit = space; }
                        // Do not split surrogate pairs or combining marks away from their base character.
                        while (fit > 1 && fit < remaining.Length && (char.IsLowSurrogate(remaining[fit]) || CharUnicodeInfo.GetUnicodeCategory(remaining, fit) == System.Globalization.UnicodeCategory.NonSpacingMark)) fit--;
                        result.Add(remaining.Substring(0, fit)); remaining = remaining.Substring(fit).TrimStart(' ');
                    }
                }
            }
            return result;
        }
        private static void AddColumns(List<PrintRow> rows, Graphics g, string[] values, float[] widths, float totalWidth, float size, bool bold, string alignment, float gap, PrintSection[] styles = null, float scale = 1)
        {
            var numeric = values.Select((v, i) => i > 0 && !string.IsNullOrEmpty(v) && v.All(c => char.IsDigit(c) || c == '.' || c == ',' || c == '-')).ToArray();
            var bolds = values.Select((v, i) => styles == null ? bold : styles[i].Bold).ToArray();
            var sizes = values.Select((v, i) => styles == null ? size : (float)styles[i].FontSize * scale).ToArray();
            try { for (var i = 0; i < sizes.Length; i++) if (numeric[i]) sizes[i] = NumericSize(g, values[i], widths[i], sizes[i], bolds[i]); }
            catch (PrintException ex) when (ex.Code == "numeric_column_too_narrow" && styles != null)
            {
                // Preserve long exact quantities/amounts on narrow rolls by using stacked full-width fields.
                for (var i = 0; i < values.Length; i++)
                {
                    if (string.IsNullOrEmpty(values[i])) continue;
                    var fontSize = numeric[i] ? NumericSize(g, values[i], totalWidth, sizes[i], bolds[i]) : sizes[i];
                    if (i > 0) rows.Add(new PrintRow { FontPoints = fontSize, Alignment = "right", HeightMm = LineHeight(fontSize), Cells = { new PrintCell { Text = i == 1 ? "الكمية:" : "قيمة البند:", WidthMm = totalWidth } } });
                    foreach (var line in Wrap(g, values[i], totalWidth, fontSize, bolds[i])) rows.Add(new PrintRow { FontPoints = fontSize, Bold = bolds[i], Alignment = styles[i].Alignment, HeightMm = LineHeight(fontSize) + (float)styles[i].SpacingMm, Cells = { new PrintCell { Text = line, WidthMm = totalWidth, Numeric = numeric[i] } } });
                }
                return;
            }
            var lines = values.Select((v, i) => Wrap(g, v, widths[i], sizes[i], bolds[i])).ToArray(); var count = lines.Max(x => x.Count);
            for (var line = 0; line < count; line++)
            {
                var row = new PrintRow { FontPoints = size, Bold = bold, Alignment = alignment, HeightMm = LineHeight(sizes.Max()) + (line == count - 1 ? (styles == null ? gap : styles.Max(s => (float)s.SpacingMm)) : 0) }; var x = totalWidth;
                for (var i = 0; i < widths.Length; i++) { x -= widths[i]; row.Cells.Add(new PrintCell { Xmm = x, WidthMm = widths[i], Text = line < lines[i].Count ? lines[i][line] : "", Numeric = numeric[i], FontPoints = sizes[i], Bold = bolds[i], Alignment = styles == null ? alignment : styles[i].Alignment }); }
                rows.Add(row);
            }
        }
        private static float NumericSize(Graphics g, string text, float width, float size, bool bold)
        {
            using (var f = Format("right", true))
            for (var candidate = size; candidate >= Math.Min(6, size); candidate -= .25f)
            using (var font = Font(candidate, bold))
                if (g.MeasureString(text, font, new PointF(0, 0), f).Width <= width - 1) return candidate;
            throw new PrintException("numeric_column_too_narrow", "القيمة الرقمية أكبر من العمود. وسّع عمود القيمة أو اختر ورقًا أعرض.");
        }
        public void Draw(Graphics g, PrintRow row, float x, float y, PrintTemplate t)
        {
            g.PageUnit = GraphicsUnit.Millimeter; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            if (row.BarcodeValue != null)
            {
                if (!ReceiptBarcodeFits(row.BarcodeValue, row.Cells[0].WidthMm, g.DpiX))
                {
                    System.Diagnostics.Trace.TraceWarning("Optional receipt barcode omitted at {0} DPI: reference cannot fit printable width.", g.DpiX);
                    using (var fallbackFont = Font(7, false)) using (var fallbackFormat = Format("center"))
                        g.DrawString("رقم المستند: " + row.BarcodeValue, fallbackFont, Brushes.Black, new RectangleF(x, y + 1, row.Cells[0].WidthMm, 15), fallbackFormat);
                    return;
                }
                var element = new LabelElement { Key = "barcode", Width = (decimal)row.Cells[0].WidthMm, Height = 10, ModuleMm = .25m };
                try { NativeBarcode.Draw(g, NativeBarcode.Modules(row.BarcodeValue, "code128"), element, x, y + 1); }
                catch (ArgumentException)
                {
                    System.Diagnostics.Trace.TraceWarning("Optional receipt barcode omitted: invalid Code 128 reference.");
                    using (var fallbackFont = Font(7, false)) using (var fallbackFormat = Format("center"))
                        g.DrawString("رقم المستند: " + row.BarcodeValue, fallbackFont, Brushes.Black, new RectangleF(x, y + 1, row.Cells[0].WidthMm, 15), fallbackFormat);
                    return;
                }
                using (var font = Font(7, false)) using (var format = Format("center", true))
                    g.DrawString(row.BarcodeValue, font, Brushes.Black, new RectangleF(x, y + 12, row.Cells[0].WidthMm, 5), format);
                return;
            }
            if (row.Divider) { var thermal = t.Paper.Preset == "58mm" || t.Paper.Preset == "80mm"; using (var pen = new Pen(thermal ? Color.FromArgb(170, 170, 170) : Color.Gray, thermal ? .12f : .15f)) { if (thermal) pen.DashStyle = DashStyle.Dash; g.DrawLine(pen, x, y + .3f, x + row.Cells[0].WidthMm, y + .3f); } return; }
            if (row.Logo)
            {
                var printableWidth = row.Cells[0].WidthMm;
                using (var image = DecodeLogo(t.LogoPngBase64))
                {
                    var scale = Math.Min(Math.Min((float)t.LogoWidthMm, printableWidth) / image.Width, (float)t.LogoHeightMm / image.Height);
                    var drawnWidth = image.Width * scale; var drawnHeight = image.Height * scale;
                    g.DrawImage(image, CenterLogoX(x, printableWidth, drawnWidth), y + ((float)t.LogoHeightMm - drawnHeight) / 2, drawnWidth, drawnHeight);
                }
                return;
            }
            foreach (var cell in row.Cells)
            using (var font = Font(cell.FontPoints > 0 ? cell.FontPoints : row.FontPoints, cell.Bold ?? row.Bold))
            using (var format = Format(cell.Alignment ?? row.Alignment, cell.Numeric))
                g.DrawString(cell.Text ?? "", font, Brushes.Black, new RectangleF(x + cell.Xmm + .25f, y, cell.WidthMm - .5f, row.HeightMm), format);
        }
        internal static float CenterLogoX(float printableLeftMm, float printableWidthMm, float logoWidthMm)
        { return printableLeftMm + (printableWidthMm - logoWidthMm) / 2f; }
        public static Bitmap DecodeLogo(string encoded)
        {
            try
            {
                byte[] bytes;
                if (string.IsNullOrWhiteSpace(encoded))
                {
                    using (var stream = typeof(NativePrintRenderer).Assembly.GetManifestResourceStream("AlKhayal.Printing.the-cashier-mark.png"))
                    {
                        if (stream == null) throw new InvalidDataException();
                        using (var buffer = new MemoryStream()) { stream.CopyTo(buffer); bytes = buffer.ToArray(); }
                    }
                }
                else bytes = Convert.FromBase64String(encoded);
                if (bytes.Length > 130000 || bytes.Length < 8 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71) throw new InvalidDataException();
                // Inspect IHDR dimensions before decoding to bound memory consumption.
                if (bytes.Length < 24) throw new InvalidDataException();
                uint w = (uint)(bytes[16] << 24 | bytes[17] << 16 | bytes[18] << 8 | bytes[19]); uint h = (uint)(bytes[20] << 24 | bytes[21] << 16 | bytes[22] << 8 | bytes[23]);
                if (w == 0 || h == 0 || w > 1200 || h > 1200) throw new InvalidDataException();
                using (var stream = new MemoryStream(bytes)) using (var img = Image.FromStream(stream, true, true)) return new Bitmap(img);
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidDataException || ex is System.Runtime.InteropServices.ExternalException)
            { throw new PrintException("invalid_logo", "اختر شعار PNG صالحًا حتى 1200×1200 وبحجم لا يتجاوز 130 كيلوبايت."); }
        }
        private static bool HasLogo(PrintTemplate template)
        {
            return !string.IsNullOrWhiteSpace(template.LogoPngBase64);
        }
        public static List<List<PrintRow>> Paginate(List<PrintRow> rows, float availableHeightMm)
        {
            var pages = new List<List<PrintRow>>(); var page = new List<PrintRow>(); float used = 0;
            foreach (var row in rows)
            {
                if (row.HeightMm > availableHeightMm) throw new PrintException("layout_too_tall", "ارتفاع العنصر أكبر من المساحة القابلة للطباعة.");
                if (used + row.HeightMm > availableHeightMm && page.Count > 0) { pages.Add(page); page = new List<PrintRow>(); used = 0; }
                page.Add(row); used += row.HeightMm;
            }
            if (page.Count > 0) pages.Add(page); return pages;
        }
    }
}
