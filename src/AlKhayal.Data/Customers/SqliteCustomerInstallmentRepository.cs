using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;
using AlKhayal.Application.Finance;
using AlKhayal.Application.Customers;
using Dapper;

namespace AlKhayal.Data.Customers
{
    public sealed class SqliteCustomerInstallmentRepository : ICustomerInstallmentRepository
    {
        private readonly string _connectionString;
        public SqliteCustomerInstallmentRepository(string connectionString) { _connectionString = connectionString; }

        public CustomerInstallmentContract Create(long customerId, long productId, decimal productPrice, decimal downPayment,
            decimal interestRate, int count, int intervalMonths, string firstDueOn, decimal totalAmount,
            IReadOnlyList<CustomerInstallmentRow> schedule, string operationKey, string requestHash, long? treasuryAccountId, long actor)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open(); using (var tx = connection.BeginTransaction())
                {
                    var old = connection.QueryFirstOrDefault<StoredContract>("SELECT Id,RequestHash FROM CustomerInstallmentContracts WHERE OperationKey=@operationKey", new { operationKey }, tx);
                    if (old != null)
                    {
                        if (!string.Equals(old.RequestHash, requestHash, StringComparison.Ordinal)) throw new CustomerOperationKeyConflictException("مفتاح العملية مستخدم لطلب تقسيط مختلف.");
                        tx.Commit(); return Get(connection, old.Id);
                    }
                    var customer = connection.QueryFirstOrDefault<CustomerRow>("SELECT Id,Name,Balance FROM Customers WHERE Id=@customerId", new { customerId }, tx);
                    if (customer == null) throw new KeyNotFoundException("العميل غير موجود.");
                    var product = connection.QueryFirstOrDefault<ProductRow>("SELECT p.Id,p.Name,d.SellPriceScaled FROM Products p JOIN ProductDetails d ON d.ProductId=p.Id WHERE p.Id=@productId AND d.IsActive=1 AND d.ArchivedUtc IS NULL AND d.MigrationState='ready'", new { productId }, tx);
                    if (product == null) throw new KeyNotFoundException("المنتج غير موجود أو غير نشط.");
                    var priceScaled = checked((long)(productPrice * 10000m));
                    if (MoneyPolicy.RoundInternalToPiastre(product.SellPriceScaled) != priceScaled) throw new ArgumentException("تغير سعر المنتج؛ حدّث الشاشة وأعد المحاولة.");
                    var downScaled = checked((long)(downPayment * 10000m));
                    var totalScaled = checked((long)(totalAmount * 10000m));
                    var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                    var number = "INS-" + Guid.NewGuid().ToString("N").Substring(0, 12).ToUpperInvariant();
                    connection.Execute(@"INSERT INTO CustomerInstallmentContracts(CustomerId,ProductId,OperationKey,RequestHash,ContractNumber,ProductName,ProductPriceScaled,DownPaymentScaled,InterestRateBasisPoints,TotalAmountScaled,InstallmentCount,IntervalMonths,FirstDueOn,CreatedUtc)
VALUES(@customerId,@productId,@operationKey,@requestHash,@number,@productName,@price,@down,@rate,@total,@count,@intervalMonths,@firstDueOn,@now)",
                        new { customerId, productId, operationKey, requestHash, number, productName = product.Name, price = priceScaled, down = downScaled, rate = checked((long)(interestRate * 100m)), total = totalScaled, count, intervalMonths, firstDueOn, now }, tx);
                    var contractId = connection.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: tx);
                    foreach (var row in schedule)
                        connection.Execute("INSERT INTO CustomerInstallmentSchedule(ContractId,InstallmentNumber,DueOn,AmountScaled) VALUES(@contractId,@number,@dueOn,@amount)", new { contractId, number = row.Number, dueOn = row.DueDate, amount = checked((long)(row.Amount * 10000m)) }, tx);

                    var currentLedger = connection.ExecuteScalar<long>("SELECT COALESCE(SUM(CASE WHEN TransactionType IN ('opening','increase') THEN AmountScaled ELSE -AmountScaled END),0) FROM CustomerTransactions WHERE CustomerId=@customerId", new { customerId }, tx);
                    if(checked((long)(customer.Balance*10000m))!=currentLedger)throw new ArgumentException("رصيد العميل لا يطابق دفتر الحساب؛ لم يتم حفظ العقد.");
                    var nextBalanceScaled = checked(currentLedger + totalScaled - downScaled);
                    connection.Execute("INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc,Reference) VALUES(@customerId,@key,@requestHash,'increase',@amount,@date,@notes,@now,@reference)",
                        new { customerId, key = "installment-contract:" + contractId + ":sale", requestHash, amount = totalScaled, date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), notes = "بيع بالتقسيط: " + product.Name, now, reference = number }, tx);
                    if (downScaled > 0)
                    {
                        connection.Execute("INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc,Reference) VALUES(@customerId,@key,@requestHash,'collection',@amount,@date,@notes,@now,@reference)",
                            new { customerId, key = "installment-contract:" + contractId + ":down-payment", requestHash, amount = downScaled, date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), notes = "مقدم بيع بالتقسيط", now, reference = number }, tx);
                        var ledgerId=connection.ExecuteScalar<long>("SELECT last_insert_rowid()",transaction:tx);
                        var movement=CustomerCollectionTreasury.PostAsync(connection,tx,ledgerId,treasuryAccountId,"cash",downScaled,requestHash,actor,System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                        AlKhayal.Data.Workforce.SqliteWorkforceStore.AssociateAsync(connection,tx,actor,"collection",downScaled,null,null,System.Threading.CancellationToken.None,movement).GetAwaiter().GetResult();
                    }
                    connection.Execute("UPDATE Customers SET Balance=@balance,UpdatedAt=@now WHERE Id=@customerId", new { balance = nextBalanceScaled / 10000m, now, customerId }, tx);
                    tx.Commit(); return Get(connection, contractId);
                }
            }
        }

        public List<CustomerInstallmentContract> GetByCustomer(long customerId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                var ids = connection.Query<long>("SELECT Id FROM CustomerInstallmentContracts WHERE CustomerId=@customerId ORDER BY Id DESC", new { customerId }).ToList();
                return ids.Select(id => Get(connection, id)).ToList();
            }
        }

        public CustomerInstallmentContract Pay(long customerId, long contractId, int installmentNumber, string paidOn, string operationKey, string requestHash, string method, long? treasuryAccountId, long actor, long? requestedAmountScaled = null)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open(); using (var tx = connection.BeginTransaction())
                {
                    var replay = connection.QueryFirstOrDefault<StoredPayment>(@"SELECT p.ContractId,p.InstallmentNumber,p.RequestHash,c.CustomerId FROM
                        (SELECT ContractId,InstallmentNumber,RequestHash,OperationKey FROM CustomerInstallmentPayments UNION ALL SELECT ContractId,InstallmentNumber,RequestHash,OperationKey FROM CustomerInstallmentPartialPayments) p
                        JOIN CustomerInstallmentContracts c ON c.Id=p.ContractId WHERE p.OperationKey=@operationKey", new { operationKey }, tx);
                    if (replay != null)
                    {
                        if (replay.CustomerId != customerId || replay.ContractId != contractId || replay.InstallmentNumber != installmentNumber || !string.Equals(replay.RequestHash, requestHash, StringComparison.Ordinal)) throw new CustomerOperationKeyConflictException("مفتاح العملية مستخدم لطلب سداد مختلف.");
                        tx.Commit(); return Get(connection, contractId);
                    }
                    if (connection.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE OperationKey=@operationKey", new { operationKey }, tx) != 0)
                        throw new CustomerOperationKeyConflictException("مفتاح العملية مستخدم في حركة عميل أخرى.");
                    var contract = connection.QueryFirstOrDefault<StoredContract>("SELECT Id,CustomerId,ContractNumber,RequestHash,SalesInvoiceId FROM CustomerInstallmentContracts WHERE Id=@contractId AND CustomerId=@customerId", new { contractId, customerId }, tx);
                    if (contract == null) throw new KeyNotFoundException("عقد التقسيط غير موجود لهذا العميل.");
                    var row = connection.QueryFirstOrDefault<StoredSchedule>(@"SELECT InstallmentNumber,AmountScaled-COALESCE((SELECT SUM(r.AmountScaled) FROM CustomerInstallmentReturnAllocations r WHERE r.ContractId=s.ContractId AND r.InstallmentNumber=s.InstallmentNumber),0) AmountScaled FROM CustomerInstallmentSchedule s WHERE ContractId=@contractId AND InstallmentNumber=@installmentNumber", new { contractId, installmentNumber }, tx);
                    if (row == null) throw new KeyNotFoundException("القسط غير موجود.");
                    var alreadyPaid=connection.ExecuteScalar<long>(@"SELECT COALESCE(SUM(AmountScaled),0) FROM
                        (SELECT AmountScaled FROM CustomerInstallmentPayments WHERE ContractId=@contractId AND InstallmentNumber=@installmentNumber
                        UNION ALL SELECT AmountScaled FROM CustomerInstallmentPartialPayments WHERE ContractId=@contractId AND InstallmentNumber=@installmentNumber)",new{contractId,installmentNumber},tx);
                    var remaining=checked(row.AmountScaled-alreadyPaid);
                    if(remaining<=0)throw new ArgumentException("تم سداد هذا القسط بالفعل.");
                    var amount=requestedAmountScaled??remaining;
                    if(amount<=0||amount>remaining)throw new ArgumentException("مبلغ السداد أكبر من المتبقي على القسط.");
                    var currentLedger = connection.ExecuteScalar<long>("SELECT COALESCE(SUM(CASE WHEN TransactionType IN ('opening','increase') THEN AmountScaled ELSE -AmountScaled END),0) FROM CustomerTransactions WHERE CustomerId=@customerId", new { customerId }, tx);
                    var customerBalance=connection.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@customerId",new{customerId},tx);
                    if(checked((long)(customerBalance*10000m))!=currentLedger)throw new ArgumentException("رصيد العميل لا يطابق دفتر الحساب؛ لم يتم سداد القسط.");
                    if (currentLedger < amount) throw new ArgumentException("رصيد العميل الحالي أقل من قيمة القسط؛ راجع كشف الحساب قبل السداد.");
                    var nextBalanceScaled = checked(currentLedger - amount);
                    var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                    var paymentTable=requestedAmountScaled.HasValue||alreadyPaid>0?"CustomerInstallmentPartialPayments":"CustomerInstallmentPayments";
                    var interestPaidSoFar=connection.ExecuteScalar<long>(@"SELECT COALESCE(SUM(InterestAmountScaled),0) FROM
                        (SELECT InterestAmountScaled FROM CustomerInstallmentPayments WHERE ContractId=@contractId
                         UNION ALL SELECT InterestAmountScaled FROM CustomerInstallmentPartialPayments WHERE ContractId=@contractId)",new{contractId},tx);
                    var interestTotal=connection.ExecuteScalar<long>("SELECT TotalAmountScaled-ProductPriceScaled FROM CustomerInstallmentContracts WHERE Id=@contractId",new{contractId},tx);
                    var interestAdjusted=connection.ExecuteScalar<long>("SELECT COALESCE(SUM(AmountScaled),0) FROM CustomerInstallmentInterestAdjustments WHERE ContractId=@contractId",new{contractId},tx);
                    var invoiceOutstanding=contract.SalesInvoiceId.HasValue?connection.ExecuteScalar<long>(@"SELECT MAX(0,i.TotalScaled-i.PaidScaled-COALESCE((SELECT SUM(r.TotalScaled) FROM SalesReturns r WHERE r.SalesInvoiceId=i.Id),0)) FROM SalesInvoices i WHERE i.Id=@Id",new{Id=contract.SalesInvoiceId.Value},tx):0;
                    var interestAmount=contract.SalesInvoiceId.HasValue?Math.Min(Math.Max(0,amount-Math.Min(amount,invoiceOutstanding)),Math.Max(0,interestTotal-interestPaidSoFar-interestAdjusted)):0;
                    connection.Execute("INSERT INTO "+paymentTable+"(ContractId,InstallmentNumber,OperationKey,RequestHash,AmountScaled,PaidOn,CreatedUtc,InterestAmountScaled) VALUES(@contractId,@installmentNumber,@operationKey,@requestHash,@amount,@paidOn,@now,@interestAmount)", new { contractId, installmentNumber, operationKey, requestHash, amount, paidOn, now, interestAmount }, tx);
                    var reference = contract.ContractNumber;
                    connection.Execute("INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc,Reference,SalesInvoiceId) VALUES(@customerId,@operationKey,@requestHash,'collection',@amount,@paidOn,@notes,@now,@reference,@invoiceId)", new { customerId, operationKey, requestHash, amount, paidOn, notes = "سداد القسط رقم " + installmentNumber, now, reference, invoiceId=contract.SalesInvoiceId }, tx);
                    var ledgerId=connection.ExecuteScalar<long>("SELECT last_insert_rowid()",transaction:tx);
                    connection.Execute(@"INSERT INTO InstallmentReceivableEntries(ContractId,EntryType,Direction,AmountScaled,OperationKey,RequestHash,CreatedUtc)
                        VALUES(@contractId,'payment','credit',@amount,@entryKey,@requestHash,@now)",new{contractId,amount,entryKey="installment-receivable:payment:"+operationKey,requestHash,now},tx);
                    var movement=CustomerCollectionTreasury.PostAsync(connection,tx,ledgerId,treasuryAccountId,method,amount,requestHash,actor,System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                    long? salePaymentId=null;
                    if(contract.SalesInvoiceId.HasValue)
                    {
                        var invoiceId=contract.SalesInvoiceId.Value;
                        var principalAmount=Math.Min(amount,invoiceOutstanding);
                        if(connection.Execute("UPDATE SalesInvoices SET PaidScaled=PaidScaled+@principalAmount WHERE Id=@invoiceId AND Status='posted' AND PaidScaled+@principalAmount<=TotalScaled AND NOT EXISTS(SELECT 1 FROM SalesCancellations WHERE SalesInvoiceId=@invoiceId)",new{principalAmount,invoiceId},tx)!=1)throw new ArgumentException("رصيد فاتورة البيع لا يسمح بهذا التحصيل.");
                        connection.Execute("INSERT INTO SalesPayments(SalesInvoiceId,CustomerId,ActorUserId,EventType,PaymentMethod,AmountScaled,OperationKey,RequestHash,OccurredOn,CreatedUtc) VALUES(@invoiceId,@customerId,@actor,'customer_collection',@method,@amount,@key,@requestHash,@paidOn,@now)",new{invoiceId,customerId,actor,method=method=="wallet"?"transfer":method,amount,key="installment-payment:"+operationKey,requestHash,paidOn,now},tx);
                        salePaymentId=connection.ExecuteScalar<long>("SELECT last_insert_rowid()",transaction:tx);
                    }
                    if(method=="cash")AlKhayal.Data.Workforce.SqliteWorkforceStore.AssociateAsync(connection,tx,actor,"collection",amount,contract.SalesInvoiceId,salePaymentId,System.Threading.CancellationToken.None,movement).GetAwaiter().GetResult();
                    var updated = connection.Execute("UPDATE Customers SET Balance=@balance,UpdatedAt=@now WHERE Id=@customerId", new { balance = nextBalanceScaled / 10000m, now, customerId }, tx);
                    if (updated != 1) throw new InvalidOperationException("تعذر تحديث رصيد العميل؛ لم يتم اعتماد سداد القسط.");
                    tx.Commit(); return Get(connection, contractId);
                }
            }
        }

        private static CustomerInstallmentContract Get(SQLiteConnection connection, long id)
        {
            var row = connection.QueryFirstOrDefault<ContractRow>(@"SELECT c.Id,c.CustomerId,u.Name AS CustomerName,c.ContractNumber,c.ProductId,c.SalesInvoiceId,c.ProductName,c.ProductPriceScaled,c.DownPaymentScaled,c.InterestRateBasisPoints,c.TotalAmountScaled,c.InterestReturnPolicy,c.InstallmentCount,c.IntervalMonths,c.FirstDueOn,
CASE WHEN NOT EXISTS(SELECT 1 FROM CustomerInstallmentSchedule s WHERE s.ContractId=c.Id AND MAX(0,s.AmountScaled-COALESCE((SELECT SUM(r.AmountScaled) FROM CustomerInstallmentReturnAllocations r WHERE r.ContractId=s.ContractId AND r.InstallmentNumber=s.InstallmentNumber),0))>(COALESCE((SELECT SUM(p.AmountScaled) FROM CustomerInstallmentPayments p WHERE p.ContractId=s.ContractId AND p.InstallmentNumber=s.InstallmentNumber),0)+COALESCE((SELECT SUM(p.AmountScaled) FROM CustomerInstallmentPartialPayments p WHERE p.ContractId=s.ContractId AND p.InstallmentNumber=s.InstallmentNumber),0))) THEN 'مكتمل' ELSE 'قائم' END AS Status
FROM CustomerInstallmentContracts c JOIN Customers u ON u.Id=c.CustomerId WHERE c.Id=@id", new { id });
            if (row == null) return null;
            var result = new CustomerInstallmentContract { Id=row.Id,CustomerId=row.CustomerId,CustomerName=row.CustomerName,ContractNumber=row.ContractNumber,ProductId=row.ProductId,SalesInvoiceId=row.SalesInvoiceId,ProductName=row.ProductName,ProductPrice=row.ProductPriceScaled/10000m,DownPayment=row.DownPaymentScaled/10000m,InterestRate=row.InterestRateBasisPoints/100m,InterestReturnPolicy=row.InterestReturnPolicy,InterestAmount=(row.TotalAmountScaled-row.ProductPriceScaled)/10000m,TotalAmount=row.TotalAmountScaled/10000m,InstallmentCount=row.InstallmentCount,IntervalMonths=row.IntervalMonths,FirstDueOn=row.FirstDueOn,Status=row.Status };
            result.Schedule=connection.Query<ScheduleRow>(@"SELECT s.InstallmentNumber AS Number,s.DueOn AS DueDate,MAX(0,s.AmountScaled-COALESCE((SELECT SUM(r.AmountScaled) FROM CustomerInstallmentReturnAllocations r WHERE r.ContractId=s.ContractId AND r.InstallmentNumber=s.InstallmentNumber),0)) AmountScaled,
                COALESCE((SELECT SUM(p.AmountScaled) FROM CustomerInstallmentPayments p WHERE p.ContractId=s.ContractId AND p.InstallmentNumber=s.InstallmentNumber),0)+COALESCE((SELECT SUM(p.AmountScaled) FROM CustomerInstallmentPartialPayments p WHERE p.ContractId=s.ContractId AND p.InstallmentNumber=s.InstallmentNumber),0) PaidScaled,
                NULLIF(MAX(COALESCE((SELECT MAX(p.PaidOn) FROM CustomerInstallmentPayments p WHERE p.ContractId=s.ContractId AND p.InstallmentNumber=s.InstallmentNumber),''),COALESCE((SELECT MAX(p.PaidOn) FROM CustomerInstallmentPartialPayments p WHERE p.ContractId=s.ContractId AND p.InstallmentNumber=s.InstallmentNumber),'')),'') PaidOn
                FROM CustomerInstallmentSchedule s WHERE s.ContractId=@id ORDER BY s.InstallmentNumber",new{id}).Select(x=>new CustomerInstallmentRow{Number=x.Number,DueDate=x.DueDate,Amount=x.AmountScaled/10000m,PaidAmount=x.PaidScaled/10000m,RemainingAmount=Math.Max(0,x.AmountScaled-x.PaidScaled)/10000m,PaidOn=x.PaidOn,Status=x.PaidScaled>=x.AmountScaled?"مسدد":x.PaidScaled>0?"جزئي":"مستحق"}).ToList();
            result.RemainingAmount=result.Schedule.Sum(x=>x.RemainingAmount);
            var returned=row.SalesInvoiceId.HasValue?connection.ExecuteScalar<long>("SELECT COALESCE(SUM(TotalScaled),0) FROM SalesReturns WHERE SalesInvoiceId=@Id",new{Id=row.SalesInvoiceId.Value}):0;
            var adjustedInterest=connection.ExecuteScalar<long>("SELECT COALESCE(SUM(AmountScaled),0) FROM CustomerInstallmentInterestAdjustments WHERE ContractId=@id",new{id});
            result.AdjustedTotalAmount=(row.TotalAmountScaled-returned-adjustedInterest)/10000m;
            return result;
        }

        private sealed class StoredContract { public long Id { get; set; } public long CustomerId { get; set; } public long? SalesInvoiceId { get; set; } public string ContractNumber { get; set; } public string RequestHash { get; set; } }
        private sealed class ContractRow { public long Id { get; set; } public long CustomerId { get; set; } public string CustomerName { get; set; } public string ContractNumber { get; set; } public long ProductId { get; set; } public long? SalesInvoiceId { get; set; } public string ProductName { get; set; } public long ProductPriceScaled { get; set; } public long DownPaymentScaled { get; set; } public long InterestRateBasisPoints { get; set; } public long TotalAmountScaled { get; set; } public string InterestReturnPolicy { get; set; } public int InstallmentCount { get; set; } public int IntervalMonths { get; set; } public string FirstDueOn { get; set; } public string Status { get; set; } }
        private sealed class CustomerRow { public long Id { get; set; } public string Name { get; set; } public decimal Balance { get; set; } }
        private sealed class ProductRow { public long Id { get; set; } public string Name { get; set; } public long SellPriceScaled { get; set; } }
        private sealed class StoredPayment { public long ContractId { get; set; } public int InstallmentNumber { get; set; } public string RequestHash { get; set; } public long CustomerId { get; set; } }
        private sealed class StoredSchedule { public int InstallmentNumber { get; set; } public long AmountScaled { get; set; } public string PaidOn { get; set; } }
        private sealed class ScheduleRow { public int Number { get; set; } public string DueDate { get; set; } public long AmountScaled { get; set; } public long PaidScaled { get; set; } public string PaidOn { get; set; } }
    }
}
