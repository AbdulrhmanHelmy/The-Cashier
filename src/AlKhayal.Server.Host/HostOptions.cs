using System; using System.IO;
namespace AlKhayal.Server.Host
{
    public sealed class HostOptions
    {
        public HostOptions()
        {
            Url = Read("THECASHIER_SERVER_URL", "http://127.0.0.1:5138");
            DatabasePath = Read("THECASHIER_DATABASE_PATH", AlKhayal.Data.Database.Db.DefaultDatabasePath());
            var packagedFrontendPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "frontend");
            var developmentFrontendPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "frontend"));
            var defaultFrontendPath = Directory.Exists(packagedFrontendPath) ? packagedFrontendPath : developmentFrontendPath;
            FrontendPath = Read("THECASHIER_FRONTEND_PATH", defaultFrontendPath);
        }
        public string Url { get; set; }
        public string DatabasePath { get; set; }
        public string FrontendPath { get; set; }
        public bool IsHttps { get { return Url.StartsWith("https://",StringComparison.OrdinalIgnoreCase); } }
        private static string Read(string name,string fallback) { var value=Environment.GetEnvironmentVariable(name); return string.IsNullOrWhiteSpace(value)?fallback:value; }
    }
}
