using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.Services.SmtpNotifications;
using PingMonitor.Web.ViewModels.Admin;
using System.Net.Mail;

namespace PingMonitor.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Admin)]
[Route("settings/notifications")]
public sealed class NotificationSettingsController : Controller
{
    private readonly INotificationSettingsService _notificationSettingsService;
    private readonly INotificationSuppressionService _notificationSuppressionService;
    private readonly ISmtpNotificationSender _smtpNotificationSender;
    private readonly ILogger<NotificationSettingsController> _logger;

    public NotificationSettingsController(
        INotificationSettingsService notificationSettingsService,
        INotificationSuppressionService notificationSuppressionService,
        ISmtpNotificationSender smtpNotificationSender,
        ILogger<NotificationSettingsController> logger)
    {
        _notificationSettingsService = notificationSettingsService;
        _notificationSuppressionService = notificationSuppressionService;
        _smtpNotificationSender = smtpNotificationSender;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var settings = await _notificationSettingsService.GetCurrentAsync(cancellationToken);
        return View("Index", await ToViewModelAsync(settings, saved: false, cancellationToken));
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

        ValidateSmtpSettings(model);
        ValidateQuietHours(model);

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
                QuietHoursEnabled = model.QuietHoursEnabled,
                QuietHoursStartLocalTime = model.QuietHoursStartLocalTime,
                QuietHoursEndLocalTime = model.QuietHoursEndLocalTime,
                QuietHoursTimeZoneId = model.QuietHoursTimeZoneId,
                QuietHoursSuppressBrowserNotifications = model.QuietHoursSuppressBrowserNotifications,
                QuietHoursSuppressSmtpNotifications = model.QuietHoursSuppressSmtpNotifications,
                SmtpNotificationsEnabled = model.SmtpNotificationsEnabled,
                SmtpHost = model.SmtpHost,
                SmtpPort = model.SmtpPort,
                SmtpUseTls = model.SmtpUseTls,
                SmtpUsername = model.SmtpUsername,
                SmtpPassword = model.SmtpPassword,
                SmtpClearPassword = model.SmtpClearPassword,
                SmtpFromAddress = model.SmtpFromAddress,
                SmtpFromDisplayName = model.SmtpFromDisplayName,
                SmtpRecipientAddresses = model.SmtpRecipientAddresses,
                SmtpNotifyEndpointDown = model.SmtpNotifyEndpointDown,
                SmtpNotifyEndpointRecovered = model.SmtpNotifyEndpointRecovered,
                SmtpNotifyAgentOffline = model.SmtpNotifyAgentOffline,
                SmtpNotifyAgentOnline = model.SmtpNotifyAgentOnline,
                UpdatedByUserId = User.Identity?.Name
            },
            cancellationToken);

        return View("Index", await ToViewModelAsync(updated, saved: true, cancellationToken));
    }

    [HttpPost("smtp-test")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendSmtpTest([FromForm] NotificationSettingsPageViewModel model, CancellationToken cancellationToken)
    {
        if (!IsValidPermissionState(model.BrowserNotificationsPermissionState))
        {
            model.BrowserNotificationsPermissionState = "default";
        }

        ValidateSmtpSettings(model);
        ValidateQuietHours(model);
        if (!ModelState.IsValid)
        {
            model.SmtpTestSent = false;
            model.SmtpTestMessage = "SMTP test send failed due to validation errors.";
            return View("Index", model);
        }

        await _notificationSettingsService.UpdateAsync(
            new UpdateNotificationSettingsCommand
            {
                BrowserNotificationsEnabled = model.BrowserNotificationsEnabled,
                BrowserNotifyEndpointDown = model.BrowserNotifyEndpointDown,
                BrowserNotifyEndpointRecovered = model.BrowserNotifyEndpointRecovered,
                BrowserNotifyAgentOffline = model.BrowserNotifyAgentOffline,
                BrowserNotifyAgentOnline = model.BrowserNotifyAgentOnline,
                BrowserNotificationsPermissionState = model.BrowserNotificationsPermissionState,
                QuietHoursEnabled = model.QuietHoursEnabled,
                QuietHoursStartLocalTime = model.QuietHoursStartLocalTime,
                QuietHoursEndLocalTime = model.QuietHoursEndLocalTime,
                QuietHoursTimeZoneId = model.QuietHoursTimeZoneId,
                QuietHoursSuppressBrowserNotifications = model.QuietHoursSuppressBrowserNotifications,
                QuietHoursSuppressSmtpNotifications = model.QuietHoursSuppressSmtpNotifications,
                SmtpNotificationsEnabled = model.SmtpNotificationsEnabled,
                SmtpHost = model.SmtpHost,
                SmtpPort = model.SmtpPort,
                SmtpUseTls = model.SmtpUseTls,
                SmtpUsername = model.SmtpUsername,
                SmtpPassword = model.SmtpPassword,
                SmtpClearPassword = model.SmtpClearPassword,
                SmtpFromAddress = model.SmtpFromAddress,
                SmtpFromDisplayName = model.SmtpFromDisplayName,
                SmtpRecipientAddresses = model.SmtpRecipientAddresses,
                SmtpNotifyEndpointDown = model.SmtpNotifyEndpointDown,
                SmtpNotifyEndpointRecovered = model.SmtpNotifyEndpointRecovered,
                SmtpNotifyAgentOffline = model.SmtpNotifyAgentOffline,
                SmtpNotifyAgentOnline = model.SmtpNotifyAgentOnline,
                UpdatedByUserId = User.Identity?.Name
            },
            cancellationToken);

        var result = await _smtpNotificationSender.SendTestAsync(cancellationToken);
        if (result.Success)
        {
            _logger.LogInformation("SMTP test send succeeded for user {UserId}.", User.Identity?.Name ?? "(unknown)");
        }
        else
        {
            _logger.LogWarning("SMTP test send failed for user {UserId}: {Reason}", User.Identity?.Name ?? "(unknown)", result.Message);
        }

        var refreshed = await _notificationSettingsService.GetCurrentAsync(cancellationToken);
        var viewModel = await ToViewModelAsync(refreshed, saved: false, cancellationToken);
        viewModel.SmtpTestSent = result.Success;
        viewModel.SmtpTestMessage = result.Message;
        return View("Index", viewModel);
    }

    private static bool IsValidPermissionState(string value)
    {
        return value is "default" or "granted" or "denied";
    }

    private void ValidateSmtpSettings(NotificationSettingsPageViewModel model)
    {
        if (!model.SmtpNotificationsEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(model.SmtpHost))
        {
            ModelState.AddModelError(nameof(model.SmtpHost), "SMTP host is required when SMTP notifications are enabled.");
        }

        if (model.SmtpPort <= 0 || model.SmtpPort > 65535)
        {
            ModelState.AddModelError(nameof(model.SmtpPort), "SMTP port must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(model.SmtpFromAddress) || !MailAddress.TryCreate(model.SmtpFromAddress, out _))
        {
            ModelState.AddModelError(nameof(model.SmtpFromAddress), "A valid SMTP from address is required.");
        }

        if (string.IsNullOrWhiteSpace(model.SmtpRecipientAddresses))
        {
            ModelState.AddModelError(nameof(model.SmtpRecipientAddresses), "At least one SMTP recipient address is required.");
        }
    }

    private void ValidateQuietHours(NotificationSettingsPageViewModel model)
    {
        if (!TimeOnly.TryParseExact(model.QuietHoursStartLocalTime?.Trim(), "HH:mm", out _))
        {
            ModelState.AddModelError(nameof(model.QuietHoursStartLocalTime), "Quiet hours start time must use HH:mm (24-hour) format.");
        }

        if (!TimeOnly.TryParseExact(model.QuietHoursEndLocalTime?.Trim(), "HH:mm", out _))
        {
            ModelState.AddModelError(nameof(model.QuietHoursEndLocalTime), "Quiet hours end time must use HH:mm (24-hour) format.");
        }

        if (string.IsNullOrWhiteSpace(model.QuietHoursTimeZoneId))
        {
            ModelState.AddModelError(nameof(model.QuietHoursTimeZoneId), "Quiet hours time zone ID is required.");
            return;
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(model.QuietHoursTimeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            ModelState.AddModelError(nameof(model.QuietHoursTimeZoneId), "Quiet hours time zone ID was not found on this server.");
        }
        catch (InvalidTimeZoneException)
        {
            ModelState.AddModelError(nameof(model.QuietHoursTimeZoneId), "Quiet hours time zone ID is invalid.");
        }
    }

    private async Task<NotificationSettingsPageViewModel> ToViewModelAsync(NotificationSettingsDto settings, bool saved, CancellationToken cancellationToken)
    {
        var suppressionStatus = await _notificationSuppressionService.GetCurrentStatusAsync(cancellationToken);
        return new NotificationSettingsPageViewModel
        {
            BrowserNotificationsEnabled = settings.BrowserNotificationsEnabled,
            BrowserNotifyEndpointDown = settings.BrowserNotifyEndpointDown,
            BrowserNotifyEndpointRecovered = settings.BrowserNotifyEndpointRecovered,
            BrowserNotifyAgentOffline = settings.BrowserNotifyAgentOffline,
            BrowserNotifyAgentOnline = settings.BrowserNotifyAgentOnline,
            BrowserNotificationsPermissionState = settings.BrowserNotificationsPermissionState ?? "default",
            QuietHoursEnabled = settings.QuietHoursEnabled,
            QuietHoursStartLocalTime = settings.QuietHoursStartLocalTime,
            QuietHoursEndLocalTime = settings.QuietHoursEndLocalTime,
            QuietHoursTimeZoneId = settings.QuietHoursTimeZoneId,
            QuietHoursSuppressBrowserNotifications = settings.QuietHoursSuppressBrowserNotifications,
            QuietHoursSuppressSmtpNotifications = settings.QuietHoursSuppressSmtpNotifications,
            QuietHoursCurrentlyActive = suppressionStatus.QuietHoursActiveNow,
            QuietHoursCurrentStatusLabel = suppressionStatus.QuietHoursActiveNow ? "active" : "inactive",
            QuietHoursCurrentReason = suppressionStatus.Reason,
            QuietHoursResolvedTimeZoneId = suppressionStatus.EffectiveTimeZoneId,
            QuietHoursEvaluatedAtUtc = suppressionStatus.EvaluatedAtUtc,
            SmtpNotificationsEnabled = settings.SmtpNotificationsEnabled,
            SmtpHost = settings.SmtpHost,
            SmtpPort = settings.SmtpPort,
            SmtpUseTls = settings.SmtpUseTls,
            SmtpUsername = settings.SmtpUsername,
            SmtpPasswordConfigured = settings.SmtpPasswordConfigured,
            SmtpFromAddress = settings.SmtpFromAddress,
            SmtpFromDisplayName = settings.SmtpFromDisplayName,
            SmtpRecipientAddresses = settings.SmtpRecipientAddresses,
            SmtpNotifyEndpointDown = settings.SmtpNotifyEndpointDown,
            SmtpNotifyEndpointRecovered = settings.SmtpNotifyEndpointRecovered,
            SmtpNotifyAgentOffline = settings.SmtpNotifyAgentOffline,
            SmtpNotifyAgentOnline = settings.SmtpNotifyAgentOnline,
            UpdatedAtUtc = settings.UpdatedAtUtc,
            UpdatedByUserId = settings.UpdatedByUserId,
            Saved = saved
        };
    }
}
