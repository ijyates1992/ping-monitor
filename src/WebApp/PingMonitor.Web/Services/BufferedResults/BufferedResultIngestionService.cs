using Microsoft.Extensions.Options;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services.BufferedResults;

internal sealed class BufferedResultIngestionService : IBufferedResultIngestionService
{
    private readonly object _sync = new();
    private readonly Queue<BufferedCheckResult> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly ILogger<BufferedResultIngestionService> _logger;
    private readonly ResultBufferOptions _options;
    private long _droppedCount;
    private DateTimeOffset? _lastFlushCompletedAtUtc;
    private int _lastFlushPersistedCount;
    private string? _lastFlushError;

    public BufferedResultIngestionService(
        IOptions<ResultBufferOptions> options,
        ILogger<BufferedResultIngestionService> logger)
    {
        _logger = logger;
        _options = options.Value;
    }

    public void Enqueue(IReadOnlyCollection<BufferedCheckResult> results)
    {
        if (results.Count == 0)
        {
            return;
        }

        var droppedInWrite = 0;
        lock (_sync)
        {
            foreach (var result in results)
            {
                if (_queue.Count >= _options.ResultBufferMaxQueueSize)
                {
                    _queue.Dequeue();
                    _droppedCount++;
                    droppedInWrite++;
                }

                _queue.Enqueue(result);
            }
        }

        if (droppedInWrite > 0)
        {
            _logger.LogWarning(
                "Result buffer overflowed. Dropped {DroppedCount} oldest raw check results to preserve newer telemetry. Queue limit: {MaxQueueSize}.",
                droppedInWrite,
                _options.ResultBufferMaxQueueSize);
        }

        _signal.Release();
    }

    public bool HasPendingItems()
    {
        lock (_sync)
        {
            return _queue.Count > 0;
        }
    }

    public bool HasPendingFullBatch()
    {
        lock (_sync)
        {
            return _queue.Count >= _options.ResultBufferMaxBatchSize;
        }
    }

    public IReadOnlyList<BufferedCheckResult> DequeueBatch(int maxBatchSize)
    {
        var batch = new List<BufferedCheckResult>(maxBatchSize);
        lock (_sync)
        {
            while (_queue.Count > 0 && batch.Count < maxBatchSize)
            {
                batch.Add(_queue.Dequeue());
            }
        }

        return batch;
    }

    public BufferedResultBufferSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new BufferedResultBufferSnapshot(
                QueueDepth: _queue.Count,
                DroppedCount: _droppedCount,
                LastFlushCompletedAtUtc: _lastFlushCompletedAtUtc,
                LastFlushPersistedCount: _lastFlushPersistedCount,
                LastFlushError: _lastFlushError);
        }
    }

    public async Task<bool> WaitForSignalAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            return await _signal.WaitAsync(timeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void RecordFlushOutcome(int persistedCount, DateTimeOffset completedAtUtc, Exception? error)
    {
        lock (_sync)
        {
            _lastFlushCompletedAtUtc = completedAtUtc;
            _lastFlushPersistedCount = persistedCount;
            _lastFlushError = error?.Message;
        }
    }
}
