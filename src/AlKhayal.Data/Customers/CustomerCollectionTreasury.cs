using System;
using System.Data.SQLite;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Treasury;
using AlKhayal.Data.Treasury;
using Dapper;

namespace AlKhayal.Data.Customers
{
    // The caller owns the SQLite transaction. A link is inserted before that transaction commits.
    internal static class CustomerCollectionTreasury
    {
        public static async Task<long> PostAsync(SQLiteConnection c, SQLiteTransaction tx, long transactionId,
            long? accountId, string method, long amount, string hash, long actor, CancellationToken ct)
        {
            if (!accountId.HasValue || accountId.Value <= 0 || actor <= 0)
                throw new TreasuryException("account_required", "اختر حساب خزنة أو بنك نشطًا للتحصيل.");
            var requiredType = method == "cash" ? "cash" :
                method == "card" || method == "transfer" || method == "wallet" ? "bank" : null;
            if (requiredType == null) throw new TreasuryException("payment_method", "طريقة التحصيل غير صالحة.");
            var account = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                "SELECT Type FROM TreasuryAccounts WHERE Id=@accountId AND IsDeleted=0", new { accountId }, tx, cancellationToken: ct)).ConfigureAwait(false);
            if (account == null) throw new TreasuryException("not_found", "الخزنة أو البنك غير موجود أو غير نشط.");
            if (account != requiredType) throw new TreasuryException("account_method_mismatch", "نوع الحساب لا يناسب طريقة التحصيل.");
            var movement = await SqliteTreasuryStore.PostWithinTransactionAsync(c, tx,
                new TreasuryMovementCommand { AccountId = accountId.Value,
                    OperationKey = "customer-collection:" + transactionId,
                    Notes = "تحصيل عميل رقم " + transactionId },
                "deposit", amount, hash, actor, ct).ConfigureAwait(false);
            if (movement.Replayed) throw new TreasuryException("operation_conflict", "قيد التحصيل مرتبط بحركة خزنة أخرى.");
            await c.ExecuteAsync(new CommandDefinition(
                "INSERT INTO CustomerCollectionTreasuryLinks(CustomerTransactionId,TreasuryMovementId,TreasuryAccountId,PaymentMethod) VALUES(@transactionId,@movementId,@accountId,@method)",
                new { transactionId, movementId = movement.Id, accountId, method }, tx, cancellationToken: ct)).ConfigureAwait(false);
            return movement.Id;
        }
    }
}
