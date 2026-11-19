using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Api.Contracts;

namespace AlKhayal.Api
{
    // All HTTP database work, including license/auth and post-response auditing,
    // finishes before the restore request upgrades to exclusive access.
    public sealed class BackupMaintenanceHandler : DelegatingHandler
    {
        private const string LeaseKey = "AlKhayal.BackupMaintenance";
        private readonly object sync = new object();
        private int requests;
        private Lease exclusive;
        private TaskCompletionSource<bool> drained;

        public bool IsExclusive { get { lock (sync) return exclusive != null; } }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var lease = new Lease(this);
            lock (sync)
            {
                if (exclusive != null)
                    return request.CreateResponse(HttpStatusCode.ServiceUnavailable,
                        ApiEnvelope<object>.Fail("maintenance", "تجري استعادة قاعدة البيانات. حاول مجددًا بعد اكتمالها."));
                requests++;
                request.Properties[LeaseKey] = lease;
            }

            try { return await base.SendAsync(request, ct).ConfigureAwait(false); }
            finally
            {
                lock (sync)
                {
                    requests--;
                    if (exclusive == lease) exclusive = null;
                    else if (requests == 1 && exclusive != null) drained.TrySetResult(true);
                }
            }
        }

        internal static Task<bool> EnterAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!request.Properties.TryGetValue(LeaseKey, out var value) || !(value is Lease lease))
                return Task.FromResult(false);
            return lease.Owner.UpgradeAsync(lease, ct);
        }

        private async Task<bool> UpgradeAsync(Lease lease, CancellationToken ct)
        {
            Task wait;
            lock (sync)
            {
                if (exclusive != null) return false;
                exclusive = lease;
                drained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (requests == 1) drained.SetResult(true);
                wait = drained.Task;
            }
            var timeout = Task.Delay(TimeSpan.FromSeconds(30), ct);
            return await Task.WhenAny(wait, timeout).ConfigureAwait(false) == wait && !ct.IsCancellationRequested;
        }

        private sealed class Lease
        {
            public Lease(BackupMaintenanceHandler owner) { Owner = owner; }
            public BackupMaintenanceHandler Owner { get; }
        }
    }
}
