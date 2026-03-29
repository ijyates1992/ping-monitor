namespace PingMonitor.Web.Services.BrowserNotifications;

public interface IBrowserNotificationQueryService
{
    Task<BrowserNotificationFeedDto> GetFeedAsync(string? lastEventId, int maxItems, CancellationToken cancellationToken);
}
