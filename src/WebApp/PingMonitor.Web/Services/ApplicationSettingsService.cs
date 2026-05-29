using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services;

internal sealed class ApplicationSettingsService : IApplicationSettingsService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly AgentProvisioningOptions _provisioningOptions;
    private readonly ApplicationUpdaterOptions _updaterOptions;

    public ApplicationSettingsService(
        PingMonitorDbContext dbContext,
        IOptions<AgentProvisioningOptions> provisioningOptions,
        IOptions<ApplicationUpdaterOptions> updaterOptions)
    {
        _dbContext = dbContext;
        _provisioningOptions = provisioningOptions.Value;
        _updaterOptions = updaterOptions.Value;
    }

    public async Task<ApplicationSettingsDto> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateEntityAsync(cancellationToken);
        return ToDto(settings);
    }

    public async Task<ApplicationSettingsDto> UpdateAsync(UpdateApplicationSettingsCommand command, CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateEntityAsync(cancellationToken);

        settings.SiteUrl = command.SiteUrl.Trim();
        settings.DefaultPingIntervalSeconds = command.DefaultPingIntervalSeconds;
        settings.DefaultRetryIntervalSeconds = command.DefaultRetryIntervalSeconds;
        settings.DefaultTimeoutMs = command.DefaultTimeoutMs;
        settings.DefaultFailureThreshold = command.DefaultFailureThreshold;
        settings.DefaultRecoveryThreshold = command.DefaultRecoveryThreshold;
        settings.DegradedEvaluationEnabled = command.DegradedEvaluationEnabled;
        settings.DegradedBaselineLookbackMinutes = command.DegradedBaselineLookbackMinutes;
        settings.DegradedCurrentWindowMinutes = command.DegradedCurrentWindowMinutes;
        settings.DegradedPacketLossIncreasePercentagePoints = command.DegradedPacketLossIncreasePercentagePoints;
        settings.DegradedRttIncreasePercent = command.DegradedRttIncreasePercent;
        settings.DegradedJitterIncreasePercent = command.DegradedJitterIncreasePercent;
        settings.DegradedMinimumSamples = command.DegradedMinimumSamples;
        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(settings);
    }

    private async Task<ApplicationSettings> GetOrCreateEntityAsync(CancellationToken cancellationToken)
    {
        var settings = await _dbContext.ApplicationSettings
            .SingleOrDefaultAsync(x => x.ApplicationSettingsId == ApplicationSettings.SingletonId, cancellationToken);

        if (settings is not null)
        {
            return settings;
        }

        settings = new ApplicationSettings
        {
            ApplicationSettingsId = ApplicationSettings.SingletonId,
            SiteUrl = BuildInitialSiteUrl(),
            DefaultPingIntervalSeconds = 60,
            DefaultRetryIntervalSeconds = 5,
            DefaultTimeoutMs = 1000,
            DefaultFailureThreshold = 3,
            DefaultRecoveryThreshold = 2,
            DegradedEvaluationEnabled = true,
            DegradedBaselineLookbackMinutes = 1440,
            DegradedCurrentWindowMinutes = 60,
            DegradedPacketLossIncreasePercentagePoints = 20d,
            DegradedRttIncreasePercent = 20d,
            DegradedJitterIncreasePercent = 20d,
            DegradedMinimumSamples = 10,
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

    private static ApplicationSettingsDto ToDto(ApplicationSettings settings)
    {
        return new ApplicationSettingsDto
        {
            SiteUrl = settings.SiteUrl,
            DefaultPingIntervalSeconds = settings.DefaultPingIntervalSeconds,
            DefaultRetryIntervalSeconds = settings.DefaultRetryIntervalSeconds,
            DefaultTimeoutMs = settings.DefaultTimeoutMs,
            DefaultFailureThreshold = settings.DefaultFailureThreshold,
            DefaultRecoveryThreshold = settings.DefaultRecoveryThreshold,
            DegradedEvaluationEnabled = settings.DegradedEvaluationEnabled,
            DegradedBaselineLookbackMinutes = settings.DegradedBaselineLookbackMinutes,
            DegradedCurrentWindowMinutes = settings.DegradedCurrentWindowMinutes,
            DegradedPacketLossIncreasePercentagePoints = settings.DegradedPacketLossIncreasePercentagePoints,
            DegradedRttIncreasePercent = settings.DegradedRttIncreasePercent,
            DegradedJitterIncreasePercent = settings.DegradedJitterIncreasePercent,
            DegradedMinimumSamples = settings.DegradedMinimumSamples,
            UpdatedAtUtc = settings.UpdatedAtUtc
        };
    }

    private static int ResolveAutomaticCheckInterval(int configuredIntervalMinutes)
    {
        return configuredIntervalMinutes < 1 ? 1 : configuredIntervalMinutes;
    }
}
