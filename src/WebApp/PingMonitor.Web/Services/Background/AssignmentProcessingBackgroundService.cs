using PingMonitor.Web.Services.State;
using PingMonitor.Web.Services.Diagnostics;
using PingMonitor.Web.Services.StartupGate;

namespace PingMonitor.Web.Services.Background;

internal sealed class AssignmentProcessingBackgroundService : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(2);
    private const int MaxBatchSize = 250;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAssignmentProcessingQueue _queue;
    private readonly IStartupGateRuntimeState _startupGateRuntimeState;
    private readonly ILogger<AssignmentProcessingBackgroundService> _logger;

    public AssignmentProcessingBackgroundService(
        IServiceScopeFactory scopeFactory,
        IAssignmentProcessingQueue queue,
        IStartupGateRuntimeState startupGateRuntimeState,
        ILogger<AssignmentProcessingBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _startupGateRuntimeState = startupGateRuntimeState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var wasBlockedByStartupGate = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_startupGateRuntimeState.IsOperationalMode)
            {
                if (!wasBlockedByStartupGate)
                {
                    _logger.LogInformation("Assignment processing is paused because Startup Gate is active.");
                    wasBlockedByStartupGate = true;
                }

                await Task.Delay(IdleDelay, stoppingToken);
                continue;
            }

            if (wasBlockedByStartupGate)
            {
                _logger.LogInformation("Startup Gate is cleared. Assignment processing is resuming normal operation.");
                wasBlockedByStartupGate = false;
            }

            await _queue.WaitForSignalAsync(IdleDelay, stoppingToken);
            await ProcessAvailableAssignmentsAsync(stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_startupGateRuntimeState.IsOperationalMode)
        {
            _logger.LogInformation("Startup Gate is active during shutdown. Skipping assignment processing queue drain.");
            await base.StopAsync(cancellationToken);
            return;
        }

        _logger.LogInformation("Application shutdown requested. Attempting best-effort assignment processing queue drain.");
        try
        {
            await ProcessAvailableAssignmentsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Best-effort assignment processing queue drain failed during shutdown.");
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task ProcessAvailableAssignmentsAsync(CancellationToken cancellationToken)
    {
        while (_queue.HasPendingItems())
        {
            var assignmentIds = _queue.DequeueBatch(MaxBatchSize);
            if (assignmentIds.Count == 0)
            {
                break;
            }

            using var scope = _scopeFactory.CreateScope();
            var stateEvaluationService = scope.ServiceProvider.GetRequiredService<IStateEvaluationService>();
            var dbActivityScope = scope.ServiceProvider.GetRequiredService<IDbActivityScope>();
            using var dbScope = dbActivityScope.BeginScope("AssignmentProcessing");

            var failedAssignments = new List<string>();
            foreach (var assignmentId in assignmentIds)
            {
                try
                {
                    await stateEvaluationService.EvaluateAssignmentStateAsync(assignmentId, cancellationToken);
                }
                catch (Exception ex)
                {
                    failedAssignments.Add(assignmentId);
                    _queue.RecordFailure(ex, DateTimeOffset.UtcNow);
                    _logger.LogError(ex, "Assignment processing failed for assignment {AssignmentId}. Assignment will be retried.", assignmentId);
                }
            }

            var processedCount = assignmentIds.Count - failedAssignments.Count;
            if (processedCount > 0)
            {
                _queue.RecordProcessedCount(processedCount, DateTimeOffset.UtcNow);
            }

            if (failedAssignments.Count > 0)
            {
                _queue.EnqueueAssignments(failedAssignments);
                await Task.Delay(FailureBackoff, cancellationToken);
            }
        }
    }
}
