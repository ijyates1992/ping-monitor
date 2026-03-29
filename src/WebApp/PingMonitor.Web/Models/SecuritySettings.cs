namespace PingMonitor.Web.Models;

public sealed class SecuritySettings
{
    public const int SingletonId = 1;

    public int SecuritySettingsId { get; set; } = SingletonId;

    public int AgentFailedAttemptsBeforeTemporaryIpBlock { get; set; } = 5;
    public int AgentTemporaryIpBlockDurationMinutes { get; set; } = 15;
    public int AgentFailedAttemptsBeforePermanentIpBlock { get; set; } = 20;

    public int UserFailedAttemptsBeforeTemporaryIpBlock { get; set; } = 5;
    public int UserTemporaryIpBlockDurationMinutes { get; set; } = 15;
    public int UserFailedAttemptsBeforePermanentIpBlock { get; set; } = 20;
    public int UserFailedAttemptsBeforeTemporaryAccountLockout { get; set; } = 5;
    public int UserTemporaryAccountLockoutDurationMinutes { get; set; } = 15;

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
