using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Backups
{
    public interface IBusinessBackups
    {
        Task<BackupStatus> StatusAsync();
        Task<BackupStatus> ConfigureAsync(BackupConfiguration configuration);
        Task<BackupFile> CreateAsync(string directory = null);
        Task<BackupManifest> InspectAsync(Stream archive, CancellationToken ct);
        Task<BackupFile> RestoreAsync(string confirmation, CancellationToken ct);
        Task<Stream> DownloadAsync(string name);
    }

    public sealed class BackupConfiguration
    {
        public string Directory { get; set; }
        public bool Enabled { get; set; }
    }

    public sealed class BackupStatus
    {
        public BackupConfiguration Configuration { get; set; }
        public BackupFile LastSuccess { get; set; }
        public DateTime? LastFailureUtc { get; set; }
        public string LastError { get; set; }
        public BackupFile[] Files { get; set; }
    }

    public sealed class BackupFile
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public DateTime CreatedUtc { get; set; }
        public long Size { get; set; }
    }

    public sealed class BackupManifest
    {
        public int FormatVersion { get; set; }
        public string ApplicationVersion { get; set; }
        public string SchemaVersion { get; set; }
        public DateTime CreatedUtc { get; set; }
        public Guid Id { get; set; }
        public string Component { get; set; }
        public long Size { get; set; }
        public string Sha256 { get; set; }
        public string Confirmation { get; set; }
    }

    public sealed class BackupException : Exception
    {
        public BackupException(string message) : base(message) { }
    }
}
