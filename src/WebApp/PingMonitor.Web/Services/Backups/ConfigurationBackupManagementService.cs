namespace PingMonitor.Web.Services.Backups;

public interface IConfigurationBackupManagementService
{
    Task<DeleteConfigurationBackupResponse> DeleteAsync(DeleteConfigurationBackupRequest request, CancellationToken cancellationToken);
    Task<BulkDeleteConfigurationBackupsResponse> BulkDeleteAsync(BulkDeleteConfigurationBackupsRequest request, CancellationToken cancellationToken);
}

public sealed class ConfigurationBackupManagementService : IConfigurationBackupManagementService
{
    private readonly IConfigurationBackupDocumentLoader _documentLoader;
    private readonly IConfigurationBackupCatalogService _catalogService;
    private readonly ILogger<ConfigurationBackupManagementService> _logger;

    public ConfigurationBackupManagementService(
        IConfigurationBackupDocumentLoader documentLoader,
        IConfigurationBackupCatalogService catalogService,
        ILogger<ConfigurationBackupManagementService> logger)
    {
        _documentLoader = documentLoader;
        _catalogService = catalogService;
        _logger = logger;
    }

    public async Task<DeleteConfigurationBackupResponse> DeleteAsync(DeleteConfigurationBackupRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.ConfirmationText?.Trim(), BackupDeleteModes.SingleConfirmationText, StringComparison.Ordinal))
        {
            return new DeleteConfigurationBackupResponse
            {
                FileId = request.FileId,
                Deleted = false,
                Message = $"Single delete requires typed confirmation '{BackupDeleteModes.SingleConfirmationText}'."
            };
        }

        try
        {
            var fullPath = _documentLoader.ResolveBackupPath(request.FileId);
            File.Delete(fullPath);
            await _catalogService.RemoveAsync(request.FileId, cancellationToken);
            _logger.LogInformation("Deleted configuration backup file {FileId}.", request.FileId);
            return new DeleteConfigurationBackupResponse { FileId = request.FileId, Deleted = true, Message = "Backup deleted." };
        }
        catch (FileNotFoundException)
        {
            _logger.LogWarning("Delete request for backup file {FileId} could not be completed because file was not found.", request.FileId);
            return new DeleteConfigurationBackupResponse { FileId = request.FileId, Deleted = false, Message = "Backup file was not found." };
        }
    }

    public async Task<BulkDeleteConfigurationBackupsResponse> BulkDeleteAsync(BulkDeleteConfigurationBackupsRequest request, CancellationToken cancellationToken)
    {
        var fileIds = request.FileIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (fileIds.Length == 0)
        {
            return new BulkDeleteConfigurationBackupsResponse { RequestedCount = 0, Messages = ["Select at least one backup file for bulk delete."] };
        }

        if (!string.Equals(request.ConfirmationText?.Trim(), BackupDeleteModes.BulkConfirmationText, StringComparison.Ordinal))
        {
            return new BulkDeleteConfigurationBackupsResponse
            {
                RequestedCount = fileIds.Length,
                FailedCount = fileIds.Length,
                Messages = [$"Bulk delete requires typed confirmation '{BackupDeleteModes.BulkConfirmationText}'."]
            };
        }

        var messages = new List<string>();
        var deleted = 0;
        var failed = 0;

        foreach (var fileId in fileIds)
        {
            var result = await DeleteAsync(new DeleteConfigurationBackupRequest { FileId = fileId, ConfirmationText = BackupDeleteModes.SingleConfirmationText }, cancellationToken);
            if (result.Deleted)
            {
                deleted++;
            }
            else
            {
                failed++;
            }

            messages.Add($"{fileId}: {result.Message}");
        }

        _logger.LogInformation(
            "Bulk delete completed for configuration backups. Requested: {RequestedCount}, Deleted: {DeletedCount}, Failed: {FailedCount}.",
            fileIds.Length,
            deleted,
            failed);

        return new BulkDeleteConfigurationBackupsResponse
        {
            RequestedCount = fileIds.Length,
            DeletedCount = deleted,
            FailedCount = failed,
            Messages = messages
        };
    }
}
