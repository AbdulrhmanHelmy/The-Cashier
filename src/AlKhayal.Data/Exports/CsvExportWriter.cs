using System;
using System.Globalization;
using System.Linq;
using System.Text;
using AlKhayal.Application.Exports;

namespace AlKhayal.Data.Exports
{
    public sealed class CsvExportWriter : IExportFileWriter
    {
        public string Format => "csv";
        public string ContentType => "text/csv; charset=utf-8";
        public string Extension => ".csv";
        public byte[] Write(ExportDocument document)
        {
            var text=new StringBuilder();
            text.AppendLine(string.Join(",",document.Definition.Columns.Select(x=>Quote(x.Header))));
            foreach(var row in document.Rows) text.AppendLine(string.Join(",",document.Definition.Columns.Select(c=>Cell(row,c))));
            var encoding=new UTF8Encoding(true);var body=encoding.GetBytes(text.ToString());var bom=encoding.GetPreamble();var result=new byte[bom.Length+body.Length];Buffer.BlockCopy(bom,0,result,0,bom.Length);Buffer.BlockCopy(body,0,result,bom.Length,body.Length);return result;
        }
        private static string Cell(ExportRow row,ExportColumn column){object value;row.Values.TryGetValue(column.Key,out value);if(value==null)return "";if(column.Type==ExportValueType.Number)return Convert.ToString(value,CultureInfo.InvariantCulture);var text=Convert.ToString(value,CultureInfo.InvariantCulture);var trimmed=text.TrimStart();if((trimmed.Length>0&&"=+-@".IndexOf(trimmed[0])>=0)||(text.Length>1&&text[0]=='0'&&char.IsDigit(text[1])))text="'"+text;return Quote(text);}
        private static string Quote(string value){value=value??"";return value.IndexOfAny(new[]{',','"','\r','\n'})>=0?'"'+value.Replace("\"","\"\"")+'"':value;}
    }
}
