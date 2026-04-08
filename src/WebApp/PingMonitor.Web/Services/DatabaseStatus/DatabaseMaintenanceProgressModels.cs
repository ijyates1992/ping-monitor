using System.Text.Json.Serialization;

namespace PingMonitor.Web.Services.DatabaseStatus;

public enum DatabaseMaintenanceOperationType
{
    BackupCreate = 1,
    Restore = 2
}

public sealed record DatabaseMaintenanceOperationProgress
{
    public string OperationId { get; init; } = string.Empty;
    public DatabaseMaintenanceOperationType OperationType { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public DateTimeOffset LastUpdatedAtUtc { get; init; }
    public bool IsRunning { get; init; }
    public bool Succeeded { get; init; }
    public bool Failed { get; init; }
    public string Stage { get; init; } = string.Empty;
    public int ApproximatePercentComplete { get; init; }
    public long? BytesProcessed { get; init; }
    public long? TotalBytes { get; init; }
    public string? FileName { get; init; }
    public string? BackupName { get; init; }
    public string? StatusMessage { get; init; }
    public string? DetailsMessage { get; init; }
    public string? ErrorMessage { get; init; }

    [JsonIgnore]
    public TimeSpan Elapsed => (CompletedAtUtc ?? DateTimeOffset.UtcNow) - StartedAtUtc;
}

public sealed class DatabaseMaintenanceOperationStartResult
{
    public bool Started { get; init; }
    public string Message { get; init; } = string.Empty;
    public DatabaseMaintenanceOperationProgress? Operation { get; init; }
}
