using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Finance;
using AlKhayal.Application.Printing;
using Dapper;

namespace AlKhayal.Data.Printing
{
    // Read-only picker projections. Preview and submission still load the authoritative document.
    public sealed class SqlitePrintDocumentSearch : IPrintDocumentSearch
    {
        private readonly string _connectionString;
        public SqlitePrintDocumentSearch(string connectionString) { _connectionString = connectionString; }

        public async Task<IReadOnlyList<PrintDocumentChoice>> SearchAsync(string type, string search, int limit, CancellationToken ct)
        {
            if (!PrintTypes.All.Contains(type)) throw new ArgumentException("Unsupported print document type.", nameof(type));
            var sql = Sql(type);
            var term = (search ?? "").Trim();
            var like = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                var rows = (await connection.QueryAsync<Row>(new CommandDefinition(sql,
                    new { Search = term, Like = like, Limit = Math.Max(1, Math.Min(30, limit)) }, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                return rows.Select(row => new PrintDocumentChoice { Id = row.Id, Reference = row.Reference,
                    Party = row.Party, Date = row.Date, Amount = row.AmountScaled.HasValue ? MoneyPolicy.FormatForUser(row.AmountScaled.Value) : null }).ToList();
            }
        }

        private static string Sql(string type)
        {
            const string filter = " AND (@Search='' OR CAST(Id AS TEXT) LIKE @Like ESCAPE '\\' OR Reference LIKE @Like ESCAPE '\\' OR Party LIKE @Like ESCAPE '\\') ORDER BY Id DESC LIMIT @Limit";
            string source;
            switch (type)
            {
                case "sale":
                    source = @"SELECT si.Id,si.InvoiceNumber Reference,COALESCE(c.Name,'نقدي مباشر') Party,si.InvoiceDate Date,si.TotalScaled AmountScaled
                        FROM SalesInvoices si LEFT JOIN Customers c ON c.Id=si.CustomerId
                        WHERE si.Status='posted' AND NOT EXISTS(SELECT 1 FROM SalesCancellations x WHERE x.SalesInvoiceId=si.Id)
                        AND NOT EXISTS(SELECT 1 FROM SalesInvoiceCorrections x WHERE x.PreviousInvoiceId=si.Id)";
                    break;
                case "delivery":
                    source = @"SELECT si.Id,si.InvoiceNumber Reference,d.CustomerName Party,si.InvoiceDate Date,si.TotalScaled AmountScaled
                        FROM SalesInvoiceDeliveries d JOIN SalesInvoices si ON si.Id=d.SalesInvoiceId
                        WHERE si.Status='posted' AND NOT EXISTS(SELECT 1 FROM SalesCancellations x WHERE x.SalesInvoiceId=si.Id)
                        AND NOT EXISTS(SELECT 1 FROM SalesInvoiceCorrections x WHERE x.PreviousInvoiceId=si.Id)";
                    break;
                case "purchase":
                    source = @"SELECT p.Id,p.InvoiceNumber Reference,s.Name Party,p.InvoiceDate Date,p.TotalScaled AmountScaled
                        FROM PurchaseInvoices p JOIN Suppliers s ON s.Id=p.SupplierId WHERE p.Status='posted'";
                    break;
                case "sales-return":
                    source = @"SELECT r.Id,r.ReturnNumber Reference,COALESCE(c.Name,'نقدي مباشر') Party,r.CreatedUtc Date,r.TotalScaled AmountScaled
                        FROM SalesReturns r JOIN SalesInvoices si ON si.Id=r.SalesInvoiceId LEFT JOIN Customers c ON c.Id=si.CustomerId WHERE 1=1";
                    break;
                case "purchase-return":
                    source = @"SELECT r.Id,'PUR-RET-'||r.Id Reference,s.Name Party,r.CreatedUtc Date,r.TotalScaled AmountScaled
                        FROM PurchaseReturns r JOIN Suppliers s ON s.Id=r.SupplierId WHERE r.Status='posted'";
                    break;
                case "customer-payment":
                    source = @"SELECT p.Id,COALESCE(p.Reference,'COL-'||p.Id) Reference,c.Name Party,p.OccurredOn Date,p.AmountScaled
                        FROM CustomerTransactions p JOIN Customers c ON c.Id=p.CustomerId WHERE p.TransactionType IN ('collection','legacy_payment')";
                    break;
                case "supplier-payment":
                    source = @"SELECT p.Id,p.Reference,s.Name Party,p.OccurredOn Date,-p.AmountScaled AmountScaled
                        FROM SupplierLedgerEntries p JOIN Suppliers s ON s.Id=p.SupplierId WHERE p.EntryType IN ('payment','legacy_payment')";
                    break;
                case "shift-summary":
                case "shift-closing":
                    source = @"SELECT s.Id,'SHIFT-'||s.Id Reference,u.DisplayName Party,s.OpenedUtc Date,s.ClosingExpectedScaled AmountScaled
                        FROM CashierShifts s JOIN Users u ON u.Id=s.CashierId WHERE 1=1" + (type == "shift-closing" ? " AND s.ClosedUtc IS NOT NULL" : "");
                    break;
                default: throw new ArgumentException("Unsupported print document type.", nameof(type));
            }
            return "SELECT * FROM (" + source + ") WHERE 1=1" + filter;
        }
        private sealed class Row
        {
            public long Id { get; set; }
            public string Reference { get; set; }
            public string Party { get; set; }
            public string Date { get; set; }
            public long? AmountScaled { get; set; }
        }
    }
}
