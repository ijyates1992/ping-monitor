using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.Backups;

public interface IConfigurationChangeBackupSignal
{
    void NotifyConfigurationChanged(string reason);
}

public sealed class ConfigurationAutoBackupBackgroundService : BackgroundService, IConfigurationChangeBackupSignal
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BackupOptions _options;
    private readonly ILogger<ConfigurationAutoBackupBackgroundService> _logger;

    private readonly object _sync = new();
    private DateTimeOffset? _lastConfigChangeSignalUtc;

    public ConfigurationAutoBackupBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<BackupOptions> options,
        ILogger<ConfigurationAutoBackupBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public void NotifyConfigurationChanged(string reason)
    {
        if (!_options.AutoBackup.Enabled || !_options.AutoBackup.OnConfigChangeEnabled)
        {
            return;
        }

        lock (_sync)
        {
            _lastConfigChangeSignalUtc = DateTimeOffset.UtcNow;
        }

        _logger.LogInformation("Configuration change signal queued for automatic backup. Reason: {Reason}.", reason);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Configuration auto-backup background service started.");
        var nextScheduledRunUtc = GetNextScheduledRunUtc(DateTimeOffset.Now);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_options.AutoBackup.Enabled && _options.AutoBackup.ScheduledEnabled && DateTimeOffset.Now >= nextScheduledRunUtc)
                {
                    await CreateAutomaticBackupAsync(ConfigurationBackupSources.AutomaticScheduled, "Automatic scheduled configuration backup", stoppingToken);
                    nextScheduledRunUtc = GetNextScheduledRunUtc(DateTimeOffset.Now.AddSeconds(1));
                }

                var pendingSignalUtc = GetPendingSignalUtc();
                if (_options.AutoBackup.Enabled && _options.AutoBackup.OnConfigChangeEnabled && pendingSignalUtc.HasValue)
                {
                    var coalesceFor = TimeSpan.FromSeconds(Math.Max(30, _options.AutoBackup.ConfigChangeCoalescingSeconds));
                    var dueAtUtc = pendingSignalUtc.Value + coalesceFor;
                    if (DateTimeOffset.UtcNow >= dueAtUtc)
                    {
                        ClearPendingSignal();
                        _logger.LogInformation("Configuration change backup coalescing window elapsed. Creating single backup for queued changes.");
                        await CreateAutomaticBackupAsync(ConfigurationBackupSources.AutomaticConfigChange, "Automatic backup created after configuration changes", stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automatic backup background loop failure.");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private async Task CreateAutomaticBackupAsync(string source, string notes, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var backupService = scope.ServiceProvider.GetRequiredService<IConfigurationBackupService>();
        var retentionService = scope.ServiceProvider.GetRequiredService<IConfigurationBackupRetentionService>();

        try
        {
            _logger.LogInformation("Automatic configuration backup started. Source: {Source}.", source);
            await backupService.CreateBackupAsync(
                new CreateConfigurationBackupRequest
                {
                    BackupName = source == ConfigurationBackupSources.AutomaticScheduled ? "auto-scheduled" : "auto-config-change",
                    Notes = notes,
                    BackupSource = source,
                    SelectedSections = GetDefaultSections()
                },
                cancellationToken);

            var prune = await retentionService.PruneAutomaticBackupsAsync(cancellationToken);
            _logger.LogInformation(
                "Automatic configuration backup completed. Source: {Source}. Retention prune requested={RequestedCount}, deleted={DeletedCount}, failed={FailedCount}.",
                source,
                prune.RequestedCount,
                prune.DeletedCount,
                prune.FailedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automatic configuration backup failed. Source: {Source}.", source);
        }
    }

    private IReadOnlyList<string> GetDefaultSections()
    {
        var sections = new List<string>
        {
            ConfigurationBackupSections.Agents,
            ConfigurationBackupSections.Endpoints,
            ConfigurationBackupSections.Groups,
            ConfigurationBackupSections.Dependencies,
            ConfigurationBackupSections.Assignments,
            ConfigurationBackupSections.SecuritySettings,
            ConfigurationBackupSections.NotificationSettings,
            ConfigurationBackupSections.UserNotificationSettings
        };

        if (_options.AutoBackup.IncludeIdentityByDefault)
        {
            sections.Add(ConfigurationBackupSections.Identity);
        }

        return sections;
    }

    private DateTimeOffset GetNextScheduledRunUtc(DateTimeOffset nowLocal)
    {
        var scheduledLocal = ParseScheduledTimeLocal(_options.AutoBackup.ScheduledTimeLocal);
        var nextLocal = new DateTimeOffset(nowLocal.Year, nowLocal.Month, nowLocal.Day, scheduledLocal.Hours, scheduledLocal.Minutes, 0, nowLocal.Offset);
        if (nextLocal <= nowLocal)
        {
            nextLocal = nextLocal.AddDays(1);
        }

        return nextLocal.ToUniversalTime();
    }

    private static TimeSpan ParseScheduledTimeLocal(string value)
    {
        if (TimeSpan.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return new TimeSpan(2, 0, 0);
    }

    private DateTimeOffset? GetPendingSignalUtc()
    {
        lock (_sync)
        {
            return _lastConfigChangeSignalUtc;
        }
    }

    private void ClearPendingSignal()
    {
        lock (_sync)
        {
            _lastConfigChangeSignalUtc = null;
        }
    }
}
