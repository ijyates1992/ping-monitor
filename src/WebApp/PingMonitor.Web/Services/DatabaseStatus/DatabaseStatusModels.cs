namespace PingMonitor.Web.Services.DatabaseStatus;

public sealed class DatabaseStatusSnapshot
{
    public string ProviderName { get; init; } = "MySQL";
    public string DatabaseName { get; init; } = string.Empty;
    public string DataSource { get; init; } = string.Empty;
    public string ServerVersion { get; init; } = string.Empty;
    public bool ConnectionHealthy { get; init; }
    public int? CurrentSchemaVersion { get; init; }
    public int RequiredSchemaVersion { get; init; }
    public int TableCount { get; init; }
    public long TotalDataBytes { get; init; }
    public long TotalIndexBytes { get; init; }
    public IReadOnlyList<DatabaseTableStatusSnapshot> Tables { get; init; } = Array.Empty<DatabaseTableStatusSnapshot>();
    public ResultBufferRuntimeSnapshot ResultBuffer { get; init; } = new();
    public IReadOnlyList<DatabaseSubsystemActivityStatusSnapshot> DbActivityBySubsystem { get; init; } = Array.Empty<DatabaseSubsystemActivityStatusSnapshot>();
}

public sealed class DatabaseTableStatusSnapshot
{
    public string TableName { get; init; } = string.Empty;
    public long ApproximateRowCount { get; init; }
    public long DataBytes { get; init; }
    public long IndexBytes { get; init; }
}

public sealed class ResultBufferRuntimeSnapshot
{
    public bool BufferingEnabled { get; init; }
    public int ConfiguredMaxBatchSize { get; init; }
    public int ConfiguredFlushIntervalSeconds { get; init; }
    public int ConfiguredMaxQueueSize { get; init; }
    public int CurrentQueueDepth { get; init; }
    public long DroppedResultCount { get; init; }
    public long TotalEnqueueCount { get; init; }
    public long FlushCount { get; init; }
    public long FailedFlushCount { get; init; }
    public long PersistedResultCount { get; init; }
    public int LastFlushAttemptedCount { get; init; }
    public int LastFlushPersistedCount { get; init; }
    public DateTimeOffset? LastFlushCompletedAtUtc { get; init; }
    public string? LastFlushError { get; init; }
    public long LastPersistDurationMs { get; init; }
    public int LastEnqueuedAssignmentCount { get; init; }
    public DateTimeOffset? LastAssignmentsEnqueuedAtUtc { get; init; }
    public AssignmentProcessingQueueRuntimeSnapshot AssignmentProcessingQueue { get; init; } = new();
}

public sealed class AssignmentProcessingQueueRuntimeSnapshot
{
    public int QueueDepth { get; init; }
    public int PendingAssignmentCount { get; init; }
    public long TotalEnqueueCount { get; init; }
    public long CoalescedDuplicateCount { get; init; }
    public long DequeueCount { get; init; }
    public long ProcessedCount { get; init; }
    public long FailedCount { get; init; }
    public DateTimeOffset? LastEnqueueAtUtc { get; init; }
    public DateTimeOffset? LastDequeuedAtUtc { get; init; }
    public DateTimeOffset? LastProcessedAtUtc { get; init; }
    public DateTimeOffset? LastFailureAtUtc { get; init; }
    public string? LastFailureError { get; init; }
}

public sealed class DatabaseSubsystemActivityStatusSnapshot
{
    public string Subsystem { get; init; } = string.Empty;
    public DatabaseSubsystemActivityWindowSnapshot Lifetime { get; init; } = new();
    public DatabaseSubsystemActivityWindowSnapshot Recent { get; init; } = new();
    public DateTimeOffset? LastActivityAtUtc { get; init; }
    public DateTimeOffset? LastErrorAtUtc { get; init; }
    public string? LastCommandType { get; init; }
}

public sealed class DatabaseSubsystemActivityWindowSnapshot
{
    public long ReadCount { get; init; }
    public long WriteCount { get; init; }
    public long ReadErrorCount { get; init; }
    public long WriteErrorCount { get; init; }
    public long ReadDurationMs { get; init; }
    public long WriteDurationMs { get; init; }
    public long WriteRows { get; init; }
    public double AverageReadDurationMs { get; init; }
    public double AverageWriteDurationMs { get; init; }
    public long TotalDurationMs { get; init; }
}
