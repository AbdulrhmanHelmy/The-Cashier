using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using AlKhayal.Application.Exports;

namespace AlKhayal.Data.Exports
{
    public sealed class XlsxExportWriter : IExportFileWriter
    {
        public string Format => "xlsx";
        public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        public string Extension => ".xlsx";
        public byte[] Write(ExportDocument document)
        {
            using(var output=new MemoryStream())
            {
                using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))
                {
                    Text(zip,"[Content_Types].xml",ContentTypes);
                    Text(zip,"_rels/.rels",RootRels);
                    Text(zip,"xl/workbook.xml",Workbook(document.Definition.ArabicName));
                    Text(zip,"xl/_rels/workbook.xml.rels",WorkbookRels);
                    Text(zip,"xl/styles.xml",Styles);
                    Sheet(zip,document);
                }
                return output.ToArray();
            }
        }
        private static void Sheet(ZipArchive zip,ExportDocument document)
        {
            var entry=zip.CreateEntry("xl/worksheets/sheet1.xml",CompressionLevel.Fastest);
            using(var stream=entry.Open())using(var x=XmlWriter.Create(stream,new XmlWriterSettings{Encoding=new UTF8Encoding(false),CloseOutput=false}))
            {
                x.WriteStartDocument();x.WriteStartElement("worksheet","http://schemas.openxmlformats.org/spreadsheetml/2006/main");
                x.WriteStartElement("sheetViews");x.WriteStartElement("sheetView");x.WriteAttributeString("workbookViewId","0");x.WriteAttributeString("rightToLeft","1");x.WriteStartElement("pane");x.WriteAttributeString("ySplit","1");x.WriteAttributeString("topLeftCell","A2");x.WriteAttributeString("activePane","bottomLeft");x.WriteAttributeString("state","frozen");x.WriteEndElement();x.WriteEndElement();x.WriteEndElement();
                x.WriteStartElement("cols");for(var i=0;i<document.Definition.Columns.Count;i++){x.WriteStartElement("col");x.WriteAttributeString("min",(i+1).ToString());x.WriteAttributeString("max",(i+1).ToString());x.WriteAttributeString("width",document.Definition.Columns[i].Width.ToString(CultureInfo.InvariantCulture));x.WriteAttributeString("customWidth","1");x.WriteEndElement();}x.WriteEndElement();
                x.WriteStartElement("sheetData");x.WriteStartElement("row");x.WriteAttributeString("r","1");foreach(var c in document.Definition.Columns)Inline(x,c.Header,1);x.WriteEndElement();
                var rowNumber=2;foreach(var row in document.Rows){x.WriteStartElement("row");x.WriteAttributeString("r",rowNumber.ToString());foreach(var c in document.Definition.Columns){object value;row.Values.TryGetValue(c.Key,out value);if(c.Type==ExportValueType.Number&&value!=null)Number(x,value);else Inline(x,value==null?"":Convert.ToString(value,CultureInfo.InvariantCulture),0);}x.WriteEndElement();rowNumber++;}x.WriteEndElement();
                var last=ColumnName(document.Definition.Columns.Count)+(Math.Max(1,rowNumber-1)).ToString();x.WriteStartElement("autoFilter");x.WriteAttributeString("ref","A1:"+last);x.WriteEndElement();x.WriteEndElement();x.WriteEndDocument();
            }
        }
        private static void Inline(XmlWriter x,string value,int style){x.WriteStartElement("c");x.WriteAttributeString("t","inlineStr");if(style>0)x.WriteAttributeString("s",style.ToString());x.WriteStartElement("is");x.WriteStartElement("t");x.WriteAttributeString("xml","space",null,"preserve");x.WriteString(value??"");x.WriteEndElement();x.WriteEndElement();x.WriteEndElement();}
        private static void Number(XmlWriter x,object value){x.WriteStartElement("c");x.WriteAttributeString("s","2");x.WriteStartElement("v");x.WriteString(Convert.ToDecimal(value,CultureInfo.InvariantCulture).ToString("0.####",CultureInfo.InvariantCulture));x.WriteEndElement();x.WriteEndElement();}
        private static string ColumnName(int n){var s="";while(n>0){n--;s=(char)('A'+n%26)+s;n/=26;}return s;}
        private static void Text(ZipArchive zip,string name,string value){var e=zip.CreateEntry(name,CompressionLevel.Fastest);using(var w=new StreamWriter(e.Open(),new UTF8Encoding(false)))w.Write(value);}
        private static string Workbook(string name)=>"<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\""+SecurityElement(name)+"\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";
        private static string SecurityElement(string s)=>System.Security.SecurityElement.Escape((s??"بيانات").Length>31?(s??"بيانات").Substring(0,31):s??"بيانات");
        private const string ContentTypes="<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>";
        private const string RootRels="<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>";
        private const string WorkbookRels="<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>";
        private const string Styles="<?xml version=\"1.0\" encoding=\"UTF-8\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Arial\"/></font><font><b/><sz val=\"11\"/><name val=\"Arial\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"3\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"><alignment horizontal=\"right\"/></xf><xf numFmtId=\"4\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs></styleSheet>";
    }
}
