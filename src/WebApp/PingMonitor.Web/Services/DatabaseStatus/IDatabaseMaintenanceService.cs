namespace PingMonitor.Web.Services.DatabaseStatus;

public interface IDatabaseMaintenanceService
{
    Task<DatabasePrunePreviewResult> PreviewPruneAsync(DatabasePrunePreviewRequest request, CancellationToken cancellationToken);
    Task<DatabasePruneExecuteResult> ExecutePruneAsync(DatabasePruneExecuteRequest request, CancellationToken cancellationToken);
    Task<DatabaseBackupCreateResult> CreateBackupAsync(DatabaseBackupCreateRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<DatabaseBackupFileSnapshot>> ListBackupsAsync(CancellationToken cancellationToken);
    string ResolveBackupDownloadPath(string fileId);
}
