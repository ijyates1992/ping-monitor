using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.BufferedResults;

internal sealed class BufferedResultFlushBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBufferedResultIngestionService _buffer;
    private readonly ResultBufferOptions _options;
    private readonly ILogger<BufferedResultFlushBackgroundService> _logger;

    public BufferedResultFlushBackgroundService(
        IServiceScopeFactory scopeFactory,
        IBufferedResultIngestionService buffer,
        IOptions<ResultBufferOptions> options,
        ILogger<BufferedResultFlushBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _buffer = buffer;
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
                await PersistAndEvaluateAsync(batch, cancellationToken);
                _buffer.RecordFlushOutcome(batch.Count, DateTimeOffset.UtcNow, null);
            }
            catch (Exception ex)
            {
                _buffer.RecordFlushOutcome(0, DateTimeOffset.UtcNow, ex);
                _logger.LogError(ex, "Buffered raw result flush failed for batch size {BatchSize}.", batch.Count);
            }

            if (!flushAllPending && !_buffer.HasPendingFullBatch())
            {
                break;
            }
        }
    }

    private async Task PersistAndEvaluateAsync(IReadOnlyList<BufferedCheckResult> batch, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PingMonitorDbContext>();
        var stateEvaluationService = scope.ServiceProvider.GetRequiredService<IStateEvaluationService>();

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

        dbContext.CheckResults.AddRange(checkResults);
        await dbContext.SaveChangesAsync(cancellationToken);

        var affectedAssignmentIds = checkResults
            .Select(x => x.AssignmentId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        try
        {
            await stateEvaluationService.EvaluateAssignmentsAsync(affectedAssignmentIds, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Buffered result flush persisted raw results, but state evaluation failed for assignments: {AssignmentIds}.",
                affectedAssignmentIds);
        }

        _logger.LogInformation(
            "Buffered result flush persisted {PersistedCount} raw check results across {AssignmentCount} assignments.",
            checkResults.Length,
            affectedAssignmentIds.Length);
    }
}
