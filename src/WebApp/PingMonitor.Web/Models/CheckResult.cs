namespace PingMonitor.Web.Models;

public sealed class CheckResult
{
    public string? ResultId { get; set; }
    public string AssignmentId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string EndpointId { get; set; } = string.Empty;
    public DateTimeOffset CheckedAtUtc { get; set; }
    public bool Success { get; set; }
    public int? RoundTripMs { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public string BatchId { get; set; } = string.Empty;
}
