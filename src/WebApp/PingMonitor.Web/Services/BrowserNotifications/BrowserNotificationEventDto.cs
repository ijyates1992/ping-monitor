namespace PingMonitor.Web.Services.BrowserNotifications;

public sealed class BrowserNotificationEventDto
{
    public string EventId { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; init; }
    public string? RelatedEndpointId { get; init; }
    public string? RelatedAgentId { get; init; }
    public string? Url { get; init; }
}
