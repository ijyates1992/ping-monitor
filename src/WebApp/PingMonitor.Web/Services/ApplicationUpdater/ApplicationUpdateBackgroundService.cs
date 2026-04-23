using System.Text.Json;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Models;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.EventLogs;
using PingMonitor.Web.Services.StartupGate;

namespace PingMonitor.Web.Services.ApplicationUpdater;

internal sealed class ApplicationUpdateBackgroundService : BackgroundService
{
    private static readonly JsonSerializerOptions DetailsJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan StartupGatePauseDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IStartupGateRuntimeState _startupGateRuntimeState;
    private readonly ApplicationUpdaterOptions _options;
    private readonly ILogger<ApplicationUpdateBackgroundService> _logger;

    internal enum AutoStagePlan
    {
        Skip = 0,
        StageReleaseBuild = 1,
        StageDevBuildWithOverride = 2,
        SuppressDevBuildWithoutOverride = 3
    }

    public ApplicationUpdateBackgroundService(
        IServiceScopeFactory scopeFactory,
        IStartupGateRuntimeState startupGateRuntimeState,
        IOptions<ApplicationUpdaterOptions> options,
        ILogger<ApplicationUpdateBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _startupGateRuntimeState = startupGateRuntimeState;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var wasPausedByStartupGate = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_startupGateRuntimeState.IsOperationalMode)
            {
                if (!wasPausedByStartupGate)
                {
                    _logger.LogInformation("Automatic updater checks are paused because Startup Gate is active.");
                    wasPausedByStartupGate = true;
                }

                await Task.Delay(StartupGatePauseDelay, stoppingToken);
                continue;
            }

            if (wasPausedByStartupGate)
            {
                _logger.LogInformation("Startup Gate is cleared. Automatic updater checks are resuming.");
                wasPausedByStartupGate = false;
            }

            var interval = ResolveInterval(_options.AutomaticUpdateCheckIntervalMinutes);

            try
            {
                var operationalSettings = await ReadOperationalSettingsAsync(stoppingToken);
                interval = ResolveInterval(operationalSettings.AutomaticUpdateCheckIntervalMinutes);

                if (!_options.UpdateChecksEnabled || !operationalSettings.EnableAutomaticUpdateChecks)
                {
                    await Task.Delay(interval, stoppingToken);
                    continue;
                }

                await RunAutomaticCheckAsync(operationalSettings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automatic updater check iteration failed unexpectedly.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task RunAutomaticCheckAsync(
        ApplicationUpdaterOperationalSettingsDto operationalSettings,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var detectionService = scope.ServiceProvider.GetRequiredService<IApplicationUpdateDetectionService>();
        var stagingService = scope.ServiceProvider.GetRequiredService<IApplicationUpdateStagingService>();
        var runtimeStateStore = scope.ServiceProvider.GetRequiredService<IApplicationUpdaterRuntimeStateStore>();
        var eventLogService = scope.ServiceProvider.GetRequiredService<IEventLogService>();

        var now = DateTimeOffset.UtcNow;
        var previousState = await runtimeStateStore.ReadAsync(cancellationToken);
        var nextState = Clone(previousState, lastAutomaticCheckAtUtc: now);

        var result = await detectionService.CheckForUpdatesAsync(operationalSettings.AllowPreviewReleases, cancellationToken);
        if (result.State == ApplicationUpdateCheckState.CheckFailed)
        {
            var shouldLogFailureTransition = previousState?.LastAutomaticCheckSucceededAtUtc is not null;
            nextState = Clone(nextState,
                lastAutomaticCheckFailedAtUtc: now,
                lastAutomaticCheckFailureMessage: result.Message);

            if (shouldLogFailureTransition)
            {
                await eventLogService.WriteAsync(new EventLogWriteRequest
                {
                    Category = EventCategory.System,
                    EventType = EventType.UpdaterAutomaticCheckFailed,
                    Severity = EventSeverity.Warning,
                    Message = $"Automatic updater check failed: {result.Message}",
                    DetailsJson = JsonSerializer.Serialize(new { result.State, result.Message }, DetailsJsonOptions)
                }, cancellationToken);
            }

            await runtimeStateStore.WriteAsync(nextState, cancellationToken);
            return;
        }

        nextState = Clone(nextState,
            lastAutomaticCheckSucceededAtUtc: now,
            lastAutomaticCheckFailureMessage: null);

        var latestTag = result.LatestApplicableRelease?.TagName;
        var releaseFound = !string.IsNullOrWhiteSpace(latestTag) && result.ReleaseDiscoverySucceeded;
        var releaseChanged = releaseFound &&
            !string.Equals(previousState?.LastDetectedApplicableReleaseTag, latestTag, StringComparison.OrdinalIgnoreCase);

        if (releaseChanged)
        {
            nextState = Clone(nextState,
                lastDetectedApplicableReleaseTag: latestTag,
                lastDetectedApplicableReleaseAtUtc: now);

            if (result.State == ApplicationUpdateCheckState.UpdateAvailable)
            {
                await eventLogService.WriteAsync(new EventLogWriteRequest
                {
                    Category = EventCategory.System,
                    EventType = EventType.UpdaterUpdateAvailableDetected,
                    Severity = EventSeverity.Info,
                    Message = $"A new applicable application update was detected: {latestTag}.",
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        latestTag,
                        result.AllowPreviewReleases,
                        releaseUrl = result.LatestApplicableRelease?.HtmlUrl
                    }, DetailsJsonOptions)
                }, cancellationToken);
            }
            else if (result.State == ApplicationUpdateCheckState.DevBuildComparisonSkipped)
            {
                nextState = Clone(nextState,
                    lastDevComparisonSkippedReleaseTag: latestTag,
                    lastDevComparisonSkippedAtUtc: now);

                await eventLogService.WriteAsync(new EventLogWriteRequest
                {
                    Category = EventCategory.System,
                    EventType = EventType.UpdaterDevBuildComparisonSkipped,
                    Severity = EventSeverity.Warning,
                    Message = $"Latest applicable release {latestTag} was detected, but this instance is running a DEV build, so semantic comparison was skipped.",
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        latestTag,
                        operationalSettings.AllowPreviewReleases,
                        devBuild = true,
                        semanticComparisonSkipped = true
                    }, DetailsJsonOptions)
                }, cancellationToken);
            }
        }

        var autoStagePlan = DetermineAutoStagePlan(
            result.State,
            releaseFound,
            operationalSettings.AutomaticallyDownloadAndStageUpdates,
            operationalSettings.AllowDevBuildAutoStageWithoutVersionComparison,
            previousState?.LastAutoStageAttemptedReleaseTag,
            latestTag);

        if (releaseFound && autoStagePlan != AutoStagePlan.Skip)
        {
            if (autoStagePlan == AutoStagePlan.StageReleaseBuild)
            {
                nextState = await RunAutoStageAsync(
                    stagingService,
                    eventLogService,
                    nextState,
                    latestTag!,
                    operationalSettings.AllowPreviewReleases,
                    now,
                    cancellationToken);
            }
            else if (autoStagePlan == AutoStagePlan.StageDevBuildWithOverride)
            {
                nextState = Clone(nextState,
                    lastDevAutoStageOverrideAllowedReleaseTag: latestTag,
                    lastDevAutoStageOverrideAllowedAtUtc: now);

                await eventLogService.WriteAsync(new EventLogWriteRequest
                {
                    Category = EventCategory.System,
                    EventType = EventType.UpdaterDevBuildAutoStageOverrideAllowed,
                    Severity = EventSeverity.Warning,
                    Message = $"DEV-build override enabled: automatic staging is proceeding for {latestTag} without semantic version comparison.",
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        latestTag,
                        devBuild = true,
                        overrideEnabled = true,
                        semanticComparisonSkipped = true
                    }, DetailsJsonOptions)
                }, cancellationToken);

                nextState = await RunAutoStageAsync(
                    stagingService,
                    eventLogService,
                    nextState,
                    latestTag!,
                    operationalSettings.AllowPreviewReleases,
                    now,
                    cancellationToken);
            }
            else if (autoStagePlan == AutoStagePlan.SuppressDevBuildWithoutOverride)
            {
                nextState = Clone(nextState,
                    lastDevAutoStageSuppressedReleaseTag: latestTag,
                    lastDevAutoStageSuppressedAtUtc: now,
                    lastAutoStageFailureMessage: "Automatic staging was skipped because this instance is running a DEV build and DEV override is disabled.");

                await eventLogService.WriteAsync(new EventLogWriteRequest
                {
                    Category = EventCategory.System,
                    EventType = EventType.UpdaterDevBuildAutoStageSkipped,
                    Severity = EventSeverity.Info,
                    Message = $"Automatic staging was skipped for {latestTag} because this instance is running a DEV build and the DEV override is disabled.",
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        latestTag,
                        devBuild = true,
                        overrideEnabled = false,
                        semanticComparisonSkipped = true
                    }, DetailsJsonOptions)
                }, cancellationToken);
            }
        }

        await runtimeStateStore.WriteAsync(nextState, cancellationToken);
    }

    private async Task<ApplicationUpdaterRuntimeState> RunAutoStageAsync(
        IApplicationUpdateStagingService stagingService,
        IEventLogService eventLogService,
        ApplicationUpdaterRuntimeState currentState,
        string latestTag,
        bool allowPreviewReleases,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var startedState = Clone(currentState,
            lastAutoStageAttemptedReleaseTag: latestTag,
            lastAutoStageAttemptedAtUtc: now,
            lastAutoStageFailureMessage: null);

        await eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.System,
            EventType = EventType.UpdaterAutoStageStarted,
            Severity = EventSeverity.Info,
            Message = $"Automatic staging started for release {latestTag}.",
            DetailsJson = JsonSerializer.Serialize(new { latestTag }, DetailsJsonOptions)
        }, cancellationToken);

        var stageResult = await stagingService.StageLatestApplicableReleaseAsync(allowPreviewReleases, cancellationToken);
        if (stageResult.State.Status == ApplicationUpdateStagingStatus.Ready)
        {
            await eventLogService.WriteAsync(new EventLogWriteRequest
            {
                Category = EventCategory.System,
                EventType = EventType.UpdaterAutoStageSucceeded,
                Severity = EventSeverity.Info,
                Message = $"Automatic staging completed for release {latestTag}.",
                DetailsJson = JsonSerializer.Serialize(new
                {
                    latestTag,
                    noOp = stageResult.State.StageOperationWasNoOp,
                    stagedZipPath = stageResult.State.StagedZipPath
                }, DetailsJsonOptions)
            }, cancellationToken);

            return Clone(startedState,
                lastAutoStagedReleaseTag: latestTag,
                lastAutoStagedAtUtc: DateTimeOffset.UtcNow,
                lastAutoStageFailureMessage: null);
        }

        await eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.System,
            EventType = EventType.UpdaterAutoStageFailed,
            Severity = EventSeverity.Warning,
            Message = $"Automatic staging failed for release {latestTag}: {stageResult.State.StageOperationMessage ?? stageResult.State.FailureMessage ?? "Unknown failure"}",
            DetailsJson = JsonSerializer.Serialize(new
            {
                latestTag,
                stageStatus = stageResult.State.Status.ToString(),
                stageResult.State.StageOperationMessage,
                stageResult.State.FailureMessage
            }, DetailsJsonOptions)
        }, cancellationToken);

        return Clone(startedState,
            lastAutoStageFailureMessage: stageResult.State.StageOperationMessage ?? stageResult.State.FailureMessage ?? "Automatic staging failed.");
    }

    private async Task<ApplicationUpdaterOperationalSettingsDto> ReadOperationalSettingsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<IApplicationUpdaterOperationalSettingsService>();
        return await settingsService.GetCurrentAsync(cancellationToken);
    }

    private static TimeSpan ResolveInterval(int configuredIntervalMinutes)
    {
        var configured = configuredIntervalMinutes;
        if (configured < 1)
        {
            configured = 1;
        }

        return TimeSpan.FromMinutes(configured);
    }

    internal static AutoStagePlan DetermineAutoStagePlan(
        ApplicationUpdateCheckState state,
        bool releaseFound,
        bool automaticStageEnabled,
        bool allowDevBuildAutoStageWithoutVersionComparison,
        string? previousAttemptedTag,
        string? latestTag)
    {
        if (!automaticStageEnabled || !releaseFound || string.IsNullOrWhiteSpace(latestTag))
        {
            return AutoStagePlan.Skip;
        }

        var alreadyAttempted = string.Equals(previousAttemptedTag, latestTag, StringComparison.OrdinalIgnoreCase);
        if (alreadyAttempted)
        {
            return AutoStagePlan.Skip;
        }

        if (state == ApplicationUpdateCheckState.UpdateAvailable)
        {
            return AutoStagePlan.StageReleaseBuild;
        }

        if (state == ApplicationUpdateCheckState.DevBuildComparisonSkipped)
        {
            return allowDevBuildAutoStageWithoutVersionComparison
                ? AutoStagePlan.StageDevBuildWithOverride
                : AutoStagePlan.SuppressDevBuildWithoutOverride;
        }

        return AutoStagePlan.Skip;
    }

    private static ApplicationUpdaterRuntimeState Clone(
        ApplicationUpdaterRuntimeState? state,
        DateTimeOffset? lastAutomaticCheckAtUtc = null,
        DateTimeOffset? lastAutomaticCheckSucceededAtUtc = null,
        DateTimeOffset? lastAutomaticCheckFailedAtUtc = null,
        string? lastAutomaticCheckFailureMessage = null,
        string? lastDetectedApplicableReleaseTag = null,
        DateTimeOffset? lastDetectedApplicableReleaseAtUtc = null,
        string? lastAutoStageAttemptedReleaseTag = null,
        DateTimeOffset? lastAutoStageAttemptedAtUtc = null,
        string? lastAutoStageFailureMessage = null,
        string? lastAutoStagedReleaseTag = null,
        DateTimeOffset? lastAutoStagedAtUtc = null,
        string? lastDevComparisonSkippedReleaseTag = null,
        DateTimeOffset? lastDevComparisonSkippedAtUtc = null,
        string? lastDevAutoStageSuppressedReleaseTag = null,
        DateTimeOffset? lastDevAutoStageSuppressedAtUtc = null,
        string? lastDevAutoStageOverrideAllowedReleaseTag = null,
        DateTimeOffset? lastDevAutoStageOverrideAllowedAtUtc = null)
    {
        return new ApplicationUpdaterRuntimeState
        {
            LastAutomaticCheckAtUtc = lastAutomaticCheckAtUtc ?? state?.LastAutomaticCheckAtUtc,
            LastAutomaticCheckSucceededAtUtc = lastAutomaticCheckSucceededAtUtc ?? state?.LastAutomaticCheckSucceededAtUtc,
            LastAutomaticCheckFailedAtUtc = lastAutomaticCheckFailedAtUtc ?? state?.LastAutomaticCheckFailedAtUtc,
            LastAutomaticCheckFailureMessage = lastAutomaticCheckFailureMessage ?? state?.LastAutomaticCheckFailureMessage,
            LastDetectedApplicableReleaseTag = lastDetectedApplicableReleaseTag ?? state?.LastDetectedApplicableReleaseTag,
            LastDetectedApplicableReleaseAtUtc = lastDetectedApplicableReleaseAtUtc ?? state?.LastDetectedApplicableReleaseAtUtc,
            LastAutoStageAttemptedReleaseTag = lastAutoStageAttemptedReleaseTag ?? state?.LastAutoStageAttemptedReleaseTag,
            LastAutoStageAttemptedAtUtc = lastAutoStageAttemptedAtUtc ?? state?.LastAutoStageAttemptedAtUtc,
            LastAutoStageFailureMessage = lastAutoStageFailureMessage ?? state?.LastAutoStageFailureMessage,
            LastAutoStagedReleaseTag = lastAutoStagedReleaseTag ?? state?.LastAutoStagedReleaseTag,
            LastAutoStagedAtUtc = lastAutoStagedAtUtc ?? state?.LastAutoStagedAtUtc,
            LastDevComparisonSkippedReleaseTag = lastDevComparisonSkippedReleaseTag ?? state?.LastDevComparisonSkippedReleaseTag,
            LastDevComparisonSkippedAtUtc = lastDevComparisonSkippedAtUtc ?? state?.LastDevComparisonSkippedAtUtc,
            LastDevAutoStageSuppressedReleaseTag = lastDevAutoStageSuppressedReleaseTag ?? state?.LastDevAutoStageSuppressedReleaseTag,
            LastDevAutoStageSuppressedAtUtc = lastDevAutoStageSuppressedAtUtc ?? state?.LastDevAutoStageSuppressedAtUtc,
            LastDevAutoStageOverrideAllowedReleaseTag = lastDevAutoStageOverrideAllowedReleaseTag ?? state?.LastDevAutoStageOverrideAllowedReleaseTag,
            LastDevAutoStageOverrideAllowedAtUtc = lastDevAutoStageOverrideAllowedAtUtc ?? state?.LastDevAutoStageOverrideAllowedAtUtc,
            LastUpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}
