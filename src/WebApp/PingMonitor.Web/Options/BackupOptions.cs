namespace PingMonitor.Web.Options;

public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    public string StoragePath { get; set; } = "App_Data/Backups";
    public long MaxUploadSizeBytes { get; set; } = 5 * 1024 * 1024;
}
