namespace PingMonitor.Web.Services;

public sealed class NotificationSuppressionDecision
{
    public bool IsSuppressed { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed class NotificationSuppressionStatus
{
    public bool QuietHoursEnabled { get; init; }
    public bool QuietHoursActiveNow { get; init; }
    public string QuietHoursStartLocalTime { get; init; } = "22:00";
    public string QuietHoursEndLocalTime { get; init; } = "07:00";
    public string ConfiguredTimeZoneId { get; init; } = "UTC";
    public string EffectiveTimeZoneId { get; init; } = "UTC";
    public DateTimeOffset EvaluatedAtUtc { get; init; }
    public string Reason { get; init; } = string.Empty;
}
