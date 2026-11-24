using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Core.Models;

namespace AlKhayal.Core.Interfaces
{
    public interface ICustomerRepository
    {
        Task<CustomerPage> ListPageAsync(string search, long page, int pageSize, string sort, bool descending, CancellationToken ct);
        List<Customer> GetAll();
        Task<IReadOnlyList<Customer>> SearchAsync(string query, int limit, CancellationToken ct);
        Customer GetById(long id);
        long Add(Customer customer);
        bool Update(Customer customer);
        bool Delete(long id);
    }
}
