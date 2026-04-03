namespace PingMonitor.Web.Services.State;

internal sealed class AssignmentProcessingQueue : IAssignmentProcessingQueue
{
    private readonly object _sync = new();
    private readonly Queue<string> _queue = new();
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _signal = new(0);

    private long _totalEnqueueCount;
    private long _coalescedDuplicateCount;
    private long _dequeueCount;
    private long _processedCount;
    private long _failedCount;
    private DateTimeOffset? _lastEnqueueAtUtc;
    private DateTimeOffset? _lastDequeuedAtUtc;
    private DateTimeOffset? _lastProcessedAtUtc;
    private DateTimeOffset? _lastFailureAtUtc;
    private string? _lastFailureError;

    public AssignmentProcessingQueueEnqueueResult EnqueueAssignments(IReadOnlyCollection<string> assignmentIds)
    {
        if (assignmentIds.Count == 0)
        {
            return new AssignmentProcessingQueueEnqueueResult(0, 0);
        }

        var enqueued = 0;
        var coalesced = 0;
        var nowUtc = DateTimeOffset.UtcNow;

        lock (_sync)
        {
            foreach (var assignmentId in assignmentIds)
            {
                if (string.IsNullOrWhiteSpace(assignmentId))
                {
                    continue;
                }

                var normalizedAssignmentId = assignmentId.Trim();
                _totalEnqueueCount++;

                if (_pending.Add(normalizedAssignmentId))
                {
                    _queue.Enqueue(normalizedAssignmentId);
                    enqueued++;
                }
                else
                {
                    _coalescedDuplicateCount++;
                    coalesced++;
                }
            }

            if (enqueued > 0)
            {
                _lastEnqueueAtUtc = nowUtc;
            }
        }

        if (enqueued > 0)
        {
            _signal.Release();
        }

        return new AssignmentProcessingQueueEnqueueResult(enqueued, coalesced);
    }

    public IReadOnlyList<string> DequeueBatch(int maxBatchSize)
    {
        if (maxBatchSize <= 0)
        {
            return Array.Empty<string>();
        }

        var batch = new List<string>(maxBatchSize);
        var nowUtc = DateTimeOffset.UtcNow;

        lock (_sync)
        {
            while (_queue.Count > 0 && batch.Count < maxBatchSize)
            {
                var assignmentId = _queue.Dequeue();
                _pending.Remove(assignmentId);
                batch.Add(assignmentId);
            }

            if (batch.Count > 0)
            {
                _dequeueCount += batch.Count;
                _lastDequeuedAtUtc = nowUtc;
            }
        }

        return batch;
    }

    public bool HasPendingItems()
    {
        lock (_sync)
        {
            return _queue.Count > 0;
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

    public void RecordProcessedCount(int processedCount, DateTimeOffset processedAtUtc)
    {
        if (processedCount <= 0)
        {
            return;
        }

        lock (_sync)
        {
            _processedCount += processedCount;
            _lastProcessedAtUtc = processedAtUtc;
        }
    }

    public void RecordFailure(Exception exception, DateTimeOffset failedAtUtc)
    {
        lock (_sync)
        {
            _failedCount++;
            _lastFailureAtUtc = failedAtUtc;
            _lastFailureError = exception.Message;
        }
    }

    public AssignmentProcessingQueueSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new AssignmentProcessingQueueSnapshot(
                QueueDepth: _queue.Count,
                PendingAssignmentCount: _pending.Count,
                TotalEnqueueCount: _totalEnqueueCount,
                CoalescedDuplicateCount: _coalescedDuplicateCount,
                DequeueCount: _dequeueCount,
                ProcessedCount: _processedCount,
                FailedCount: _failedCount,
                LastEnqueueAtUtc: _lastEnqueueAtUtc,
                LastDequeuedAtUtc: _lastDequeuedAtUtc,
                LastProcessedAtUtc: _lastProcessedAtUtc,
                LastFailureAtUtc: _lastFailureAtUtc,
                LastFailureError: _lastFailureError);
        }
    }
}
