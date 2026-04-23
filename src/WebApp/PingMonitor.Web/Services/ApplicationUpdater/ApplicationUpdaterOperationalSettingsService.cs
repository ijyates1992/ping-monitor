using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.ApplicationUpdater;

internal sealed class ApplicationUpdaterOperationalSettingsService : IApplicationUpdaterOperationalSettingsService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly ApplicationUpdaterOptions _updaterOptions;
    private readonly AgentProvisioningOptions _provisioningOptions;

    public ApplicationUpdaterOperationalSettingsService(
        PingMonitorDbContext dbContext,
        IOptions<ApplicationUpdaterOptions> updaterOptions,
        IOptions<AgentProvisioningOptions> provisioningOptions)
    {
        _dbContext = dbContext;
        _updaterOptions = updaterOptions.Value;
        _provisioningOptions = provisioningOptions.Value;
    }

    public async Task<ApplicationUpdaterOperationalSettingsDto> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateEntityAsync(cancellationToken);
        return ToDto(settings);
    }

    public async Task<ApplicationUpdaterOperationalSettingsDto> UpdateAsync(
        UpdateApplicationUpdaterOperationalSettingsCommand command,
        CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateEntityAsync(cancellationToken);

        settings.EnableAutomaticUpdateChecks = command.EnableAutomaticUpdateChecks;
        settings.AutomaticUpdateCheckIntervalMinutes = ResolveAutomaticCheckInterval(command.AutomaticUpdateCheckIntervalMinutes);
        settings.AutomaticallyDownloadAndStageUpdates = command.AutomaticallyDownloadAndStageUpdates;
        settings.AllowDevBuildAutoStageWithoutVersionComparison = command.AllowDevBuildAutoStageWithoutVersionComparison;
        settings.AllowPreviewReleases = command.AllowPreviewReleases;
        settings.UpdaterOperationalSettingsInitializedAtUtc ??= DateTimeOffset.UtcNow;
        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(settings);
    }

    private async Task<ApplicationSettings> GetOrCreateEntityAsync(CancellationToken cancellationToken)
    {
        var settings = await _dbContext.ApplicationSettings
            .SingleOrDefaultAsync(x => x.ApplicationSettingsId == ApplicationSettings.SingletonId, cancellationToken);

        if (settings is null)
        {
            settings = new ApplicationSettings
            {
                ApplicationSettingsId = ApplicationSettings.SingletonId,
                SiteUrl = BuildInitialSiteUrl(),
                DefaultPingIntervalSeconds = 60,
                DefaultRetryIntervalSeconds = 5,
                DefaultTimeoutMs = 1000,
                DefaultFailureThreshold = 3,
                DefaultRecoveryThreshold = 2,
                EnableAutomaticUpdateChecks = _updaterOptions.EnableAutomaticUpdateChecks,
                AutomaticUpdateCheckIntervalMinutes = ResolveAutomaticCheckInterval(_updaterOptions.AutomaticUpdateCheckIntervalMinutes),
                AutomaticallyDownloadAndStageUpdates = _updaterOptions.AutomaticallyDownloadAndStageUpdates,
                AllowDevBuildAutoStageWithoutVersionComparison = _updaterOptions.AllowDevBuildAutoStageWithoutVersionComparison,
                AllowPreviewReleases = _updaterOptions.AllowPreviewReleases,
                UpdaterOperationalSettingsInitializedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            _dbContext.ApplicationSettings.Add(settings);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return settings;
        }

        if (settings.UpdaterOperationalSettingsInitializedAtUtc is null)
        {
            settings.EnableAutomaticUpdateChecks = _updaterOptions.EnableAutomaticUpdateChecks;
            settings.AutomaticUpdateCheckIntervalMinutes = ResolveAutomaticCheckInterval(_updaterOptions.AutomaticUpdateCheckIntervalMinutes);
            settings.AutomaticallyDownloadAndStageUpdates = _updaterOptions.AutomaticallyDownloadAndStageUpdates;
            settings.AllowDevBuildAutoStageWithoutVersionComparison = _updaterOptions.AllowDevBuildAutoStageWithoutVersionComparison;
            settings.AllowPreviewReleases = _updaterOptions.AllowPreviewReleases;
            settings.UpdaterOperationalSettingsInitializedAtUtc = DateTimeOffset.UtcNow;
            settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return settings;
    }


    private string BuildInitialSiteUrl()
    {
        var configuredUrl = _provisioningOptions.ServerUrl?.Trim();
        if (string.IsNullOrWhiteSpace(configuredUrl))
        {
            return string.Empty;
        }

        return Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri)
            ? uri.ToString().TrimEnd('/')
            : string.Empty;
    }

    private static ApplicationUpdaterOperationalSettingsDto ToDto(ApplicationSettings settings)
    {
        return new ApplicationUpdaterOperationalSettingsDto
        {
            EnableAutomaticUpdateChecks = settings.EnableAutomaticUpdateChecks,
            AutomaticUpdateCheckIntervalMinutes = ResolveAutomaticCheckInterval(settings.AutomaticUpdateCheckIntervalMinutes),
            AutomaticallyDownloadAndStageUpdates = settings.AutomaticallyDownloadAndStageUpdates,
            AllowDevBuildAutoStageWithoutVersionComparison = settings.AllowDevBuildAutoStageWithoutVersionComparison,
            AllowPreviewReleases = settings.AllowPreviewReleases,
            UpdatedAtUtc = settings.UpdatedAtUtc
        };
    }

    private static int ResolveAutomaticCheckInterval(int configuredIntervalMinutes)
    {
        return configuredIntervalMinutes < 1 ? 1 : configuredIntervalMinutes;
    }
}
