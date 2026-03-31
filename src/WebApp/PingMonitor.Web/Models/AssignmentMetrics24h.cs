namespace PingMonitor.Web.Models;

public sealed class AssignmentMetrics24h
{
    public string AssignmentId { get; set; } = string.Empty;
    public DateTimeOffset WindowStartUtc { get; set; }
    public DateTimeOffset WindowEndUtc { get; set; }
    public long UptimeSeconds { get; set; }
    public long DowntimeSeconds { get; set; }
    public long UnknownSeconds { get; set; }
    public long SuppressedSeconds { get; set; }
    public int? LastRttMs { get; set; }
    public DateTimeOffset? LastSuccessfulCheckUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
