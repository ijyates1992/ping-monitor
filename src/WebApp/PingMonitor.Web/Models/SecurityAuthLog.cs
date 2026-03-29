namespace PingMonitor.Web.Models;

public sealed class SecurityAuthLog
{
    public string SecurityAuthLogId { get; set; } = $"sal_{Guid.NewGuid():N}";
    public DateTimeOffset OccurredAtUtc { get; set; }
    public SecurityAuthType AuthType { get; set; }
    public string SubjectIdentifier { get; set; } = string.Empty;
    public string? SourceIpAddress { get; set; }
    public bool Success { get; set; }
    public string? FailureReason { get; set; }
    public string? UserId { get; set; }
    public string? AgentId { get; set; }
    public string? DetailsJson { get; set; }
}
