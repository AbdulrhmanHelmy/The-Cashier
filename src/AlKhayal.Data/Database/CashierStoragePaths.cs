using System;
using System.IO;

namespace AlKhayal.Data.Database
{
    public static class CashierStoragePaths
    {
        public static string LocalRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TheCashier");
        public static string MachineRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TheCashier");
        public static string Database => Path.Combine(LocalRoot, "thecashier.db");
        public static string Backups => Path.Combine(LocalRoot, "Backups");
        public static string Logs => Path.Combine(LocalRoot, "Logs");
        public static string Cache => Path.Combine(LocalRoot, "Cache");
        public static string Temporary => Path.Combine(LocalRoot, "Temp");
        public static string Trial => Path.Combine(LocalRoot, "Licensing", "trial.dat");
        public static string TrialSecurity => Path.Combine(MachineRoot, "Security", "TrialState.bin");
        public static string License => Path.Combine(MachineRoot, "Licensing", "license.alklic");
    }
}
