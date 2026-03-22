namespace PingMonitor.Web.Models;

public sealed class MonitorAssignment
{
    public string AssignmentId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string EndpointId { get; set; } = string.Empty;
    public CheckType CheckType { get; set; } = CheckType.Icmp;
    public bool Enabled { get; set; }
    public int PingIntervalSeconds { get; set; }
    public int RetryIntervalSeconds { get; set; }
    public int TimeoutMs { get; set; }
    public int FailureThreshold { get; set; }
    public int RecoveryThreshold { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
