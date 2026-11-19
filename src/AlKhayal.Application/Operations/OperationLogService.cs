using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Operations
{
    public sealed class OperationLogEntry
    {
        public long Id { get; set; }
        public long? ActorUserId { get; set; }
        public string ActorUsername { get; set; }
        public string ActorDisplayName { get; set; }
        public string HttpMethod { get; set; }
        public string Resource { get; set; }
        public int StatusCode { get; set; }
        public string Outcome { get; set; }
        public string OccurredUtc { get; set; }
    }

    public sealed class OperationLogQuery
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public string Search { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public long? ActorUserId { get; set; }
        public string Outcome { get; set; }
    }

    public sealed class OperationLogPage
    {
        public IReadOnlyList<OperationLogEntry> Items { get; set; }
        public long TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public interface IOperationLogStore
    {
        Task RecordAsync(OperationLogEntry entry, CancellationToken cancellationToken);
        Task<OperationLogPage> ListAsync(OperationLogQuery query, CancellationToken cancellationToken);
    }

    public sealed class OperationLogService
    {
        private readonly IOperationLogStore _store;

        public OperationLogService(IOperationLogStore store)
        {
            _store = store ?? throw new ArgumentNullException("store");
        }

        public Task RecordAsync(long? userId, string username, string displayName, string method, string resource, int statusCode, bool succeeded, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(resource))
                throw new ArgumentException("بيانات سجل العملية غير مكتملة.");

            return _store.RecordAsync(new OperationLogEntry
            {
                ActorUserId = userId,
                ActorUsername = Limit(username, 64),
                ActorDisplayName = Limit(displayName, 128),
                HttpMethod = Limit(method.Trim().ToUpperInvariant(), 10),
                Resource = Limit(resource.Trim(), 512),
                StatusCode = statusCode,
                Outcome = succeeded ? "success" : "failure",
                OccurredUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            }, cancellationToken);
        }

        public Task<OperationLogPage> ListAsync(OperationLogQuery query, CancellationToken cancellationToken)
        {
            if (query == null) query = new OperationLogQuery();
            query.Page = Math.Max(1, Math.Min(1000000, query.Page <= 0 ? 1 : query.Page));
            query.PageSize = Math.Max(1, Math.Min(100, query.PageSize <= 0 ? 50 : query.PageSize));
            query.Search = Limit(query.Search == null ? string.Empty : query.Search.Trim(), 128);
            query.Outcome = string.IsNullOrWhiteSpace(query.Outcome) ? null : query.Outcome.Trim().ToLowerInvariant();
            if (query.Outcome != null && query.Outcome != "success" && query.Outcome != "failure")
                throw new ArgumentException("حالة العملية غير صحيحة.");
            if (query.ActorUserId.HasValue && query.ActorUserId.Value <= 0)
                throw new ArgumentException("المستخدم المحدد غير صحيح.");

            DateTime from, to;
            if (!string.IsNullOrWhiteSpace(query.From) && !DateTime.TryParseExact(query.From, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out from))
                throw new ArgumentException("تاريخ البداية غير صحيح.");
            if (!string.IsNullOrWhiteSpace(query.To) && !DateTime.TryParseExact(query.To, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out to))
                throw new ArgumentException("تاريخ النهاية غير صحيح.");
            if (!string.IsNullOrWhiteSpace(query.From) && !string.IsNullOrWhiteSpace(query.To) && string.CompareOrdinal(query.From, query.To) > 0)
                throw new ArgumentException("تاريخ البداية يجب ألا يتجاوز تاريخ النهاية.");
            return _store.ListAsync(query, cancellationToken);
        }

        private static string Limit(string value, int maximum)
        {
            value = value ?? string.Empty;
            return value.Length <= maximum ? value : value.Substring(0, maximum);
        }
    }
}
