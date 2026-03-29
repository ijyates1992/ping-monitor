using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.Security;

public sealed class SecurityIpBlockListItem
{
    public string SecurityIpBlockId { get; init; } = string.Empty;
    public SecurityAuthType AuthType { get; init; }
    public string IpAddress { get; init; } = string.Empty;
    public SecurityIpBlockType BlockType { get; init; }
    public DateTimeOffset BlockedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public string? Reason { get; init; }
}

public sealed class ManualSecurityIpBlockRequest
{
    public required SecurityAuthType AuthType { get; init; }
    public required string IpAddress { get; init; }
    public string? Reason { get; init; }
    public string? CreatedByUserId { get; init; }
}

public sealed class RemoveSecurityIpBlockRequest
{
    public required string SecurityIpBlockId { get; init; }
    public string? RemovedByUserId { get; init; }
}

public sealed class SecurityIpBlockOperationResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }

    public static SecurityIpBlockOperationResult Success() => new() { Succeeded = true };
    public static SecurityIpBlockOperationResult Failure(string error) => new() { Succeeded = false, Error = error };
}
