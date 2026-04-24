using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using PingMonitor.Web.Models.Identity;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.EventLogs;
using PingMonitor.Web.Services.SmtpNotifications;
using PingMonitor.Web.Services.Telegram;
using PingMonitor.Web.Services.Time;
using PingMonitor.Web.ViewModels.Profile;
using System.Text;
using System.Text.Json;

namespace PingMonitor.Web.Controllers;

[Authorize]
[Route("profile")]
public sealed class ProfileController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserNotificationSettingsService _userNotificationSettingsService;
    private readonly INotificationSettingsService _notificationSettingsService;
    private readonly ITelegramLinkService _telegramLinkService;
    private readonly ITelegramBotIdentityResolver _telegramBotIdentityResolver;
    private readonly ISmtpNotificationSender _smtpNotificationSender;
    private readonly IEventLogService _eventLogService;
    private readonly IUserTimeZoneService _userTimeZoneService;
    private readonly ILogger<ProfileController> _logger;

    public ProfileController(
        UserManager<ApplicationUser> userManager,
        IUserNotificationSettingsService userNotificationSettingsService,
        INotificationSettingsService notificationSettingsService,
        ITelegramLinkService telegramLinkService,
        ITelegramBotIdentityResolver telegramBotIdentityResolver,
        ISmtpNotificationSender smtpNotificationSender,
        IEventLogService eventLogService,
        IUserTimeZoneService userTimeZoneService,
        ILogger<ProfileController> logger)
    {
        _userManager = userManager;
        _userNotificationSettingsService = userNotificationSettingsService;
        _notificationSettingsService = notificationSettingsService;
        _telegramLinkService = telegramLinkService;
        _telegramBotIdentityResolver = telegramBotIdentityResolver;
        _smtpNotificationSender = smtpNotificationSender;
        _eventLogService = eventLogService;
        _userTimeZoneService = userTimeZoneService;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = await BuildModelAsync(cancellationToken);
        return View("Index", model);
    }

    [HttpGet("notification-settings")]
    public async Task<IActionResult> NotificationSettings(CancellationToken cancellationToken)
    {
        var model = await BuildModelAsync(cancellationToken);
        return View("NotificationSettings", model);
    }

    [HttpPost("account")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAccount([FromForm] UpdateProfileAccountDetailsInputModel model, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync();
        if (user is null)
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildModelAsync(cancellationToken);
            invalidModel.Email = model.Email;
            return View("Index", invalidModel);
        }

        var trimmedEmail = model.Email.Trim();
        var setEmailResult = await _userManager.SetEmailAsync(user, trimmedEmail);
        if (!setEmailResult.Succeeded)
        {
            foreach (var error in setEmailResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            var invalidModel = await BuildModelAsync(cancellationToken);
            invalidModel.Email = model.Email;
            return View("Index", invalidModel);
        }

        user.EmailConfirmed = false;
        user.NormalizedEmail = trimmedEmail.ToUpperInvariant();
        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            foreach (var error in updateResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            var invalidModel = await BuildModelAsync(cancellationToken);
            invalidModel.Email = model.Email;
            return View("Index", invalidModel);
        }

        var sendResult = await SendVerificationEmailAsync(user, cancellationToken);
        var refreshed = await BuildModelAsync(cancellationToken);
        refreshed.AccountSaved = true;
        refreshed.EmailVerificationResendSucceeded = sendResult.Success;
        refreshed.EmailVerificationMessage = sendResult.Message;
        return View("Index", refreshed);
    }

    [HttpPost("email-verification/resend")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendEmailVerification(CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync();
        if (user is null)
        {
            return Challenge();
        }

        if (user.EmailConfirmed)
        {
            var alreadyVerified = await BuildModelAsync(cancellationToken);
            alreadyVerified.EmailVerificationMessage = "Your email is already verified.";
            alreadyVerified.EmailVerificationResendSucceeded = true;
            return View("NotificationSettings", alreadyVerified);
        }

        var sendResult = await SendVerificationEmailAsync(user, cancellationToken);
        var refreshed = await BuildModelAsync(cancellationToken);
        refreshed.EmailVerificationResendSucceeded = sendResult.Success;
        refreshed.EmailVerificationMessage = sendResult.Message;
        return View("NotificationSettings", refreshed);
    }

    [HttpPost("password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePassword([FromForm] ProfilePageViewModel model, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync();
        if (user is null)
        {
            return Challenge();
        }

        if (string.IsNullOrWhiteSpace(model.CurrentPassword))
        {
            ModelState.AddModelError(nameof(ProfilePageViewModel.CurrentPassword), "Current password is required.");
        }

        if (string.IsNullOrWhiteSpace(model.NewPassword))
        {
            ModelState.AddModelError(nameof(ProfilePageViewModel.NewPassword), "New password is required.");
        }

        if (!string.Equals(model.NewPassword, model.ConfirmNewPassword, StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(ProfilePageViewModel.ConfirmNewPassword), "New password confirmation must match.");
        }

        if (!ModelState.IsValid)
        {
            return View("Index", await BuildModelAsync(cancellationToken, model));
        }

        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View("Index", await BuildModelAsync(cancellationToken, model));
        }

        var refreshed = await BuildModelAsync(cancellationToken);
        refreshed.PasswordSaved = true;
        return View("Index", refreshed);
    }

    [HttpPost("display-preferences")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDisplayPreferences([FromForm] UpdateProfileDisplayPreferencesInputModel model, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync();
        if (user is null)
        {
            return Challenge();
        }

        if (!_userTimeZoneService.IsSupportedTimeZoneId(model.DisplayTimeZoneId))
        {
            ModelState.AddModelError(nameof(UpdateProfileDisplayPreferencesInputModel.DisplayTimeZoneId), "Select a valid display time zone.");
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildModelAsync(cancellationToken);
            invalidModel.DisplayTimeZoneId = model.DisplayTimeZoneId;
            return View("Index", invalidModel);
        }

        user.DisplayTimeZoneId = model.DisplayTimeZoneId.Trim();
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            var invalidModel = await BuildModelAsync(cancellationToken);
            invalidModel.DisplayTimeZoneId = model.DisplayTimeZoneId;
            return View("Index", invalidModel);
        }

        var refreshed = await BuildModelAsync(cancellationToken);
        refreshed.DisplayPreferencesSaved = true;
        return View("Index", refreshed);
    }

    [HttpPost("notification-settings")]
    [HttpPost("notifications")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateNotifications([FromForm] ProfilePageViewModel model, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync();
        if (user is null)
        {
            return Challenge();
        }

        if (!TimeOnly.TryParseExact(model.QuietHoursStartLocalTime?.Trim(), "HH:mm", out _))
        {
            ModelState.AddModelError(nameof(ProfilePageViewModel.QuietHoursStartLocalTime), "Use HH:mm format.");
        }
        if (!TimeOnly.TryParseExact(model.QuietHoursEndLocalTime?.Trim(), "HH:mm", out _))
        {
            ModelState.AddModelError(nameof(ProfilePageViewModel.QuietHoursEndLocalTime), "Use HH:mm format.");
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(model.QuietHoursTimeZoneId.Trim());
        }
        catch
        {
            ModelState.AddModelError(nameof(ProfilePageViewModel.QuietHoursTimeZoneId), "Quiet hours time zone ID is invalid.");
        }

        if (!ModelState.IsValid)
        {
            return View("NotificationSettings", await BuildModelAsync(cancellationToken, model));
        }

        await _userNotificationSettingsService.UpdateAsync(new UpdateUserNotificationSettingsCommand
        {
            UserId = user.Id,
            BrowserNotificationsEnabled = model.BrowserNotificationsEnabled,
            BrowserNotifyEndpointDown = model.BrowserNotifyEndpointDown,
            BrowserNotifyEndpointRecovered = model.BrowserNotifyEndpointRecovered,
            BrowserNotifyAgentOffline = model.BrowserNotifyAgentOffline,
            BrowserNotifyAgentOnline = model.BrowserNotifyAgentOnline,
            BrowserNotificationsPermissionState = model.BrowserNotificationsPermissionState,
            SmtpNotificationsEnabled = model.SmtpNotificationsEnabled,
            SmtpNotifyEndpointDown = model.SmtpNotifyEndpointDown,
            SmtpNotifyEndpointRecovered = model.SmtpNotifyEndpointRecovered,
            SmtpNotifyAgentOffline = model.SmtpNotifyAgentOffline,
            SmtpNotifyAgentOnline = model.SmtpNotifyAgentOnline,
            TelegramNotificationsEnabled = model.TelegramNotificationsEnabled,
            TelegramNotifyEndpointDown = model.TelegramNotifyEndpointDown,
            TelegramNotifyEndpointRecovered = model.TelegramNotifyEndpointRecovered,
            TelegramNotifyAgentOffline = model.TelegramNotifyAgentOffline,
            TelegramNotifyAgentOnline = model.TelegramNotifyAgentOnline,
            QuietHoursEnabled = model.QuietHoursEnabled,
            QuietHoursStartLocalTime = model.QuietHoursStartLocalTime ?? "22:00",
            QuietHoursEndLocalTime = model.QuietHoursEndLocalTime ?? "07:00",
            QuietHoursTimeZoneId = model.QuietHoursTimeZoneId ?? "UTC",
            QuietHoursSuppressBrowserNotifications = model.QuietHoursSuppressBrowserNotifications,
            QuietHoursSuppressSmtpNotifications = model.QuietHoursSuppressSmtpNotifications,
            QuietHoursSuppressTelegramNotifications = model.QuietHoursSuppressTelegramNotifications
        }, cancellationToken);

        var refreshed = await BuildModelAsync(cancellationToken);
        refreshed.NotificationsSaved = true;
        return View("NotificationSettings", refreshed);
    }

    [HttpPost("notification-settings/telegram/remove")]
    [HttpPost("telegram/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveTelegramAccount(CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync();
        if (user is null)
        {
            return Challenge();
        }

        await _telegramLinkService.UnlinkAccountAsync(user.Id, cancellationToken);
        var refreshed = await BuildModelAsync(cancellationToken);
        refreshed.TelegramAccountRemoved = true;
        return View("NotificationSettings", refreshed);
    }

    [HttpPost("notification-settings/telegram/generate-code")]
    [HttpPost("telegram/generate-code")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateTelegramCode(CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync();
        if (user is null)
        {
            return Challenge();
        }

        await _telegramLinkService.GenerateCodeAsync(user.Id, cancellationToken);
        var refreshed = await BuildModelAsync(cancellationToken);
        refreshed.TelegramCodeGenerated = true;
        return View("NotificationSettings", refreshed);
    }

    private async Task<ApplicationUser?> RequireUserAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(userId) ? null : await _userManager.FindByIdAsync(userId);
    }

    private async Task<ProfilePageViewModel> BuildModelAsync(CancellationToken cancellationToken, ProfilePageViewModel? source = null)
    {
        var user = await RequireUserAsync();
        if (user is null)
        {
            return new ProfilePageViewModel();
        }

        var settings = await _userNotificationSettingsService.GetCurrentAsync(user.Id, cancellationToken);
        var telegramChannelSettings = await _notificationSettingsService.GetTelegramChannelAsync(cancellationToken);
        var pendingCode = await _telegramLinkService.GetActiveCodeAsync(user.Id, cancellationToken);
        var telegramAccount = await _telegramLinkService.GetAccountStatusAsync(user.Id, cancellationToken);
        var telegramLinkingAvailable = telegramChannelSettings.TelegramEnabled && !string.IsNullOrWhiteSpace(telegramChannelSettings.TelegramBotToken);
        var telegramBotIdentifier = telegramLinkingAvailable
            ? await _telegramBotIdentityResolver.ResolveBotIdentifierAsync(telegramChannelSettings.TelegramBotToken!, cancellationToken)
            : null;

        var selectableTimeZones = _userTimeZoneService.GetSelectableTimeZoneOptions();
        var selectedDisplayTimeZoneId = source?.DisplayTimeZoneId ?? user.DisplayTimeZoneId ?? "UTC";
        if (!_userTimeZoneService.IsSupportedTimeZoneId(selectedDisplayTimeZoneId))
        {
            selectedDisplayTimeZoneId = "UTC";
        }

        return new ProfilePageViewModel
        {
            UserName = user.UserName ?? string.Empty,
            Email = source?.Email ?? user.Email ?? string.Empty,
            EmailVerified = user.EmailConfirmed,
            DisplayTimeZoneId = selectedDisplayTimeZoneId,
            AvailableDisplayTimeZoneOptions = selectableTimeZones
                .Select(option => new DisplayTimeZoneOptionViewModel
                {
                    Value = option.Value,
                    Label = option.Label
                })
                .ToList(),
            BrowserNotificationsEnabled = source?.BrowserNotificationsEnabled ?? settings.BrowserNotificationsEnabled,
            BrowserNotifyEndpointDown = source?.BrowserNotifyEndpointDown ?? settings.BrowserNotifyEndpointDown,
            BrowserNotifyEndpointRecovered = source?.BrowserNotifyEndpointRecovered ?? settings.BrowserNotifyEndpointRecovered,
            BrowserNotifyAgentOffline = source?.BrowserNotifyAgentOffline ?? settings.BrowserNotifyAgentOffline,
            BrowserNotifyAgentOnline = source?.BrowserNotifyAgentOnline ?? settings.BrowserNotifyAgentOnline,
            BrowserNotificationsPermissionState = source?.BrowserNotificationsPermissionState ?? settings.BrowserNotificationsPermissionState,
            SmtpNotificationsEnabled = source?.SmtpNotificationsEnabled ?? settings.SmtpNotificationsEnabled,
            SmtpNotifyEndpointDown = source?.SmtpNotifyEndpointDown ?? settings.SmtpNotifyEndpointDown,
            SmtpNotifyEndpointRecovered = source?.SmtpNotifyEndpointRecovered ?? settings.SmtpNotifyEndpointRecovered,
            SmtpNotifyAgentOffline = source?.SmtpNotifyAgentOffline ?? settings.SmtpNotifyAgentOffline,
            SmtpNotifyAgentOnline = source?.SmtpNotifyAgentOnline ?? settings.SmtpNotifyAgentOnline,
            TelegramNotificationsEnabled = source?.TelegramNotificationsEnabled ?? settings.TelegramNotificationsEnabled,
            TelegramNotifyEndpointDown = source?.TelegramNotifyEndpointDown ?? settings.TelegramNotifyEndpointDown,
            TelegramNotifyEndpointRecovered = source?.TelegramNotifyEndpointRecovered ?? settings.TelegramNotifyEndpointRecovered,
            TelegramNotifyAgentOffline = source?.TelegramNotifyAgentOffline ?? settings.TelegramNotifyAgentOffline,
            TelegramNotifyAgentOnline = source?.TelegramNotifyAgentOnline ?? settings.TelegramNotifyAgentOnline,
            QuietHoursEnabled = source?.QuietHoursEnabled ?? settings.QuietHoursEnabled,
            QuietHoursStartLocalTime = source?.QuietHoursStartLocalTime ?? settings.QuietHoursStartLocalTime,
            QuietHoursEndLocalTime = source?.QuietHoursEndLocalTime ?? settings.QuietHoursEndLocalTime,
            QuietHoursTimeZoneId = source?.QuietHoursTimeZoneId ?? settings.QuietHoursTimeZoneId,
            QuietHoursSuppressBrowserNotifications = source?.QuietHoursSuppressBrowserNotifications ?? settings.QuietHoursSuppressBrowserNotifications,
            QuietHoursSuppressSmtpNotifications = source?.QuietHoursSuppressSmtpNotifications ?? settings.QuietHoursSuppressSmtpNotifications,
            QuietHoursSuppressTelegramNotifications = source?.QuietHoursSuppressTelegramNotifications ?? settings.QuietHoursSuppressTelegramNotifications,
            NotificationSettingsUpdatedAtUtc = settings.UpdatedAtUtc,
            ActiveTelegramCode = pendingCode?.Code,
            ActiveTelegramCodeExpiresAtUtc = pendingCode?.ExpiresAtUtc,
            TelegramLinked = telegramAccount?.Verified ?? false,
            TelegramLinkedChatId = telegramAccount?.ChatId,
            TelegramLinkedUsername = telegramAccount?.Username,
            TelegramLinkedDisplayName = telegramAccount?.DisplayName,
            TelegramLinkedAtUtc = telegramAccount?.LinkedAtUtc,
            TelegramLinkingAvailable = telegramLinkingAvailable,
            TelegramBotIdentifier = telegramBotIdentifier
        };
    }

    private async Task<SmtpNotificationSendResult> SendVerificationEmailAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.Email) || !MailAddress.TryCreate(user.Email, out _))
        {
            return SmtpNotificationSendResult.Failed("A valid email address is required to send verification.");
        }

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var callbackUrl = Url.Action(
            action: "VerifyEmail",
            controller: "Account",
            values: new { userId = user.Id, code = encodedToken },
            protocol: Request.Scheme);

        if (string.IsNullOrWhiteSpace(callbackUrl))
        {
            return SmtpNotificationSendResult.Failed("Email verification link generation failed.");
        }

        var result = await _smtpNotificationSender.SendEmailVerificationAsync(user.Email, callbackUrl, cancellationToken);
        var eventType = result.Success ? "email_verification_email_sent" : "email_verification_email_send_failed";
        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            Category = EventCategory.Security,
            EventType = eventType,
            Severity = result.Success ? EventSeverity.Info : EventSeverity.Warning,
            Message = result.Success
                ? $"Verification email sent for user {user.Id}."
                : $"Verification email send failed for user {user.Id}.",
            DetailsJson = result.Success ? null : JsonSerializer.Serialize(new { reason = result.Message })
        }, cancellationToken);

        _logger.LogInformation(
            "Email verification resend requested by user {UserId}. Success={Success}",
            user.Id,
            result.Success);
        return result;
    }
}
