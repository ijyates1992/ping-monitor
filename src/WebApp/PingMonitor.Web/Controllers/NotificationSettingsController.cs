using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.ViewModels.Admin;

namespace PingMonitor.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Admin)]
[Route("settings/notifications")]
public sealed class NotificationSettingsController : Controller
{
    private readonly INotificationSettingsService _notificationSettingsService;

    public NotificationSettingsController(INotificationSettingsService notificationSettingsService)
    {
        _notificationSettingsService = notificationSettingsService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var settings = await _notificationSettingsService.GetCurrentAsync(cancellationToken);
        return View("Index", ToViewModel(settings, saved: false));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index([FromForm] NotificationSettingsPageViewModel model, CancellationToken cancellationToken)
    {
        if (!IsValidPermissionState(model.BrowserNotificationsPermissionState))
        {
            ModelState.AddModelError(
                nameof(NotificationSettingsPageViewModel.BrowserNotificationsPermissionState),
                "Permission state must be default, granted, or denied.");
        }

        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        var updated = await _notificationSettingsService.UpdateAsync(
            new UpdateNotificationSettingsCommand
            {
                BrowserNotificationsEnabled = model.BrowserNotificationsEnabled,
                BrowserNotifyEndpointDown = model.BrowserNotifyEndpointDown,
                BrowserNotifyEndpointRecovered = model.BrowserNotifyEndpointRecovered,
                BrowserNotifyAgentOffline = model.BrowserNotifyAgentOffline,
                BrowserNotifyAgentOnline = model.BrowserNotifyAgentOnline,
                BrowserNotificationsPermissionState = model.BrowserNotificationsPermissionState,
                UpdatedByUserId = User.Identity?.Name
            },
            cancellationToken);

        return View("Index", ToViewModel(updated, saved: true));
    }

    private static bool IsValidPermissionState(string value)
    {
        return value is "default" or "granted" or "denied";
    }

    private static NotificationSettingsPageViewModel ToViewModel(NotificationSettingsDto settings, bool saved)
    {
        return new NotificationSettingsPageViewModel
        {
            BrowserNotificationsEnabled = settings.BrowserNotificationsEnabled,
            BrowserNotifyEndpointDown = settings.BrowserNotifyEndpointDown,
            BrowserNotifyEndpointRecovered = settings.BrowserNotifyEndpointRecovered,
            BrowserNotifyAgentOffline = settings.BrowserNotifyAgentOffline,
            BrowserNotifyAgentOnline = settings.BrowserNotifyAgentOnline,
            BrowserNotificationsPermissionState = settings.BrowserNotificationsPermissionState ?? "default",
            UpdatedAtUtc = settings.UpdatedAtUtc,
            UpdatedByUserId = settings.UpdatedByUserId,
            Saved = saved
        };
    }
}
