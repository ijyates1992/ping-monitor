namespace PingMonitor.Web.Services.State;

public interface IAssignmentProcessingQueue
{
    AssignmentProcessingQueueEnqueueResult EnqueueAssignments(IReadOnlyCollection<string> assignmentIds);
    IReadOnlyList<string> DequeueBatch(int maxBatchSize);
    bool HasPendingItems();
    Task<bool> WaitForSignalAsync(TimeSpan timeout, CancellationToken cancellationToken);
    void RecordProcessedCount(int processedCount, DateTimeOffset processedAtUtc);
    void RecordFailure(Exception exception, DateTimeOffset failedAtUtc);
    AssignmentProcessingQueueSnapshot GetSnapshot();
}

public sealed record AssignmentProcessingQueueEnqueueResult(int EnqueuedCount, int CoalescedDuplicateCount);

public sealed record AssignmentProcessingQueueSnapshot(
    int QueueDepth,
    int PendingAssignmentCount,
    long TotalEnqueueCount,
    long CoalescedDuplicateCount,
    long DequeueCount,
    long ProcessedCount,
    long FailedCount,
    DateTimeOffset? LastEnqueueAtUtc,
    DateTimeOffset? LastDequeuedAtUtc,
    DateTimeOffset? LastProcessedAtUtc,
    DateTimeOffset? LastFailureAtUtc,
    string? LastFailureError);
