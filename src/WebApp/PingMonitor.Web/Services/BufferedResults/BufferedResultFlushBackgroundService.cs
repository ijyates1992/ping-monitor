using Microsoft.Extensions.Options;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.Metrics;
using PingMonitor.Web.Services.State;

namespace PingMonitor.Web.Services.BufferedResults;

internal sealed class BufferedResultFlushBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBufferedResultIngestionService _buffer;
    private readonly ResultBufferOptions _options;
    private readonly IAssignmentProcessingQueue _assignmentProcessingQueue;
    private readonly ILogger<BufferedResultFlushBackgroundService> _logger;

    public BufferedResultFlushBackgroundService(
        IServiceScopeFactory scopeFactory,
        IBufferedResultIngestionService buffer,
        IAssignmentProcessingQueue assignmentProcessingQueue,
        IOptions<ResultBufferOptions> options,
        ILogger<BufferedResultFlushBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _buffer = buffer;
        _assignmentProcessingQueue = assignmentProcessingQueue;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var flushInterval = TimeSpan.FromSeconds(_options.ResultBufferFlushIntervalSeconds);
        var nextFallbackFlushAtUtc = DateTimeOffset.UtcNow.Add(flushInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
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

        var checkResults = batch.Select(item => new CheckResult
        {
            CheckResultId = item.CheckResultId,
            AssignmentId = item.AssignmentId,
            AgentId = item.AgentId,
            EndpointId = item.EndpointId,
            CheckedAtUtc = item.CheckedAtUtc,
            Success = item.Success,
            RoundTripMs = item.RoundTripMs,
            ErrorCode = item.ErrorCode,
            ErrorMessage = item.ErrorMessage,
            ReceivedAtUtc = item.ReceivedAtUtc,
            BatchId = item.BatchId
        }).ToArray();

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
            checkResults.Length,
            affectedAssignmentIds.Length,
            enqueueResult.EnqueuedCount,
            enqueueResult.CoalescedDuplicateCount);

        return new FlushResult(
            PersistDurationMs: persistDurationMs,
            AssignmentEnqueuedCount: enqueueResult.EnqueuedCount,
            LastAssignmentsEnqueuedAtUtc: enqueueResult.EnqueuedCount > 0 ? enqueueTimeUtc : null);
    }

    private sealed record FlushResult(long PersistDurationMs, int AssignmentEnqueuedCount, DateTimeOffset? LastAssignmentsEnqueuedAtUtc);
}
