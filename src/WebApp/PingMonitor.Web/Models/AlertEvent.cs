namespace PingMonitor.Web.Models;

public sealed class AlertEvent
{
    public string AlertId { get; set; } = string.Empty;
    public string AssignmentId { get; set; } = string.Empty;
    public string EndpointId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public DateTimeOffset OpenedAtUtc { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public AlertStatus Status { get; set; } = AlertStatus.Open;
    public EndpointStateKind TriggerState { get; set; } = EndpointStateKind.Down;
    public EndpointStateKind? ClearedState { get; set; }
    public bool Suppressed { get; set; }
    public string? Notes { get; set; }
}
