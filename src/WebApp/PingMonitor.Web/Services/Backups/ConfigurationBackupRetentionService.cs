using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.Backups;

public interface IConfigurationBackupRetentionService
{
    Task<BulkDeleteConfigurationBackupsResponse> PruneAutomaticBackupsAsync(CancellationToken cancellationToken);
}

public sealed class ConfigurationBackupRetentionService : IConfigurationBackupRetentionService
{
    private readonly IConfigurationBackupQueryService _backupQueryService;
    private readonly IConfigurationBackupManagementService _backupManagementService;
    private readonly BackupOptions _options;
    private readonly ILogger<ConfigurationBackupRetentionService> _logger;

    public ConfigurationBackupRetentionService(
        IConfigurationBackupQueryService backupQueryService,
        IConfigurationBackupManagementService backupManagementService,
        IOptions<BackupOptions> options,
        ILogger<ConfigurationBackupRetentionService> logger)
    {
        _backupQueryService = backupQueryService;
        _backupManagementService = backupManagementService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<BulkDeleteConfigurationBackupsResponse> PruneAutomaticBackupsAsync(CancellationToken cancellationToken)
    {
        if (!_options.Retention.Enabled || _options.Retention.AutomaticBackupMaxCount <= 0)
        {
            return new BulkDeleteConfigurationBackupsResponse { Messages = ["Automatic backup retention is disabled."] };
        }

        var allBackups = await _backupQueryService.ListBackupsAsync(cancellationToken);
        var automaticBackups = allBackups
            .Where(x => ConfigurationBackupSources.IsAutomatic(x.BackupSource))
            .OrderByDescending(x => x.ExportedAtUtc ?? x.FileCreatedAtUtc)
            .ToList();

        var fileIdsToDelete = new List<string>();
        if (automaticBackups.Count > _options.Retention.AutomaticBackupMaxCount)
        {
            fileIdsToDelete.AddRange(automaticBackups
                .Skip(_options.Retention.AutomaticBackupMaxCount)
                .Select(x => x.FileId));
        }

        if (_options.Retention.AutomaticBackupMaxAgeDays.HasValue && _options.Retention.AutomaticBackupMaxAgeDays.Value > 0)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.Retention.AutomaticBackupMaxAgeDays.Value);
            fileIdsToDelete.AddRange(automaticBackups
                .Where(x => (x.ExportedAtUtc ?? x.FileCreatedAtUtc) < cutoff)
                .Select(x => x.FileId));
        }

        var distinctFileIds = fileIdsToDelete.Distinct(StringComparer.Ordinal).ToArray();
        if (distinctFileIds.Length == 0)
        {
            return new BulkDeleteConfigurationBackupsResponse { RequestedCount = 0, Messages = ["No automatic backups required pruning."] };
        }

        _logger.LogInformation("Automatic backup retention pruning started. Files selected: {Count}.", distinctFileIds.Length);
        return await _backupManagementService.BulkDeleteAsync(
            new BulkDeleteConfigurationBackupsRequest
            {
                FileIds = distinctFileIds,
                ConfirmationText = BackupDeleteModes.BulkConfirmationText
            },
            cancellationToken);
    }
}
