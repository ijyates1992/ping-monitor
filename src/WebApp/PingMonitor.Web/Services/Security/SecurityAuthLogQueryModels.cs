using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.Security;

public sealed class SecurityAuthLogListItem
{
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required string SubjectIdentifier { get; init; }
    public string? SourceIpAddress { get; init; }
    public required bool Success { get; init; }
    public string? FailureReason { get; init; }
    public string? UserId { get; init; }
    public string? AgentId { get; init; }
}

public sealed class SecurityAuthLogQuery
{
    public required SecurityAuthType AuthType { get; init; }
    public required bool IncludeSuccessful { get; init; }
    public required int Limit { get; init; }
}
