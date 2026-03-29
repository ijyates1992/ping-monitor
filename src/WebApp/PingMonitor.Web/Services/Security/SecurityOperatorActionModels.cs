using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.Security;

public sealed class ActiveSecurityIpBlockDetails
{
    public string SecurityIpBlockId { get; init; } = string.Empty;
    public SecurityAuthType AuthType { get; init; }
    public string IpAddress { get; init; } = string.Empty;
    public SecurityIpBlockType BlockType { get; init; }
    public DateTimeOffset BlockedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}

public sealed class LockedOutUserDetails
{
    public string UserId { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public DateTimeOffset LockoutEndUtc { get; init; }
}

public sealed class UnlockSecurityUserRequest
{
    public required string UserId { get; init; }
    public string? OperatorUserId { get; init; }
}

public sealed class SecurityUserUnlockOperationResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }

    public static SecurityUserUnlockOperationResult Success() => new() { Succeeded = true };
    public static SecurityUserUnlockOperationResult Failure(string error) => new() { Succeeded = false, Error = error };
}
