using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Exports
{
    public enum ExportValueType { Text, Number, Date }

    public sealed class ExportColumn
    {
        public ExportColumn(string key, string header, ExportValueType type, double width)
        { Key = key; Header = header; Type = type; Width = width; }
        public string Key { get; }
        public string Header { get; }
        public ExportValueType Type { get; }
        public double Width { get; }
    }

    public sealed class ExportDatasetDefinition
    {
        public string Key { get; set; }
        public string ArabicName { get; set; }
        public string PermissionRoute { get; set; }
        public string FeatureKey { get; set; }
        public IReadOnlyList<string> SupportedFilters { get; set; }
        public IReadOnlyList<ExportColumn> Columns { get; set; }
    }

    public sealed class ExportQuery
    {
        public string Dataset { get; set; }
        public IReadOnlyDictionary<string, string> Filters { get; set; }
    }

    public sealed class ExportRow
    {
        public IDictionary<string, object> Values { get; set; }
    }

    public sealed class ExportDocument
    {
        public ExportDatasetDefinition Definition { get; set; }
        public IReadOnlyList<ExportRow> Rows { get; set; }
    }

    public interface IExportDataSource
    {
        Task<IReadOnlyList<ExportRow>> QueryAsync(ExportQuery query, CancellationToken cancellationToken);
    }

    public interface IExportFileWriter
    {
        string Format { get; }
        string ContentType { get; }
        string Extension { get; }
        byte[] Write(ExportDocument document);
    }

    public sealed class ExportResult
    {
        public byte[] Content { get; set; }
        public string ContentType { get; set; }
        public string FileName { get; set; }
    }
}
