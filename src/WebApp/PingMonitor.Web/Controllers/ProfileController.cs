using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Models.Identity;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Telegram;
using PingMonitor.Web.ViewModels.Profile;

namespace PingMonitor.Web.Controllers;

[Authorize]
[Route("profile")]
public sealed class ProfileController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserNotificationSettingsService _userNotificationSettingsService;
    private readonly ITelegramLinkService _telegramLinkService;

    public ProfileController(
        UserManager<ApplicationUser> userManager,
        IUserNotificationSettingsService userNotificationSettingsService,
        ITelegramLinkService telegramLinkService)
    {
        _userManager = userManager;
        _userNotificationSettingsService = userNotificationSettingsService;
        _telegramLinkService = telegramLinkService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = await BuildModelAsync(cancellationToken);
        return View("Index", model);
    }

    [HttpPost("account")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAccount([FromForm] ProfilePageViewModel model, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync();
        if (user is null)
        {
            return Challenge();
        }

        if (string.IsNullOrWhiteSpace(model.Email) || !MailAddress.TryCreate(model.Email, out _))
        {
            ModelState.AddModelError(nameof(ProfilePageViewModel.Email), "A valid email address is required.");
        }

        if (!ModelState.IsValid)
        {
            return View("Index", await BuildModelAsync(cancellationToken, model));
        }

        var trimmedEmail = model.Email.Trim();
        var setEmailResult = await _userManager.SetEmailAsync(user, trimmedEmail);
        if (!setEmailResult.Succeeded)
        {
            foreach (var error in setEmailResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View("Index", await BuildModelAsync(cancellationToken, model));
        }

        user.NormalizedEmail = trimmedEmail.ToUpperInvariant();
        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            foreach (var error in updateResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View("Index", await BuildModelAsync(cancellationToken, model));
        }

        var refreshed = await BuildModelAsync(cancellationToken);
        refreshed.AccountSaved = true;
        return View("Index", refreshed);
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
            return View("Index", await BuildModelAsync(cancellationToken, model));
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
        return View("Index", refreshed);
    }



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
        return View("Index", refreshed);
    }

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
        return View("Index", refreshed);
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
        var pendingCode = await _telegramLinkService.GetActiveCodeAsync(user.Id, cancellationToken);
        var telegramAccount = await _telegramLinkService.GetAccountStatusAsync(user.Id, cancellationToken);
        return new ProfilePageViewModel
        {
            UserName = user.UserName ?? string.Empty,
            Email = source?.Email ?? user.Email ?? string.Empty,
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
            TelegramLinked = telegramAccount is not null && telegramAccount.Verified,
            TelegramLinkedChatId = telegramAccount?.ChatId,
            TelegramLinkedUsername = telegramAccount?.Username,
            TelegramLinkedDisplayName = telegramAccount?.DisplayName,
            TelegramLinkedAtUtc = telegramAccount?.LinkedAtUtc
        };
    }
}
