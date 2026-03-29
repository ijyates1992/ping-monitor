using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.Security;

public sealed class SecurityIpBlockStatus
{
    public bool IsBlocked { get; init; }
    public SecurityIpBlockType? BlockType { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public string FailureReason { get; init; } = string.Empty;

    public static SecurityIpBlockStatus NotBlocked() => new()
    {
        IsBlocked = false,
        FailureReason = string.Empty
    };

    public static SecurityIpBlockStatus Blocked(SecurityIpBlockType blockType, DateTimeOffset? expiresAtUtc) => new()
    {
        IsBlocked = true,
        BlockType = blockType,
        ExpiresAtUtc = expiresAtUtc,
        FailureReason = blockType == SecurityIpBlockType.Temporary ? "ip_temporarily_blocked" : "ip_permanently_blocked"
    };
}

public sealed class UserLockoutStatus
{
    public bool IsLockedOut { get; init; }
    public DateTimeOffset? LockoutEndUtc { get; init; }

    public static UserLockoutStatus NotLockedOut() => new()
    {
        IsLockedOut = false,
        LockoutEndUtc = null
    };

    public static UserLockoutStatus Locked(DateTimeOffset lockoutEndUtc) => new()
    {
        IsLockedOut = true,
        LockoutEndUtc = lockoutEndUtc
    };
}
