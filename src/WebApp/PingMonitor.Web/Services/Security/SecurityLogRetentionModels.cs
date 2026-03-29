namespace PingMonitor.Web.Services.Security;

public sealed class SecurityLogRetentionPreview
{
    public bool RetentionEnabled { get; init; }
    public int RetentionDays { get; init; }
    public bool AutoPruneEnabled { get; init; }
    public DateTimeOffset? CutoffUtc { get; init; }
    public int EligibleAuthLogRows { get; init; }
    public bool PruneSkipped { get; init; }
    public string? SkipReason { get; init; }
}

public sealed class SecurityLogPruneRequest
{
    public required string ConfirmationText { get; init; }
    public string? RequestedByUserId { get; init; }
    public required bool IsManual { get; init; }
}

public sealed class SecurityLogPruneResult
{
    public required SecurityLogRetentionPreview Preview { get; init; }
    public int RowsDeleted { get; init; }
    public int RowsRemaining { get; init; }
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
}
