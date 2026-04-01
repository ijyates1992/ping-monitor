namespace PingMonitor.Web.Services.DatabaseStatus;

public enum DatabasePruneTarget
{
    SecurityAuthLogs = 1,
    EventLogs = 2,
    CheckResults = 3,
    StateTransitions = 4
}

public sealed class DatabasePrunePreviewRequest
{
    public DatabasePruneTarget Target { get; init; }
    public DateTimeOffset CutoffUtc { get; init; }
    public string? RequestedBy { get; init; }
}

public sealed class DatabasePrunePreviewResult
{
    public DatabasePruneTarget Target { get; init; }
    public DateTimeOffset CutoffUtc { get; init; }
    public long EligibleRowCount { get; init; }
}

public sealed class DatabasePruneExecuteRequest
{
    public const string ConfirmationKeyword = "PRUNE";

    public DatabasePruneTarget Target { get; init; }
    public DateTimeOffset CutoffUtc { get; init; }
    public string ConfirmationText { get; init; } = string.Empty;
    public string? RequestedBy { get; init; }
}

public sealed class DatabasePruneExecuteResult
{
    public DatabasePruneTarget Target { get; init; }
    public DateTimeOffset CutoffUtc { get; init; }
    public long EligibleRowCountBeforeDelete { get; init; }
    public int RowsDeleted { get; init; }
}

public sealed class DatabaseBackupCreateRequest
{
    public string? RequestedBy { get; init; }
}

public sealed class DatabaseBackupCreateResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? FileName { get; init; }
    public string? FullPath { get; init; }
    public long? FileSizeBytes { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }
}

public sealed class DatabaseBackupFileSnapshot
{
    public string FileName { get; init; } = string.Empty;
    public string FileId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public long FileSizeBytes { get; init; }
    public string FullPath { get; init; } = string.Empty;
}
