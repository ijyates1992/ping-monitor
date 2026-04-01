namespace PingMonitor.Web.Services.BufferedResults;

public interface IBufferedResultIngestionService
{
    void Enqueue(IReadOnlyCollection<BufferedCheckResult> results);
    bool HasPendingItems();
    bool HasPendingFullBatch();
    IReadOnlyList<BufferedCheckResult> DequeueBatch(int maxBatchSize);
    BufferedResultBufferSnapshot GetSnapshot();
    Task<bool> WaitForSignalAsync(TimeSpan timeout, CancellationToken cancellationToken);
    void RecordFlushOutcome(int attemptedCount, int persistedCount, DateTimeOffset completedAtUtc, Exception? error);
}

public sealed record BufferedResultBufferSnapshot(
    int QueueDepth,
    long DroppedCount,
    long TotalEnqueueCount,
    long FlushCount,
    long FailedFlushCount,
    long TotalPersistedCount,
    DateTimeOffset? LastFlushCompletedAtUtc,
    int LastFlushAttemptedCount,
    int LastFlushPersistedCount,
    string? LastFlushError);
