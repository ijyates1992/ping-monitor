namespace PingMonitor.Web.Options;

public sealed class DatabaseMaintenanceOptions
{
    public const string SectionName = "DatabaseMaintenance";

    public string BackupStoragePath { get; set; } = "App_Data/DbBackups";
    public string MySqlDumpExecutablePath { get; set; } = "mysqldump";
}
