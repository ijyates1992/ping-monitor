namespace PingMonitor.Web.Models;

public sealed class AssignmentStateInterval
{
    public string AssignmentStateIntervalId { get; set; } = string.Empty;
    public string AssignmentId { get; set; } = string.Empty;
    public EndpointStateKind State { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
