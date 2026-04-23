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
            var interval = ResolveInterval();
            if (!_options.UpdateChecksEnabled || !_options.EnableAutomaticUpdateChecks)
            {
                await Task.Delay(interval, stoppingToken);
                continue;
            }

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

            try
            {
                await RunAutomaticCheckAsync(stoppingToken);
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

    private async Task RunAutomaticCheckAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var detectionService = scope.ServiceProvider.GetRequiredService<IApplicationUpdateDetectionService>();
        var stagingService = scope.ServiceProvider.GetRequiredService<IApplicationUpdateStagingService>();
        var runtimeStateStore = scope.ServiceProvider.GetRequiredService<IApplicationUpdaterRuntimeStateStore>();
        var eventLogService = scope.ServiceProvider.GetRequiredService<IEventLogService>();

        var now = DateTimeOffset.UtcNow;
        var previousState = await runtimeStateStore.ReadAsync(cancellationToken);
        var nextState = Clone(previousState, lastAutomaticCheckAtUtc: now);

        var result = await detectionService.CheckForUpdatesAsync(_options.AllowPreviewReleases, cancellationToken);
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
        if (result.State == ApplicationUpdateCheckState.UpdateAvailable &&
            !string.IsNullOrWhiteSpace(latestTag) &&
            !string.Equals(previousState?.LastDetectedApplicableReleaseTag, latestTag, StringComparison.OrdinalIgnoreCase))
        {
            nextState = Clone(nextState,
                lastDetectedApplicableReleaseTag: latestTag,
                lastDetectedApplicableReleaseAtUtc: now);

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

            if (_options.AutomaticallyDownloadAndStageUpdates)
            {
                var alreadyAttempted = string.Equals(previousState?.LastAutoStageAttemptedReleaseTag, latestTag, StringComparison.OrdinalIgnoreCase);
                if (!alreadyAttempted)
                {
                    nextState = await RunAutoStageAsync(stagingService, eventLogService, nextState, latestTag, now, cancellationToken);
                }
            }
        }

        await runtimeStateStore.WriteAsync(nextState, cancellationToken);
    }

    private async Task<ApplicationUpdaterRuntimeState> RunAutoStageAsync(
        IApplicationUpdateStagingService stagingService,
        IEventLogService eventLogService,
        ApplicationUpdaterRuntimeState currentState,
        string latestTag,
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

        var stageResult = await stagingService.StageLatestApplicableReleaseAsync(_options.AllowPreviewReleases, cancellationToken);
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

    private TimeSpan ResolveInterval()
    {
        var configured = _options.AutomaticUpdateCheckIntervalMinutes;
        if (configured < 1)
        {
            configured = 1;
        }

        return TimeSpan.FromMinutes(configured);
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
        DateTimeOffset? lastAutoStagedAtUtc = null)
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
            LastUpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}
