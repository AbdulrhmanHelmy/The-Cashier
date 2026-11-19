using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AlKhayal.Application.Finance;
using AlKhayal.Core.Interfaces;
using AlKhayal.Core.Models;

namespace AlKhayal.Application.Customers
{
    public sealed class InstallmentService
    {
        private readonly ICustomerInstallmentRepository _repository;
        public InstallmentService(ICustomerInstallmentRepository repository) { _repository = repository; }

        public CustomerInstallmentContract Create(long customerId, long productId, decimal productPrice, decimal downPayment,
            decimal interestRate, int count, int intervalMonths, string firstDueOn, string operationKey, long? treasuryAccountId = null, long actor = 0)
        {
            if (customerId <= 0 || productId <= 0) throw new ArgumentException("اختر العميل والمنتج.");
            if (productPrice <= 0 || !MoneyPolicy.HasAtMostTwoDecimals(productPrice)) throw new ArgumentException("سعر المنتج يجب أن يكون موجبًا وبدقة لا تتجاوز خانتين.");
            if (downPayment < 0 || downPayment >= productPrice || !MoneyPolicy.HasAtMostTwoDecimals(downPayment)) throw new ArgumentException("المقدم يجب أن يكون أقل من سعر المنتج.");
            if (downPayment > 0 && (!treasuryAccountId.HasValue || treasuryAccountId <= 0 || actor <= 0)) throw new ArgumentException("اختر خزنة نشطة لتسجيل المقدم.");
            if (interestRate < 0 || interestRate > 1000 || !MoneyPolicy.HasAtMostTwoDecimals(interestRate)) throw new ArgumentException("نسبة الفائدة غير صالحة.");
            if (count < 1 || count > 120) throw new ArgumentException("عدد الأقساط يجب أن يكون من 1 إلى 120.");
            if (intervalMonths < 1 || intervalMonths > 12) throw new ArgumentException("الفترة بين الأقساط يجب أن تكون من شهر إلى 12 شهرًا.");
            if (string.IsNullOrWhiteSpace(operationKey) || operationKey.Trim().Length > 120) throw new ArgumentException("مفتاح العملية مطلوب.");
            DateTime first;
            if (!DateTime.TryParseExact(firstDueOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out first)) throw new ArgumentException("تاريخ أول قسط غير صحيح.");

            var financed = decimal.Round((productPrice - downPayment) * (1m + interestRate / 100m), 2, MidpointRounding.AwayFromZero);
            var fullAmount = checked(financed + downPayment);
            var cents = checked((long)(financed * 100m));
            var each = cents / count;
            var remainder = cents % count;
            var schedule = new List<CustomerInstallmentRow>();
            for (var i = 0; i < count; i++)
            {
                var due = first.AddMonths(checked(i * intervalMonths));
                var amount = each + (i == count - 1 ? remainder : 0);
                schedule.Add(new CustomerInstallmentRow { Number = i + 1, DueDate = due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Amount = amount / 100m, Status = "مستحق" });
            }
            var key = operationKey.Trim();
            return _repository.Create(customerId, productId, productPrice, downPayment, interestRate, count, intervalMonths,
                first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), fullAmount, schedule, key,
                Hash(string.Join("|", customerId, productId, F(productPrice), F(downPayment), F(interestRate), count, intervalMonths, first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))+(treasuryAccountId.HasValue?"|"+treasuryAccountId.Value.ToString(CultureInfo.InvariantCulture):"")), treasuryAccountId, actor);
        }

        public List<CustomerInstallmentContract> GetByCustomer(long customerId)
        {
            if (customerId <= 0) throw new ArgumentException("معرف العميل غير صالح.");
            return _repository.GetByCustomer(customerId);
        }

        public CustomerInstallmentContract Pay(long customerId, long contractId, int installmentNumber, string paidOn, string operationKey, string method = null, long? treasuryAccountId = null, long actor = 0, string amount = null)
        {
            if (customerId <= 0 || contractId <= 0 || installmentNumber <= 0) throw new ArgumentException("بيانات القسط غير صالحة.");
            if (string.IsNullOrWhiteSpace(operationKey) || operationKey.Trim().Length > 120) throw new ArgumentException("مفتاح العملية مطلوب.");
            if (!treasuryAccountId.HasValue || treasuryAccountId <= 0 || actor <= 0 || !(method == "cash" || method == "card" || method == "transfer" || method == "wallet")) throw new ArgumentException("اختر طريقة السداد وحساب الخزنة أو البنك.");
            long parsedAmount=0;if(amount!=null&&(!MoneyPolicy.TryParseUserAmount(amount,false,out parsedAmount)||parsedAmount<=0))throw new ArgumentException("مبلغ السداد غير صالح.");
            DateTime date;
            if (!DateTime.TryParseExact(paidOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) throw new ArgumentException("تاريخ السداد غير صحيح.");
            var key = operationKey.Trim(); var normalizedDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return _repository.Pay(customerId, contractId, installmentNumber, normalizedDate, key,
                Hash(string.Join("|", customerId, contractId, installmentNumber, normalizedDate, method, treasuryAccountId, amount==null?"":parsedAmount.ToString(CultureInfo.InvariantCulture))), method, treasuryAccountId, actor, amount==null?(long?)null:parsedAmount);
        }

        private static string F(decimal value) { return value.ToString("0.00", CultureInfo.InvariantCulture); }
        private static string Hash(string value) { using (var sha = SHA256.Create()) { var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value)); var result = new StringBuilder(); foreach (var b in bytes) result.Append(b.ToString("x2", CultureInfo.InvariantCulture)); return result.ToString(); } }
    }
}
