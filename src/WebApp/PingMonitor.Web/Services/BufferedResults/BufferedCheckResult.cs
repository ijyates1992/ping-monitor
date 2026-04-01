namespace PingMonitor.Web.Services.BufferedResults;

public sealed class BufferedCheckResult
{
    public string CheckResultId { get; init; } = string.Empty;
    public string AssignmentId { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string EndpointId { get; init; } = string.Empty;
    public DateTimeOffset CheckedAtUtc { get; init; }
    public bool Success { get; init; }
    public int? RoundTripMs { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset ReceivedAtUtc { get; init; }
    public string BatchId { get; init; } = string.Empty;
}
