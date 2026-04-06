using PingMonitor.Web.Services.StartupGate;
using PingMonitor.Web.Services.DatabaseStatus;

namespace PingMonitor.Web.ViewModels.StartupGate;

public sealed class StartupGatePageViewModel
{
    public required StartupGateStatus Status { get; init; }
    public required StartupDatabaseConfigurationForm DatabaseForm { get; init; }
    public required StartupAdminBootstrapForm AdminForm { get; init; }
    public required StartupDatabaseBackupUploadForm DatabaseBackupUploadForm { get; init; }
    public required StartupDatabaseBackupRestoreForm DatabaseBackupRestoreForm { get; init; }
    public required IReadOnlyList<DatabaseBackupFileSnapshot> DatabaseBackups { get; init; }
    public string? StatusMessage { get; init; }
    public string? ErrorMessage { get; init; }
}
