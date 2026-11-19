using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Operations;
using Dapper;

namespace AlKhayal.Data.Operations
{
    public sealed class SqliteOperationLogStore : IOperationLogStore
    {
        private readonly string _connectionString;
        public SqliteOperationLogStore(string connectionString) { _connectionString = connectionString; }

        public async Task RecordAsync(OperationLogEntry entry, CancellationToken cancellationToken)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await connection.ExecuteAsync(new CommandDefinition(@"INSERT INTO OperationAuditLog
(ActorUserId,ActorUsername,ActorDisplayName,HttpMethod,Resource,StatusCode,Outcome,OccurredUtc)
VALUES(@ActorUserId,@ActorUsername,@ActorDisplayName,@HttpMethod,@Resource,@StatusCode,@Outcome,@OccurredUtc)", entry, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }
        }

        public async Task<OperationLogPage> ListAsync(OperationLogQuery query, CancellationToken cancellationToken)
        {
            var fromUtc = string.IsNullOrWhiteSpace(query.From) ? null : LocalDateBoundaryUtc(query.From);
            var toUtc = string.IsNullOrWhiteSpace(query.To) ? null : LocalDateBoundaryUtc(DateTime.ParseExact(query.To, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            var parameters = new
            {
                query.ActorUserId,
                query.Outcome,
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Search = "%" + (query.Search ?? string.Empty).Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%",
                Offset = (query.Page - 1) * query.PageSize,
                query.PageSize
            };
            const string where = @"WHERE (@ActorUserId IS NULL OR ActorUserId=@ActorUserId)
AND (@Outcome IS NULL OR Outcome=@Outcome)
AND (@FromUtc IS NULL OR OccurredUtc>=@FromUtc)
AND (@ToUtc IS NULL OR OccurredUtc<@ToUtc)
AND (@Search='%%' OR ActorUsername LIKE @Search ESCAPE '\' OR ActorDisplayName LIKE @Search ESCAPE '\' OR Resource LIKE @Search ESCAPE '\')";
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var total = await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM OperationAuditLog " + where, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);
                var items = (await connection.QueryAsync<OperationLogEntry>(new CommandDefinition("SELECT Id,ActorUserId,ActorUsername,ActorDisplayName,HttpMethod,Resource,StatusCode,Outcome,OccurredUtc FROM OperationAuditLog " + where + " ORDER BY Id DESC LIMIT @PageSize OFFSET @Offset", parameters, cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();
                return new OperationLogPage { Items = items, TotalCount = total, Page = query.Page, PageSize = query.PageSize };
            }
        }

        private static string LocalDateBoundaryUtc(string date)
        {
            var local = DateTime.SpecifyKind(DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(local, TimeZoneInfo.Local).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        }
    }
}
