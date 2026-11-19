using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using AlKhayal.Application.Exports;
using AlKhayal.Data.Exports;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class ExportWriterTests
    {
        private static ExportDocument Document(params ExportRow[] rows)=>new ExportDocument{Definition=new ExportDatasetDefinition{Key="test",ArabicName="بيانات",Columns=new[]{new ExportColumn("name","الاسم",ExportValueType.Text,20),new ExportColumn("phone","الهاتف",ExportValueType.Text,18),new ExportColumn("amount","المبلغ",ExportValueType.Number,15)}},Rows=rows};
        private static ExportRow Row(string name,string phone,decimal amount)=>new ExportRow{Values=new Dictionary<string,object>{{"name",name},{"phone",phone},{"amount",amount}}};

        [Fact] public void Csv_is_utf8_bom_escaped_and_formula_safe()
        {
            var bytes=new CsvExportWriter().Write(Document(Row("=SUM(1,2)\nعميل","00123",12.5m)));
            Assert.Equal(new byte[]{0xEF,0xBB,0xBF},bytes.Take(3));var text=Encoding.UTF8.GetString(bytes);Assert.Contains("\"'=SUM(1,2)\nعميل\"",text);Assert.Contains("'00123",text);Assert.Contains("12.5",text);
        }

        [Fact] public void Xlsx_has_rtl_frozen_filter_bold_header_numeric_and_text_cells()
        {
            var bytes=new XlsxExportWriter().Write(Document(Row("عميل","00123",12.5m)));using(var archive=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read))
            {Assert.NotNull(archive.GetEntry("xl/workbook.xml"));var sheet=Read(archive,"xl/worksheets/sheet1.xml");var styles=Read(archive,"xl/styles.xml");Assert.Contains("rightToLeft=\"1\"",sheet);Assert.Contains("state=\"frozen\"",sheet);Assert.Contains("autoFilter",sheet);Assert.Contains("t=\"inlineStr\"",sheet);Assert.Contains("<v>12.5</v>",sheet);Assert.Contains("<b",styles);XDocument.Parse(sheet);}
        }

        [Fact] public void Empty_dataset_still_has_headers_in_both_formats()
        {Assert.Contains("الاسم",Encoding.UTF8.GetString(new CsvExportWriter().Write(Document())));using(var archive=new ZipArchive(new MemoryStream(new XlsxExportWriter().Write(Document())),ZipArchiveMode.Read))Assert.Contains("الاسم",Read(archive,"xl/worksheets/sheet1.xml"));}

        [Fact] public async System.Threading.Tasks.Task Definitions_and_invalid_dataset_format_filter_are_explicit()
        {
            var service=new ExportService(new EmptySource(),new IExportFileWriter[]{new CsvExportWriter(),new XlsxExportWriter()});Assert.Equal(6,service.Definitions.Count);Assert.Equal(new[]{"name","sku","barcode","category","unit","purchasePrice","sellingPrice","status"},service.Find("products").Columns.Select(x=>x.Key));
            await Assert.ThrowsAsync<ArgumentException>(()=>service.ExportAsync("users","csv",null,System.Threading.CancellationToken.None));await Assert.ThrowsAsync<ArgumentException>(()=>service.ExportAsync("products","pdf",null,System.Threading.CancellationToken.None));await Assert.ThrowsAsync<ArgumentException>(()=>service.ExportAsync("products","csv",new Dictionary<string,string>{{"password","x"}},System.Threading.CancellationToken.None));
        }
        private static string Read(ZipArchive a,string name){using(var reader=new StreamReader(a.GetEntry(name).Open()))return reader.ReadToEnd();}
        private sealed class EmptySource:IExportDataSource{public System.Threading.Tasks.Task<IReadOnlyList<ExportRow>> QueryAsync(ExportQuery q,System.Threading.CancellationToken c)=>System.Threading.Tasks.Task.FromResult<IReadOnlyList<ExportRow>>(new List<ExportRow>());}
    }
}
