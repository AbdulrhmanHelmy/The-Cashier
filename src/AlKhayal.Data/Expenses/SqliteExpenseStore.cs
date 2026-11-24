using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Expenses;
using AlKhayal.Application.Treasury;
using AlKhayal.Data.Treasury;
using Dapper;

namespace AlKhayal.Data.Expenses
{
    public sealed class SqliteExpenseStore : IExpenseStore
    {
        private readonly string _connectionString;
        private const string VoucherSql = "SELECT Id,CategoryId,CategoryName,TreasuryAccountId,AccountName,TreasuryMovementId,AmountScaled,ExpenseDate,Payee,Notes,UserId,CreatedUtc FROM ExpenseVouchers";
        public SqliteExpenseStore(string connectionString) { _connectionString = connectionString; }
        public async Task<IReadOnlyList<ExpenseCategory>> CategoriesAsync(CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                return (await c.QueryAsync<ExpenseCategory>(new CommandDefinition("SELECT Id,Name FROM ExpenseCategories ORDER BY Id", cancellationToken: ct)).ConfigureAwait(false)).ToList();
            }
        }
        public async Task<ExpenseCategory> AddCategoryAsync(string name, string normalizedName, long userId, CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                using (var tx = c.BeginTransaction())
                {
                    // A repeated create after a lost response returns the single normalized type.
                    var previous = await c.QuerySingleOrDefaultAsync<ExpenseCategory>(new CommandDefinition("SELECT Id,Name FROM ExpenseCategories WHERE NormalizedName=@normalizedName", new { normalizedName }, tx, cancellationToken: ct)).ConfigureAwait(false);
                    if (previous != null) { tx.Commit(); return previous; }
                    var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                    var id = await c.ExecuteScalarAsync<long>(new CommandDefinition("INSERT INTO ExpenseCategories(Name,NormalizedName,UserId,CreatedUtc) VALUES(@name,@normalizedName,@userId,@now); SELECT last_insert_rowid();", new { name, normalizedName, userId, now }, tx, cancellationToken: ct)).ConfigureAwait(false);
                    tx.Commit();
                    return new ExpenseCategory { Id = id, Name = name };
                }
            }
        }
        public Task<ExpenseVoucherPage> VoucherPageAsync(long page, CancellationToken ct)
        {
            return Task.Run(async () =>
            {
                const int pageSize = 50;
                using (var c = new SQLiteConnection(_connectionString))
                {
                    await c.OpenAsync(ct).ConfigureAwait(false);
                    using (var tx = c.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                    {
                        var count = await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM ExpenseVouchers",
                            transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                        var currentPage = Math.Min(page, Math.Max(1, count / pageSize + (count % pageSize == 0 ? 0 : 1)));
                        var items = (await c.QueryAsync<ExpenseVoucher>(new CommandDefinition(VoucherSql +
                            " ORDER BY Id DESC LIMIT @Limit OFFSET @Offset", new { Limit = pageSize, Offset = (currentPage - 1) * pageSize },
                            tx, cancellationToken: ct)).ConfigureAwait(false)).AsList();
                        tx.Commit();
                        return new ExpenseVoucherPage { Items = items, TotalCount = count, Page = currentPage };
                    }
                }
            }, ct);
        }

        public async Task<IReadOnlyList<ExpenseVoucher>> VouchersAsync(CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                return (await c.QueryAsync<ExpenseVoucher>(new CommandDefinition(VoucherSql + " ORDER BY Id DESC LIMIT 200", cancellationToken: ct)).ConfigureAwait(false)).ToList();
            }
        }
        public async Task<IReadOnlyList<ExpenseVoucher>> SearchVouchersAsync(string query, CancellationToken ct)
        {
            var like = "%" + query.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_") + "%";
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                var rows = await c.QueryAsync<ExpenseVoucher>(new CommandDefinition(@"
                    SELECT Id, CategoryName, Payee FROM ExpenseVouchers
                    WHERE printf('EXP-%06d',Id) LIKE @Like ESCAPE '!' OR Payee LIKE @Like ESCAPE '!' OR CategoryName LIKE @Like ESCAPE '!'
                    ORDER BY CASE WHEN printf('EXP-%06d',Id)=@Query COLLATE NOCASE THEN 0 ELSE 1 END,Id DESC LIMIT 20",
                    new { Query = query, Like = like }, cancellationToken: ct)).ConfigureAwait(false);
                return rows.ToList();
            }
        }

        public async Task<ExpenseVoucher> GetVoucherAsync(long id, CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                return await c.QuerySingleOrDefaultAsync<ExpenseVoucher>(
                    new CommandDefinition(VoucherSql + " WHERE Id=@Id", new { Id = id }, cancellationToken: ct)).ConfigureAwait(false);
            }
        }

        public async Task<ExpenseVoucher> PostAsync(ExpenseCommand command, long amount, string hash, long userId, CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_connectionString))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                using (var tx = c.BeginTransaction())
                {
                    var previousHash = await c.ExecuteScalarAsync<string>(new CommandDefinition("SELECT RequestHash FROM ExpenseVouchers WHERE OperationKey=@OperationKey", new { command.OperationKey }, tx, cancellationToken: ct)).ConfigureAwait(false);
                    if (previousHash != null)
                    {
                        if (hash != previousHash) throw new ExpenseException("operation_conflict", "رقم العملية مستخدم ببيانات أخرى. ابدأ سندًا جديدًا.");
                        var previous = await c.QuerySingleAsync<ExpenseVoucher>(new CommandDefinition(VoucherSql + " WHERE OperationKey=@OperationKey", new { command.OperationKey }, tx, cancellationToken: ct)).ConfigureAwait(false);
                        previous.Replayed = true; tx.Commit(); return previous;
                    }
                    var category = await c.ExecuteScalarAsync<string>(new CommandDefinition("SELECT Name FROM ExpenseCategories WHERE Id=@CategoryId", new { command.CategoryId }, tx, cancellationToken: ct)).ConfigureAwait(false);
                    if (category == null) throw new ExpenseException("not_found", "نوع المصروف غير موجود. أضفه من إدارة المصروفات أولًا.");
                    var account = await c.ExecuteScalarAsync<string>(new CommandDefinition("SELECT Name FROM TreasuryAccounts WHERE Id=@TreasuryAccountId AND IsDeleted=0", new { command.TreasuryAccountId }, tx, cancellationToken: ct)).ConfigureAwait(false);
                    if (account == null) throw new ExpenseException("not_found", "الخزنة أو البنك غير موجود.");
                    var movement = await SqliteTreasuryStore.PostWithinTransactionAsync(c, tx, new TreasuryMovementCommand {
                        AccountId = command.TreasuryAccountId, Amount = command.Amount, Notes = "مصروف — " + category + " — " + command.Payee + " — " + command.Notes,
                        OperationKey = "expense:" + command.OperationKey
                    }, "withdraw", amount, hash, userId, ct).ConfigureAwait(false);
                    // A preexisting independent movement must never be claimed by a new voucher.
                    if (movement.Replayed) throw new ExpenseException("operation_conflict", "رقم العملية مستخدم بحركة مالية أخرى.");
                    var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                    var id = await c.ExecuteScalarAsync<long>(new CommandDefinition(@"INSERT INTO ExpenseVouchers(CategoryId,CategoryName,TreasuryAccountId,AccountName,TreasuryMovementId,AmountScaled,ExpenseDate,Payee,Notes,OperationKey,RequestHash,UserId,CreatedUtc)
                        VALUES(@CategoryId,@category,@TreasuryAccountId,@account,@movementId,@amount,@ExpenseDate,@Payee,@Notes,@OperationKey,@hash,@userId,@now); SELECT last_insert_rowid();",
                        new { command.CategoryId, category, command.TreasuryAccountId, account, movementId = movement.Id, amount, command.ExpenseDate, command.Payee, command.Notes, command.OperationKey, hash, userId, now }, tx, cancellationToken: ct)).ConfigureAwait(false);
                    var result = await c.QuerySingleAsync<ExpenseVoucher>(new CommandDefinition(VoucherSql + " WHERE Id=@id", new { id }, tx, cancellationToken: ct)).ConfigureAwait(false);
                    tx.Commit(); return result;
                }
            }
        }
    }
}
