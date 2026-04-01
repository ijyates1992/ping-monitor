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
}
