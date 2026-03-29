namespace PingMonitor.Web.Services.BrowserNotifications;

public sealed class BrowserNotificationFeedDto
{
    public bool BrowserNotificationsEnabled { get; init; }
    public string? LastEventId { get; init; }
    public IReadOnlyList<BrowserNotificationEventDto> Items { get; init; } = [];
}
