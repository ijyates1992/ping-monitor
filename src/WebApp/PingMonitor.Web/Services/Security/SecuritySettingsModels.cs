namespace PingMonitor.Web.Services.Security;

public sealed class SecuritySettingsDto
{
    public int AgentFailedAttemptsBeforeTemporaryIpBlock { get; init; }
    public int AgentTemporaryIpBlockDurationMinutes { get; init; }
    public int AgentFailedAttemptsBeforePermanentIpBlock { get; init; }

    public int UserFailedAttemptsBeforeTemporaryIpBlock { get; init; }
    public int UserTemporaryIpBlockDurationMinutes { get; init; }
    public int UserFailedAttemptsBeforePermanentIpBlock { get; init; }
    public int UserFailedAttemptsBeforeTemporaryAccountLockout { get; init; }
    public int UserTemporaryAccountLockoutDurationMinutes { get; init; }
    public bool SecurityLogRetentionEnabled { get; init; }
    public int SecurityLogRetentionDays { get; init; }
    public bool SecurityLogAutoPruneEnabled { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed class UpdateSecuritySettingsCommand
{
    public int AgentFailedAttemptsBeforeTemporaryIpBlock { get; init; }
    public int AgentTemporaryIpBlockDurationMinutes { get; init; }
    public int AgentFailedAttemptsBeforePermanentIpBlock { get; init; }

    public int UserFailedAttemptsBeforeTemporaryIpBlock { get; init; }
    public int UserTemporaryIpBlockDurationMinutes { get; init; }
    public int UserFailedAttemptsBeforePermanentIpBlock { get; init; }
    public int UserFailedAttemptsBeforeTemporaryAccountLockout { get; init; }
    public int UserTemporaryAccountLockoutDurationMinutes { get; init; }
    public bool SecurityLogRetentionEnabled { get; init; }
    public int SecurityLogRetentionDays { get; init; }
    public bool SecurityLogAutoPruneEnabled { get; init; }
}
