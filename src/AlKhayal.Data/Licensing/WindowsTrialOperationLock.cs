using System;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using AlKhayal.Application.Licensing;

namespace AlKhayal.Data.Licensing
{
    public sealed class WindowsTrialOperationLock : ITrialOperationLock
    {
        public const string MutexName = @"Global\TheCashierTrialState";

        public IDisposable TryAcquire(TimeSpan timeout)
        {
            bool created;
            var mutex = new Mutex(false, MutexName, out created, Security());
            try
            {
                try
                {
                    if (!mutex.WaitOne(timeout)) { mutex.Dispose(); return null; }
                }
                catch (AbandonedMutexException) { /* ownership transfers to this process */ }
                return new Lease(mutex);
            }
            catch { mutex.Dispose(); throw; }
        }

        private static MutexSecurity Security()
        {
            var security = new MutexSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), MutexRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), MutexRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), MutexRights.Synchronize | MutexRights.Modify, AccessControlType.Allow));
            return security;
        }

        private sealed class Lease : IDisposable
        {
            private Mutex mutex;
            public Lease(Mutex mutex) { this.mutex = mutex; }
            public void Dispose()
            {
                var owned = Interlocked.Exchange(ref mutex, null);
                if (owned == null) return;
                try { owned.ReleaseMutex(); } finally { owned.Dispose(); }
            }
        }
    }
}
