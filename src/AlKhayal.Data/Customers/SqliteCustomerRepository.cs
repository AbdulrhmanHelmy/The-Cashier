using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;
using Dapper;

namespace AlKhayal.Data.Customers
{
    public sealed class SqliteCustomerRepository : ICustomerRepository
    {
        private readonly string _connectionString;
        public SqliteCustomerRepository(string connectionString) { _connectionString = connectionString; }

        private const string ArabicSearchVariants = "أإآةى";
        private const string ArabicSearchCanonical = "اااهي";
        private static readonly string ArabicSearchMarks = new string(Enumerable.Range(0x064B, 0x065F - 0x064B + 1).Select(x => (char)x).ToArray()) + (char)0x0670;
        private static readonly string DirectorySearch = " WHERE " + string.Join(" OR ",
            new[] { "Code", "Name", "Phone", "SecondaryPhone", "Address", "Status", "CAST(Balance AS TEXT)" }
                .Select(column => NormalizeSearchColumn(column) + " LIKE @Like ESCAPE '!'"));

        private static string NormalizeSearchColumn(string column)
        {
            for (var i = 0; i < ArabicSearchVariants.Length; i++)
                column = "replace(" + column + ",'" + ArabicSearchVariants[i] + "','" + ArabicSearchCanonical[i] + "')";
            foreach (var mark in ArabicSearchMarks)
                column = "replace(" + column + ",'" + mark + "','')";
            return column;
        }

        private static string NormalizeSearchValue(string value)
        {
            for (var i = 0; i < ArabicSearchVariants.Length; i++)
                value = value.Replace(ArabicSearchVariants[i], ArabicSearchCanonical[i]);
            return new string(value.Where(character => ArabicSearchMarks.IndexOf(character) < 0).ToArray()).Trim();
        }

        public Task<CustomerPage> ListPageAsync(string search, long page, int pageSize, string sort, bool descending, CancellationToken ct)
        {
            string order;
            switch (sort)
            {
                case "name": order = "Name"; break;
                case "phone": order = "Phone"; break;
                case "balance": order = "Balance"; break;
                case "address": order = "Address"; break;
                case "status": order = "Status"; break;
                default: order = "Code"; break;
            }
            var direction = descending ? " DESC" : " ASC";
            return Task.Run(async () =>
            {
                using (var c = new SQLiteConnection(_connectionString))
                {
                    await c.OpenAsync(ct).ConfigureAwait(false);
                    using (var tx = c.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                    {
                        var normalized = NormalizeSearchValue(search);
                        var source = " FROM Customers" + (normalized.Length == 0 ? "" : DirectorySearch);
                        var like = "%" + normalized.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_") + "%";
                        var total = await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*)" + source,
                            new { Like = like }, tx, cancellationToken: ct)).ConfigureAwait(false);
                        var current = System.Math.Min(page, System.Math.Max(1, total / pageSize + (total % pageSize == 0 ? 0 : 1)));
                        var rows = (await c.QueryAsync<Customer>(new CommandDefinition(
                            "SELECT Id,Code,Name,Phone,SecondaryPhone,Address,Balance,CreditLimit,Subscription,Status" + source +
                            " ORDER BY " + order + direction + ", Id" + direction + " LIMIT @Limit OFFSET @Offset",
                            new { Like = like, Limit = pageSize, Offset = (current - 1) * pageSize }, tx, cancellationToken: ct)).ConfigureAwait(false)).AsList();
                        tx.Commit();
                        return new CustomerPage { Items = rows, TotalCount = total, Page = current, PageSize = pageSize };
                    }
                }
            }, ct);
        }

        public List<Customer> GetAll()
        {
            using (var connection = new SQLiteConnection(_connectionString))
                return connection.Query<Customer>("SELECT Id, Code, Name, Phone, SecondaryPhone, Address, Balance, CreditLimit, Subscription, Status FROM Customers ORDER BY Id DESC").ToList();
        }
        public async Task<IReadOnlyList<Customer>> SearchAsync(string query, int limit, CancellationToken ct)
        {
            var escaped=(query??string.Empty).Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_");
            using(var connection=new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                var rows=await connection.QueryAsync<Customer>(new CommandDefinition(@"SELECT Id, Code, Name, Phone, SecondaryPhone, Address, Status FROM Customers
                    WHERE Status='نشط' AND (@Query='' OR Name LIKE @Like ESCAPE '\' OR Code LIKE @Like ESCAPE '\' OR Phone LIKE @Like ESCAPE '\' OR SecondaryPhone LIKE @Like ESCAPE '\')
                    ORDER BY Name, Id LIMIT @Limit",new{Query=query,Like="%"+escaped+"%",Limit=limit},cancellationToken:ct)).ConfigureAwait(false);
                return rows.ToList();
            }
        }

        public Customer GetById(long id)
        {
            using (var connection = new SQLiteConnection(_connectionString))
                return connection.QuerySingleOrDefault<Customer>("SELECT Id, Code, Name, Phone, SecondaryPhone, Address, Balance, CreditLimit, Subscription, Status FROM Customers WHERE Id = @Id", new { Id = id });
        }

        public long Add(Customer customer)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            { connection.Open();connection.Execute("PRAGMA busy_timeout=5000;");using(var tx=connection.BeginTransaction(System.Data.IsolationLevel.Serializable)) { var now=System.DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture); if(string.IsNullOrWhiteSpace(customer.Code))customer.Code=NextCode(connection,tx);var id=connection.ExecuteScalar<long>("INSERT INTO Customers (Code, Name, Phone, SecondaryPhone, Address, Balance, CreditLimit, Subscription, Status, CreatedAt, UpdatedAt) VALUES (@Code, @Name, @Phone, @SecondaryPhone, @Address, @Balance, @CreditLimit, @Subscription, @Status, @Now, @Now); SELECT last_insert_rowid();", new { customer.Code, customer.Name, customer.Phone, customer.SecondaryPhone, customer.Address, customer.Balance, customer.CreditLimit, customer.Subscription, customer.Status, Now=now },tx);if(connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CustomerTransactions'",transaction:tx)>0&&customer.Balance>0)connection.Execute("INSERT INTO CustomerTransactions(CustomerId,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc) VALUES(@Id,'opening',@Amount,@Date,'رصيد افتتاحي',@Now)",new{Id=id,Amount=checked((long)(customer.Balance*10000m)),Date=System.DateTime.UtcNow.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),Now=now},tx);tx.Commit();return id;}}
        }

        private static string NextCode(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // The transaction serializes writers. Historical free-form codes remain untouched;
            // only numeric CUS- codes participate in this sequence.
            var next=connection.ExecuteScalar<long>("SELECT COALESCE(MAX(CAST(SUBSTR(Code,5) AS INTEGER)),0)+1 FROM Customers WHERE UPPER(Code) GLOB 'CUS-[0-9]*' AND SUBSTR(Code,5) NOT GLOB '*[^0-9]*'",transaction:transaction);
            while(true)
            {
                var code="CUS-"+next.ToString("000",CultureInfo.InvariantCulture);
                if(connection.ExecuteScalar<long>("SELECT COUNT(*) FROM Customers WHERE Code=@Code",new{Code=code},transaction)==0)return code;
                next++;
            }
        }

        public bool Update(Customer customer)
        {
            using (var connection = new SQLiteConnection(_connectionString))
                return connection.Execute("UPDATE Customers SET Code=@Code, Name=@Name, Phone=@Phone, SecondaryPhone=@SecondaryPhone, Address=@Address, Subscription=@Subscription, Status=@Status, UpdatedAt=@Now WHERE Id=@Id", new { customer.Id, customer.Code, customer.Name, customer.Phone, customer.SecondaryPhone, customer.Address, customer.Subscription, customer.Status, Now = System.DateTime.UtcNow.ToString("o") }) == 1;
        }

        public bool Delete(long id)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            { connection.Open();using(var tx=connection.BeginTransaction()){if(connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CustomerTransactions'",transaction:tx)>0){var other=connection.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE CustomerId=@Id AND TransactionType<>'opening'",new{Id=id},tx);if(other>0){tx.Rollback();return false;}connection.Execute("DELETE FROM CustomerTransactions WHERE CustomerId=@Id AND TransactionType='opening'",new{Id=id},tx);}var deleted=connection.Execute("DELETE FROM Customers WHERE Id=@Id",new { Id=id},tx)==1;tx.Commit();return deleted;} }
        }
    }
}
