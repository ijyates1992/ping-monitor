namespace PingMonitor.Web.Services;

public sealed class UpdateApplicationSettingsCommand
{
    public string SiteUrl { get; init; } = string.Empty;
    public int DefaultPingIntervalSeconds { get; init; }
    public int DefaultRetryIntervalSeconds { get; init; }
    public int DefaultTimeoutMs { get; init; }
    public int DefaultFailureThreshold { get; init; }
    public int DefaultRecoveryThreshold { get; init; }
}
