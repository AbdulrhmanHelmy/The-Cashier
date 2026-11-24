using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Core.Models;

namespace AlKhayal.Core.Interfaces
{
    public interface IMaintenanceRepository
    {
        MaintenanceTicket Create(MaintenanceTicket ticket, string operationKey, string requestHash);
        List<MaintenanceTicket> List(long? customerId, long? ticketId = null);
        Task<IReadOnlyList<MaintenanceTicket>> SearchAsync(string query, CancellationToken ct);
        MaintenanceTicket UpdateStatus(long ticketId, string status, string operationKey);
        MaintenanceTicket Deliver(long ticketId, decimal finalCost, string settlementType, string deliveredOn, string operationKey, string requestHash, long? treasuryAccountId, long actor);
    }
}
