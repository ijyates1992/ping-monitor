using System.ComponentModel.DataAnnotations;
using PingMonitor.Web.Services.DatabaseStatus;

namespace PingMonitor.Web.ViewModels.Admin;

public sealed class DatabaseStatusPageViewModel
{
    public string ProviderName { get; init; } = string.Empty;
    public string DatabaseName { get; init; } = string.Empty;
    public string DataSource { get; init; } = string.Empty;
    public string ServerVersion { get; init; } = string.Empty;
    public bool ConnectionHealthy { get; init; }
    public int? CurrentSchemaVersion { get; init; }
    public int RequiredSchemaVersion { get; init; }
    public int TableCount { get; init; }
    public long TotalDataBytes { get; init; }
    public long TotalIndexBytes { get; init; }
    public IReadOnlyList<DatabaseStatusTableViewModel> Tables { get; init; } = Array.Empty<DatabaseStatusTableViewModel>();
    public DatabaseStatusRuntimeBufferViewModel RuntimeBuffer { get; init; } = new();
    public DatabasePruneForm PruneForm { get; init; } = new();
    public DatabasePrunePreviewViewModel? PrunePreview { get; init; }
    public string? PruneStatusMessage { get; init; }
    public DatabaseBackupForm BackupForm { get; init; } = new();
    public string? BackupStatusMessage { get; init; }
    public IReadOnlyList<DatabaseBackupRowViewModel> Backups { get; init; } = [];
}

public sealed class DatabaseStatusTableViewModel
{
    public string TableName { get; init; } = string.Empty;
    public long ApproximateRowCount { get; init; }
    public long DataBytes { get; init; }
    public long IndexBytes { get; init; }
    public long TotalBytes { get; init; }
}

public sealed class DatabaseStatusRuntimeBufferViewModel
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
    public double QueueUtilizationPercent { get; init; }
    public double BufferDropRatePercent { get; init; }
    public double FlushSuccessRatePercent { get; init; }
    public string CacheHitRateNote { get; init; } = "No request/result cache hit-rate metric is currently instrumented.";
}

public sealed class DatabasePruneForm
{
    [Required]
    public DatabasePruneTarget Target { get; set; } = DatabasePruneTarget.CheckResults;

    [Range(1, 3650)]
    public int AgeDays { get; set; } = 30;

    public string ConfirmationText { get; set; } = string.Empty;
}

public sealed class DatabasePrunePreviewViewModel
{
    public DatabasePruneTarget Target { get; init; }
    public int AgeDays { get; init; }
    public DateTimeOffset CutoffUtc { get; init; }
    public long EligibleRowCount { get; init; }
}

public sealed class DatabaseBackupForm
{
    public bool ConfirmBackup { get; set; }
}

public sealed class DatabaseBackupRowViewModel
{
    public string FileName { get; init; } = string.Empty;
    public string FileId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public long SizeBytes { get; init; }
}
