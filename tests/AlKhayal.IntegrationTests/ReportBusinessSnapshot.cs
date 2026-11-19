using System;
using System.Data.SQLite;
using System.Linq;
using AlKhayal.Data.Database;
using Dapper;
using Newtonsoft.Json;

namespace AlKhayal.IntegrationTests
{
    internal static class ReportBusinessSnapshot
    {
        // Authenticated GETs append HTTP audit rows; neither those nor session rows are report data.
        internal static string Capture(string databasePath)
        {
            using (var c = new SQLiteConnection(Db.ConnectionStringFor(databasePath)))
            {
                c.Open();
                var tables = c.Query<string>("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT IN ('Sessions','OperationAuditLog') ORDER BY name").ToArray();
                return string.Join("\n", tables.Select(name => name + ":" + JsonConvert.SerializeObject(c.Query("SELECT * FROM \"" + name.Replace("\"", "\"\"") + "\"").ToArray())));
            }
        }
    }
}
