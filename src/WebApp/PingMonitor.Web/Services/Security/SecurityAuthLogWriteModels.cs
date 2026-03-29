namespace PingMonitor.Web.Services.Security;

public sealed class UserAuthLogWriteRequest
{
    public required string SubjectIdentifier { get; init; }
    public string? UserId { get; init; }
    public string? SourceIpAddress { get; init; }
    public bool Success { get; init; }
    public string? FailureReason { get; init; }
}

public sealed class AgentAuthLogWriteRequest
{
    public required string SubjectIdentifier { get; init; }
    public string? AgentId { get; init; }
    public string? SourceIpAddress { get; init; }
    public bool Success { get; init; }
    public string? FailureReason { get; init; }
    public string? RequestPath { get; init; }
}
