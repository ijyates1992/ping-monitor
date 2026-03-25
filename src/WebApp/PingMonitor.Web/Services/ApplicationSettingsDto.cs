namespace PingMonitor.Web.Services;

public sealed class ApplicationSettingsDto
{
    public string SiteUrl { get; init; } = string.Empty;
    public int DefaultPingIntervalSeconds { get; init; }
    public int DefaultRetryIntervalSeconds { get; init; }
    public int DefaultTimeoutMs { get; init; }
    public int DefaultFailureThreshold { get; init; }
    public int DefaultRecoveryThreshold { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}
