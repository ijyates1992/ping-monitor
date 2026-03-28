namespace PingMonitor.Web.Options;

public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    public string StoragePath { get; set; } = "App_Data/Backups";
    public long MaxUploadSizeBytes { get; set; } = 5 * 1024 * 1024;
    public BackupRetentionOptions Retention { get; set; } = new();
    public AutoBackupOptions AutoBackup { get; set; } = new();
}

public sealed class BackupRetentionOptions
{
    public bool Enabled { get; set; } = true;
    public int AutomaticBackupMaxCount { get; set; } = 30;
    public int? AutomaticBackupMaxAgeDays { get; set; }
}

public sealed class AutoBackupOptions
{
    public bool Enabled { get; set; } = true;
    public bool OnConfigChangeEnabled { get; set; } = true;
    public bool ScheduledEnabled { get; set; } = true;
    public string ScheduledTimeLocal { get; set; } = "02:00";
    public bool IncludeIdentityByDefault { get; set; }
    public int ConfigChangeCoalescingSeconds { get; set; } = 180;
}
