using System;
using System.Data.SQLite;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Finance;
using AlKhayal.Application.Printing;
using Dapper;

namespace AlKhayal.Data.Printing
{
    public sealed class SqlitePrintPaymentReader : IPrintPaymentReader, IPrintContactReader
    {
        private readonly string _connectionString;
        public SqlitePrintPaymentReader(string connectionString) { _connectionString = connectionString; }
        public async Task<string> PhoneAsync(string type, long id, CancellationToken ct)
        {
            if (type != "customer" && type != "supplier") throw new ArgumentException("نوع الطرف غير صالح.", nameof(type));
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                return await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(type == "customer" ? "SELECT Phone FROM Customers WHERE Id=@Id" : "SELECT Phone FROM Suppliers WHERE Id=@Id", new { Id = id }, cancellationToken: ct)).ConfigureAwait(false);
            }
        }
        public async Task<PrintDocumentData> GetAsync(string type, long id, CancellationToken ct)
        {
            string sql;
            // Customer identity is CustomerTransactions.Id; includes sale-linked collections, never debt reductions.
            // Supplier identity is SupplierLedgerEntries.Id. These identity spaces must not be interchanged.
            if (type == "customer-payment") sql = "SELECT COALESCE(p.Reference,'COL-'||p.Id) Reference,p.AmountScaled,c.Name Party,c.Phone PartyPhone,p.OccurredOn Date,p.Notes Notes FROM CustomerTransactions p JOIN Customers c ON c.Id=p.CustomerId WHERE p.Id=@id AND p.TransactionType IN ('collection','legacy_payment')";
            else if (type == "supplier-payment") sql = "SELECT p.Reference,-p.AmountScaled AmountScaled,s.Name Party,s.Phone PartyPhone,p.OccurredOn Date,p.PaymentMethod Method,p.Notes Notes FROM SupplierLedgerEntries p JOIN Suppliers s ON s.Id=p.SupplierId WHERE p.Id=@id AND p.EntryType IN ('payment','legacy_payment')";
            else return null;
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                var row = await c.QuerySingleOrDefaultAsync<Payment>(new CommandDefinition(sql, new { id }, cancellationToken: ct)).ConfigureAwait(false);
                if (row == null) return null;
                var d = new PrintDocumentData { Type = type, Id = id, Reference = row.Reference, Title = type == "customer-payment" ? "سند قبض عميل" : "سند دفع مورد" };
                d.Fields["party"] = row.Party; d.Fields["date"] = row.Date;
                d.Fields["partyPhone"] = row.PartyPhone; d.Fields["paymentMethod"] = row.Method; d.Fields["paymentNotes"] = row.Notes;
                d.Fields["grandTotal"] = MoneyPolicy.FormatForUser(row.AmountScaled);
                return d;
            }
        }
        private sealed class Payment { public string Reference { get; set; } public string Party { get; set; } public string PartyPhone { get; set; } public string Date { get; set; } public string Method { get; set; } public string Notes { get; set; } public long AmountScaled { get; set; } }
    }
}
