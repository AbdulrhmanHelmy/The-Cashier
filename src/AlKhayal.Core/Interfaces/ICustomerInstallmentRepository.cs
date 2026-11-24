using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Core.Models;

namespace AlKhayal.Core.Interfaces
{
    public interface ICustomerInstallmentRepository
    {
        CustomerInstallmentContract Create(long customerId, long productId, decimal productPrice, decimal downPayment,
            decimal interestRate, int count, int intervalMonths, string firstDueOn, decimal totalAmount,
            IReadOnlyList<CustomerInstallmentRow> schedule, string operationKey, string requestHash, long? treasuryAccountId, long actor);
        List<CustomerInstallmentContract> GetByCustomer(long customerId);
        Task<IReadOnlyList<CustomerInstallmentContract>> SearchAsync(string query, CancellationToken ct);
        CustomerInstallmentContract Pay(long customerId, long contractId, int installmentNumber, string paidOn,
            string operationKey, string requestHash, string method, long? treasuryAccountId, long actor, long? amountScaled = null);
    }
}
