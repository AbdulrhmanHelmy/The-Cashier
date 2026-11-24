using System;
using System.Collections.Generic;
using System.Globalization;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;
using AlKhayal.Application.Finance;
namespace AlKhayal.Application.Customers
{
    public sealed class CustomerAccountService
    {
        private readonly ICustomerAccountRepository _accounts;
        public CustomerAccountService(ICustomerAccountRepository accounts) { _accounts = accounts; }
        public CustomerPayment AddPayment(long customerId, CustomerPayment payment, long actor = 0)
        {
            if (payment == null) throw new ArgumentException("بيانات الدفعة مطلوبة.");
            if (customerId <= 0) throw new ArgumentException("اختر عميلًا صحيحًا.");
            if (payment.Amount <= 0 || !MoneyPolicy.HasAtMostTwoDecimals(payment.Amount)) throw new ArgumentException("قيمة الدفعة يجب أن تكون موجبة وبدقة لا تتجاوز خانتين عشريتين.");
            if (string.IsNullOrWhiteSpace(payment.Method)) throw new ArgumentException("طريقة السداد مطلوبة.");
            if (string.IsNullOrWhiteSpace(payment.Account)) throw new ArgumentException("حساب السداد مطلوب.");
            if (payment.Notes != null && payment.Notes.Length > 1000) throw new ArgumentException("الملاحظات يجب ألا تتجاوز 1000 حرف.");
            if (string.IsNullOrWhiteSpace(payment.OperationKey) || payment.OperationKey.Trim().Length > 120 || !payment.TreasuryAccountId.HasValue || payment.TreasuryAccountId <= 0 || actor <= 0) throw new ArgumentException("اختر حساب التحصيل ومفتاح العملية.");
            var method=payment.Method.Trim();if(method=="نقدي")method="cash";
            if(!(method=="cash"||method=="card"||method=="transfer"||method=="wallet"))throw new ArgumentException("طريقة التحصيل غير صالحة.");
            return _accounts.AddPayment(customerId, payment.Amount, payment.PaymentDate, method, payment.Account.Trim(), payment.Notes,payment.OperationKey.Trim(),payment.TreasuryAccountId,actor);
        }
        private static void ValidateStatementDates(string from, string to)
        {
            DateTime parsed;
            if (!string.IsNullOrWhiteSpace(from) && !DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) throw new ArgumentException("تاريخ البداية غير صحيح.");
            if (!string.IsNullOrWhiteSpace(to) && !DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) throw new ArgumentException("تاريخ النهاية غير صحيح.");
            if (!string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to) && string.CompareOrdinal(from, to) > 0) throw new ArgumentException("تاريخ البداية يجب ألا يكون بعد تاريخ النهاية.");

        }
        public CustomerStatement GetStatement(long customerId, string from, string to)
        {
            ValidateStatementDates(from, to);
            return _accounts.GetStatement(customerId, from, to);
        }

        public System.Threading.Tasks.Task<CustomerStatement> GetStatementPageAsync(long customerId, string from, string to, long page, int pageSize, System.Threading.CancellationToken ct)
        {
            ValidateStatementDates(from, to);
            if (page < 1 || (pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200))
                throw new ArgumentException("رقم الصفحة أو حجمها غير صالح.");
            return System.Threading.Tasks.Task.Run(() => _accounts.GetStatementPage(customerId, from, to, page, pageSize, ct), ct);
        }

        public List<CustomerPayment> GetPayments(long customerId) { return _accounts.GetPayments(customerId); }
        public CustomerStatementTransaction PostTransaction(long customerId,string type,decimal amount,string occurredOn,string notes,string operationKey,string method=null,long? treasuryAccountId=null,long actor=0)
        {
            if(customerId<=0||amount<=0||!MoneyPolicy.HasAtMostTwoDecimals(amount)) throw new ArgumentException("أدخل مبلغًا موجبًا بدقة لا تتجاوز خانتين عشريتين.");
            if(type!="increase"&&type!="collection"&&type!="reduction") throw new ArgumentException("نوع العملية غير صالح.");
            if(string.IsNullOrWhiteSpace(operationKey)||operationKey.Trim().Length>120) throw new ArgumentException("تعذر التحقق من تكرار العملية. حدّث الصفحة وحاول مرة أخرى.");
            DateTime date;if(!DateTime.TryParseExact(occurredOn,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date)) throw new ArgumentException("تاريخ العملية غير صحيح.");
            if(notes!=null&&notes.Length>1000)throw new ArgumentException("الملاحظات يجب ألا تتجاوز 1000 حرف.");
            if(type=="collection"&&(!treasuryAccountId.HasValue||treasuryAccountId<=0||actor<=0||!(method=="cash"||method=="card"||method=="transfer"||method=="wallet")))throw new ArgumentException("اختر طريقة التحصيل وحساب الخزنة أو البنك.");
            if(type!="collection"&&(method!=null||treasuryAccountId.HasValue))throw new ArgumentException("حساب الخزنة يخص التحصيل فقط.");
            return _accounts.PostTransaction(customerId,type,amount,date.ToString("yyyy-MM-dd"),notes,operationKey.Trim(),method,treasuryAccountId,actor);
        }
    }
}
