using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.EventLogs;

public sealed class EventLogWriteRequest
{
    public DateTimeOffset? OccurredAtUtc { get; init; }
    public required EventCategory Category { get; init; }
    public required string EventType { get; init; }
    public required EventSeverity Severity { get; init; }
    public string? AgentId { get; init; }
    public string? EndpointId { get; init; }
    public string? AssignmentId { get; init; }
    public required string Message { get; init; }
    public string? DetailsJson { get; init; }
}
