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
    public string? OperationId { get; init; }
    public DatabaseBackupMode BackupMode { get; init; } = DatabaseBackupMode.Full;
    public DatabaseBackupCreationSource BackupSource { get; init; } = DatabaseBackupCreationSource.Manual;
    public string? RequestedBy { get; init; }
    public bool SuppressEventLogWrites { get; init; }
}

public sealed class DatabaseBackupUploadRequest
{
    public string OriginalFileName { get; init; } = string.Empty;
    public Stream Content { get; init; } = Stream.Null;
    public string? RequestedBy { get; init; }
}

public sealed class DatabaseBackupUploadResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? FileName { get; init; }
    public DateTimeOffset? UploadedAtUtc { get; init; }
    public long? FileSizeBytes { get; init; }
}

public sealed class DatabaseBackupRestoreRequest
{
    public const string ConfirmationKeyword = "RESTORE";

    public string? OperationId { get; init; }
    public string FileId { get; init; } = string.Empty;
    public string ConfirmationText { get; init; } = string.Empty;
    public string? RequestedBy { get; init; }
}

public sealed class DatabaseBackupRestoreResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? RestoredFileName { get; init; }
    public DateTimeOffset? RestoredFileCreatedAtUtc { get; init; }
    public DatabaseBackupMode BackupMode { get; init; } = DatabaseBackupMode.Full;
    public bool PreRestoreBackupCreated { get; init; }
    public string? PreRestoreBackupFileName { get; init; }
}

public sealed class DatabaseBackupDeleteRequest
{
    public string FileId { get; init; } = string.Empty;
    public bool ConfirmDelete { get; init; }
    public string? RequestedBy { get; init; }
}

public sealed class DatabaseBackupDeleteResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? FileName { get; init; }
}

public sealed class DatabaseBackupCreateResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? FileName { get; init; }
    public string? FullPath { get; init; }
    public DatabaseBackupMode BackupMode { get; init; } = DatabaseBackupMode.Full;
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
    public string MetadataSummary { get; init; } = string.Empty;
    public string BackupSource { get; init; } = string.Empty;
    public DatabaseBackupMode BackupMode { get; init; } = DatabaseBackupMode.Full;
    public string BackupModeDisplayName { get; init; } = string.Empty;
}

public enum DatabaseBackupMode
{
    Full = 1,
    Compact = 2
}

public enum DatabaseBackupCreationSource
{
    Manual = 1,
    PreRestore = 2
}
