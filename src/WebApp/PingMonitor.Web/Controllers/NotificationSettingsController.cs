using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Models;
using PingMonitor.Web.Models.Identity;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.Services.SmtpNotifications;
using PingMonitor.Web.ViewModels.Admin;
using System.Net.Mail;

namespace PingMonitor.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Admin)]
[Route("admin/notification-infrastructure-settings")]
[Route("settings/notifications")]
public sealed class NotificationSettingsController : Controller
{
    private readonly INotificationSettingsService _notificationSettingsService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISmtpNotificationSender _smtpNotificationSender;
    private readonly ILogger<NotificationSettingsController> _logger;

    public NotificationSettingsController(
        INotificationSettingsService notificationSettingsService,
        UserManager<ApplicationUser> userManager,
        ISmtpNotificationSender smtpNotificationSender,
        ILogger<NotificationSettingsController> logger)
    {
        _notificationSettingsService = notificationSettingsService;
        _userManager = userManager;
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
        ValidateSmtpSettings(model);

        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        var updated = await _notificationSettingsService.UpdateAsync(
            new UpdateNotificationSettingsCommand
            {
                BrowserNotificationsEnabled = false,
                BrowserNotifyEndpointDown = true,
                BrowserNotifyEndpointRecovered = true,
                BrowserNotifyAgentOffline = true,
                BrowserNotifyAgentOnline = true,
                BrowserNotificationsPermissionState = "default",
                TelegramEnabled = model.TelegramEnabled,
                TelegramBotToken = model.TelegramBotToken,
                TelegramClearBotToken = model.TelegramClearBotToken,
                TelegramInboundMode = ParseInboundMode(model.TelegramInboundMode),
                TelegramPollIntervalSeconds = model.TelegramPollIntervalSeconds,
                QuietHoursEnabled = false,
                QuietHoursStartLocalTime = "22:00",
                QuietHoursEndLocalTime = "07:00",
                QuietHoursTimeZoneId = "UTC",
                QuietHoursSuppressBrowserNotifications = true,
                QuietHoursSuppressSmtpNotifications = true,
                SmtpNotificationsEnabled = model.SmtpNotificationsEnabled,
                SmtpHost = model.SmtpHost,
                SmtpPort = model.SmtpPort,
                SmtpUseTls = model.SmtpUseTls,
                SmtpUsername = model.SmtpUsername,
                SmtpPassword = model.SmtpPassword,
                SmtpClearPassword = model.SmtpClearPassword,
                SmtpFromAddress = model.SmtpFromAddress,
                SmtpFromDisplayName = model.SmtpFromDisplayName,
                SmtpRecipientAddresses = null,
                SmtpNotifyEndpointDown = true,
                SmtpNotifyEndpointRecovered = true,
                SmtpNotifyAgentOffline = true,
                SmtpNotifyAgentOnline = true,
                UpdatedByUserId = User.Identity?.Name
            },
            cancellationToken);

        return View("Index", await ToViewModelAsync(updated, saved: true, cancellationToken));
    }

    [HttpPost("smtp-test")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendSmtpTest([FromForm] NotificationSettingsPageViewModel model, CancellationToken cancellationToken)
    {
        ValidateSmtpSettings(model);
        if (!ModelState.IsValid)
        {
            model.SmtpTestSent = false;
            model.SmtpTestMessage = "SMTP test send failed due to validation errors.";
            return View("Index", model);
        }

        await _notificationSettingsService.UpdateAsync(
            new UpdateNotificationSettingsCommand
            {
                BrowserNotificationsEnabled = false,
                BrowserNotifyEndpointDown = true,
                BrowserNotifyEndpointRecovered = true,
                BrowserNotifyAgentOffline = true,
                BrowserNotifyAgentOnline = true,
                BrowserNotificationsPermissionState = "default",
                TelegramEnabled = model.TelegramEnabled,
                TelegramBotToken = model.TelegramBotToken,
                TelegramClearBotToken = model.TelegramClearBotToken,
                TelegramInboundMode = ParseInboundMode(model.TelegramInboundMode),
                TelegramPollIntervalSeconds = model.TelegramPollIntervalSeconds,
                QuietHoursEnabled = false,
                QuietHoursStartLocalTime = "22:00",
                QuietHoursEndLocalTime = "07:00",
                QuietHoursTimeZoneId = "UTC",
                QuietHoursSuppressBrowserNotifications = true,
                QuietHoursSuppressSmtpNotifications = true,
                SmtpNotificationsEnabled = model.SmtpNotificationsEnabled,
                SmtpHost = model.SmtpHost,
                SmtpPort = model.SmtpPort,
                SmtpUseTls = model.SmtpUseTls,
                SmtpUsername = model.SmtpUsername,
                SmtpPassword = model.SmtpPassword,
                SmtpClearPassword = model.SmtpClearPassword,
                SmtpFromAddress = model.SmtpFromAddress,
                SmtpFromDisplayName = model.SmtpFromDisplayName,
                SmtpRecipientAddresses = null,
                SmtpNotifyEndpointDown = true,
                SmtpNotifyEndpointRecovered = true,
                SmtpNotifyAgentOffline = true,
                SmtpNotifyAgentOnline = true,
                UpdatedByUserId = User.Identity?.Name
            },
            cancellationToken);

        var user = await _userManager.GetUserAsync(User);
        if (user is null || string.IsNullOrWhiteSpace(user.Email) || !MailAddress.TryCreate(user.Email, out _))
        {
            model.SmtpTestSent = false;
            model.SmtpTestMessage = "Current user requires a valid email address for SMTP test.";
            return View("Index", model);
        }
        if (!user.EmailConfirmed)
        {
            model.SmtpTestSent = false;
            model.SmtpTestMessage = "Current user email address must be verified before SMTP test send.";
            return View("Index", model);
        }

        var result = await _smtpNotificationSender.SendTestAsync(user.Email, cancellationToken);
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

    }

    private static TelegramInboundMode ParseInboundMode(string? value)
    {
        return Enum.TryParse<TelegramInboundMode>(value?.Trim(), true, out var parsed) ? parsed : TelegramInboundMode.Polling;
    }

    private async Task<NotificationSettingsPageViewModel> ToViewModelAsync(NotificationSettingsDto settings, bool saved, CancellationToken cancellationToken)
    {
        return new NotificationSettingsPageViewModel
        {
            TelegramEnabled = settings.TelegramEnabled,
            TelegramInboundMode = settings.TelegramInboundMode.ToString(),
            TelegramPollIntervalSeconds = settings.TelegramPollIntervalSeconds,
            TelegramBotTokenConfigured = settings.TelegramBotTokenConfigured,
            SmtpNotificationsEnabled = settings.SmtpNotificationsEnabled,
            SmtpHost = settings.SmtpHost,
            SmtpPort = settings.SmtpPort,
            SmtpUseTls = settings.SmtpUseTls,
            SmtpUsername = settings.SmtpUsername,
            SmtpPasswordConfigured = settings.SmtpPasswordConfigured,
            SmtpFromAddress = settings.SmtpFromAddress,
            SmtpFromDisplayName = settings.SmtpFromDisplayName,
            UpdatedAtUtc = settings.UpdatedAtUtc,
            UpdatedByUserId = settings.UpdatedByUserId,
            Saved = saved
        };
    }
}
