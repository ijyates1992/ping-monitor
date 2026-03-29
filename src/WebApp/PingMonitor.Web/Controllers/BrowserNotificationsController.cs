using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services.BrowserNotifications;

namespace PingMonitor.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public sealed class BrowserNotificationsController : ControllerBase
{
    private readonly IBrowserNotificationQueryService _browserNotificationQueryService;

    public BrowserNotificationsController(IBrowserNotificationQueryService browserNotificationQueryService)
    {
        _browserNotificationQueryService = browserNotificationQueryService;
    }

    [HttpGet("browser-feed")]
    public async Task<ActionResult<BrowserNotificationFeedDto>> GetBrowserFeed(
        [FromQuery] string? lastEventId,
        [FromQuery] int maxItems = 25,
        CancellationToken cancellationToken = default)
    {
        var feed = await _browserNotificationQueryService.GetFeedAsync(lastEventId, maxItems, cancellationToken);
        return Ok(feed);
    }
}
