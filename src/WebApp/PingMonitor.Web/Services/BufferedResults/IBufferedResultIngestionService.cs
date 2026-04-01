namespace PingMonitor.Web.Services.BufferedResults;

public interface IBufferedResultIngestionService
{
    void Enqueue(IReadOnlyCollection<BufferedCheckResult> results);
    bool HasPendingItems();
    bool HasPendingFullBatch();
    IReadOnlyList<BufferedCheckResult> DequeueBatch(int maxBatchSize);
    BufferedResultBufferSnapshot GetSnapshot();
    Task<bool> WaitForSignalAsync(TimeSpan timeout, CancellationToken cancellationToken);
    void RecordFlushOutcome(int persistedCount, DateTimeOffset completedAtUtc, Exception? error);
}

public sealed record BufferedResultBufferSnapshot(
    int QueueDepth,
    long DroppedCount,
    DateTimeOffset? LastFlushCompletedAtUtc,
    int LastFlushPersistedCount,
    string? LastFlushError);
