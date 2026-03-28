namespace PingMonitor.Web.Models;

public sealed class EventLog
{
    public string EventLogId { get; set; } = $"evt_{Guid.NewGuid():N}";
    public DateTimeOffset OccurredAtUtc { get; set; }
    public EventCategory EventCategory { get; set; }
    public string EventType { get; set; } = string.Empty;
    public EventSeverity Severity { get; set; }
    public string? AgentId { get; set; }
    public string? EndpointId { get; set; }
    public string? AssignmentId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? DetailsJson { get; set; }
}
