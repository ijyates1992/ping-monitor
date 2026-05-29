using PingMonitor.Web.Services.StartupGate;

namespace PingMonitor.Web.Services.Metrics;

internal sealed class RollingWindowHydrationBackgroundService : BackgroundService
{
    private static readonly TimeSpan StartupGatePollDelay = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRollingWindowHydrationState _hydrationState;
    private readonly IStartupGateRuntimeState _startupGateRuntimeState;
    private readonly ILogger<RollingWindowHydrationBackgroundService> _logger;

    public RollingWindowHydrationBackgroundService(
        IServiceScopeFactory scopeFactory,
        IRollingWindowHydrationState hydrationState,
        IStartupGateRuntimeState startupGateRuntimeState,
        ILogger<RollingWindowHydrationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _hydrationState = hydrationState;
        _startupGateRuntimeState = startupGateRuntimeState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var wasBlockedByStartupGate = false;

        while (!_startupGateRuntimeState.IsOperationalMode)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (!wasBlockedByStartupGate)
            {
                _logger.LogInformation("Rolling window hydration is paused because Startup Gate is active.");
                wasBlockedByStartupGate = true;
            }

            await Task.Delay(StartupGatePollDelay, stoppingToken);
        }

        if (wasBlockedByStartupGate)
        {
            _logger.LogInformation("Startup Gate is cleared. Rolling window hydration is starting.");
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        _hydrationState.MarkRunning(startedAtUtc);
        _logger.LogInformation("Rolling window hydration started at {StartedAtUtc}.", startedAtUtc);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var metricsService = scope.ServiceProvider.GetRequiredService<IAssignmentMetrics24hService>();
            await metricsService.RebuildAllAsync(stoppingToken);

            var completedAtUtc = DateTimeOffset.UtcNow;
            _hydrationState.MarkComplete(completedAtUtc);
            _logger.LogInformation("Rolling window hydration completed at {CompletedAtUtc}.", completedAtUtc);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown should not mark hydration failed; the next process start will hydrate again.
        }
        catch (Exception ex)
        {
            var failedAtUtc = DateTimeOffset.UtcNow;
            _hydrationState.MarkFailed(failedAtUtc, ex.Message);
            _logger.LogError(ex, "Rolling window hydration failed at {FailedAtUtc}. Agent result ingestion will remain blocked until the application is restarted and hydration completes.", failedAtUtc);
        }
    }
}
