using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.Metrics;
using PingMonitor.Web.Services.Diagnostics;
using PingMonitor.Web.Services.State;
using PingMonitor.Web.Services.StartupGate;

namespace PingMonitor.Web.Services.BufferedResults;

internal sealed class BufferedResultFlushBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBufferedResultIngestionService _buffer;
    private readonly ResultBufferOptions _options;
    private readonly IAssignmentProcessingQueue _assignmentProcessingQueue;
    private readonly IStartupGateRuntimeState _startupGateRuntimeState;
    private readonly ILogger<BufferedResultFlushBackgroundService> _logger;

    public BufferedResultFlushBackgroundService(
        IServiceScopeFactory scopeFactory,
        IBufferedResultIngestionService buffer,
        IAssignmentProcessingQueue assignmentProcessingQueue,
        IStartupGateRuntimeState startupGateRuntimeState,
        IOptions<ResultBufferOptions> options,
        ILogger<BufferedResultFlushBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _buffer = buffer;
        _assignmentProcessingQueue = assignmentProcessingQueue;
        _startupGateRuntimeState = startupGateRuntimeState;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var flushInterval = TimeSpan.FromSeconds(_options.ResultBufferFlushIntervalSeconds);
        var nextFallbackFlushAtUtc = DateTimeOffset.UtcNow.Add(flushInterval);
        var wasBlockedByStartupGate = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_startupGateRuntimeState.IsOperationalMode)
            {
                if (!wasBlockedByStartupGate)
                {
                    _logger.LogInformation("Buffered result flush is paused because Startup Gate is active.");
                    wasBlockedByStartupGate = true;
                }

                await Task.Delay(flushInterval, stoppingToken);
                continue;
            }

            if (wasBlockedByStartupGate)
            {
                _logger.LogInformation("Startup Gate is cleared. Buffered result flush is resuming normal operation.");
                wasBlockedByStartupGate = false;
                nextFallbackFlushAtUtc = DateTimeOffset.UtcNow.Add(flushInterval);
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var waitTime = nowUtc >= nextFallbackFlushAtUtc
                ? TimeSpan.Zero
                : nextFallbackFlushAtUtc - nowUtc;

            await _buffer.WaitForSignalAsync(waitTime, stoppingToken);

            var fallbackDue = DateTimeOffset.UtcNow >= nextFallbackFlushAtUtc;
            if (!_buffer.HasPendingFullBatch() && !fallbackDue)
            {
                continue;
            }

            await FlushAvailableBatchesAsync(flushAllPending: fallbackDue, stoppingToken);
            nextFallbackFlushAtUtc = DateTimeOffset.UtcNow.Add(flushInterval);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_startupGateRuntimeState.IsOperationalMode)
        {
            _logger.LogInformation("Startup Gate is active during shutdown. Skipping buffered result flush drain.");
            await base.StopAsync(cancellationToken);
            return;
        }

        _logger.LogInformation("Application shutdown requested. Attempting best-effort flush of buffered raw check results.");
        try
        {
            await FlushAvailableBatchesAsync(flushAllPending: true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Best-effort shutdown flush failed for buffered raw check results.");
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task FlushAvailableBatchesAsync(bool flushAllPending, CancellationToken cancellationToken)
    {
        while (_buffer.HasPendingItems())
        {
            var batch = _buffer.DequeueBatch(_options.ResultBufferMaxBatchSize);
            if (batch.Count == 0)
            {
                break;
            }

            try
            {
                var flushResult = await PersistAndEnqueueAssignmentsAsync(batch, cancellationToken);
                _buffer.RecordFlushOutcome(
                    batch.Count,
                    batch.Count,
                    DateTimeOffset.UtcNow,
                    null,
                    flushResult.PersistDurationMs,
                    flushResult.AssignmentEnqueuedCount,
                    flushResult.LastAssignmentsEnqueuedAtUtc);
            }
            catch (Exception ex)
            {
                _buffer.RecordFlushOutcome(
                    batch.Count,
                    0,
                    DateTimeOffset.UtcNow,
                    ex,
                    persistDurationMs: 0,
                    enqueuedAssignmentCount: 0,
                    lastAssignmentsEnqueuedAtUtc: null);
                _logger.LogError(ex, "Buffered raw result flush failed for batch size {BatchSize}.", batch.Count);
            }

            if (!flushAllPending && !_buffer.HasPendingFullBatch())
            {
                break;
            }
        }
    }

    private async Task<FlushResult> PersistAndEnqueueAssignmentsAsync(IReadOnlyList<BufferedCheckResult> batch, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PingMonitorDbContext>();
        var assignmentMetrics24hService = scope.ServiceProvider.GetRequiredService<IAssignmentMetrics24hService>();
        var dbActivityScope = scope.ServiceProvider.GetRequiredService<IDbActivityScope>();
        using var dbScope = dbActivityScope.BeginScope("RawResultFlush");

        var assignmentIds = batch
            .Select(item => item.AssignmentId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (assignmentIds.Length == 0)
        {
            return new FlushResult(
                PersistDurationMs: 0,
                AssignmentEnqueuedCount: 0,
                LastAssignmentsEnqueuedAtUtc: null);
        }

        var assignmentIdPredicate = BuildAssignmentIdPredicate(assignmentIds);
        var assignmentContextRows = await dbContext.MonitorAssignments.AsNoTracking()
            .Where(assignmentIdPredicate)
            .Select(x => new
            {
                x.AssignmentId,
                x.AgentId,
                x.EndpointId
            })
            .ToArrayAsync(cancellationToken);

        var assignmentContexts = assignmentContextRows
            .ToDictionary(x => x.AssignmentId, StringComparer.Ordinal);

        var checkResults = new List<CheckResult>(batch.Count);
        var missingAssignmentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in batch)
        {
            if (!assignmentContexts.TryGetValue(item.AssignmentId, out var assignmentContext))
            {
                missingAssignmentIds.Add(item.AssignmentId);
                continue;
            }

            checkResults.Add(new CheckResult
            {
                // AssignmentId is the source-of-truth identity for raw rows.
                // AgentId/EndpointId are compatibility fields until Phase 2 schema slimming.
                CheckResultId = item.CheckResultId,
                AssignmentId = item.AssignmentId,
                AgentId = assignmentContext.AgentId,
                EndpointId = assignmentContext.EndpointId,
                CheckedAtUtc = item.CheckedAtUtc,
                Success = item.Success,
                RoundTripMs = item.RoundTripMs,
                ErrorCode = item.ErrorCode,
                ErrorMessage = item.ErrorMessage,
                ReceivedAtUtc = item.ReceivedAtUtc,
                BatchId = item.BatchId
            });
        }

        if (missingAssignmentIds.Count > 0)
        {
            _logger.LogWarning(
                "Buffered raw result flush skipped {SkippedCount} rows due to missing assignment metadata. Missing assignment IDs: {MissingAssignmentIds}.",
                batch.Count - checkResults.Count,
                string.Join(", ", missingAssignmentIds.OrderBy(x => x, StringComparer.Ordinal)));
        }

        if (checkResults.Count == 0)
        {
            return new FlushResult(
                PersistDurationMs: 0,
                AssignmentEnqueuedCount: 0,
                LastAssignmentsEnqueuedAtUtc: null);
        }

        var persistStartedAtUtc = DateTimeOffset.UtcNow;
        dbContext.CheckResults.AddRange(checkResults);
        await dbContext.SaveChangesAsync(cancellationToken);
        var persistDurationMs = Math.Max(0, (long)(DateTimeOffset.UtcNow - persistStartedAtUtc).TotalMilliseconds);

        await assignmentMetrics24hService.ApplyCheckResultsBatchAsync(checkResults, cancellationToken);

        var affectedAssignmentIds = checkResults
            .Select(x => x.AssignmentId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var enqueueResult = _assignmentProcessingQueue.EnqueueAssignments(affectedAssignmentIds);
        var enqueueTimeUtc = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Buffered result flush persisted {PersistedCount} raw check results across {AssignmentCount} assignments. Enqueued {EnqueuedAssignments} assignments for downstream processing ({CoalescedDuplicates} coalesced duplicates).",
            checkResults.Count,
            affectedAssignmentIds.Length,
            enqueueResult.EnqueuedCount,
            enqueueResult.CoalescedDuplicateCount);

        return new FlushResult(
            PersistDurationMs: persistDurationMs,
            AssignmentEnqueuedCount: enqueueResult.EnqueuedCount,
            LastAssignmentsEnqueuedAtUtc: enqueueResult.EnqueuedCount > 0 ? enqueueTimeUtc : null);
    }

    private static Expression<Func<MonitorAssignment, bool>> BuildAssignmentIdPredicate(IReadOnlyList<string> assignmentIds)
    {
        var assignmentParameter = Expression.Parameter(typeof(MonitorAssignment), "assignment");
        var assignmentIdProperty = Expression.Property(assignmentParameter, nameof(MonitorAssignment.AssignmentId));
        Expression? predicateBody = null;

        foreach (var assignmentId in assignmentIds)
        {
            var assignmentIdConstant = Expression.Constant(assignmentId, typeof(string));
            var equalsExpression = Expression.Equal(assignmentIdProperty, assignmentIdConstant);
            predicateBody = predicateBody is null
                ? equalsExpression
                : Expression.OrElse(predicateBody, equalsExpression);
        }

        predicateBody ??= Expression.Constant(false);
        return Expression.Lambda<Func<MonitorAssignment, bool>>(predicateBody, assignmentParameter);
    }

    private sealed record FlushResult(long PersistDurationMs, int AssignmentEnqueuedCount, DateTimeOffset? LastAssignmentsEnqueuedAtUtc);
}
