using System.ComponentModel.DataAnnotations;

namespace PingMonitor.Web.ViewModels.Profile;

public sealed class ProfilePageViewModel
{
    public string UserName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = string.Empty;
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;
    [DataType(DataType.Password)]
    public string ConfirmNewPassword { get; set; } = string.Empty;

    public bool BrowserNotificationsEnabled { get; set; }
    public bool BrowserNotifyEndpointDown { get; set; }
    public bool BrowserNotifyEndpointRecovered { get; set; }
    public bool BrowserNotifyAgentOffline { get; set; }
    public bool BrowserNotifyAgentOnline { get; set; }
    public string BrowserNotificationsPermissionState { get; set; } = "default";
    public bool SmtpNotificationsEnabled { get; set; }
    public bool SmtpNotifyEndpointDown { get; set; }
    public bool SmtpNotifyEndpointRecovered { get; set; }
    public bool SmtpNotifyAgentOffline { get; set; }
    public bool SmtpNotifyAgentOnline { get; set; }
    public bool QuietHoursEnabled { get; set; }
    public string QuietHoursStartLocalTime { get; set; } = "22:00";
    public string QuietHoursEndLocalTime { get; set; } = "07:00";
    public string QuietHoursTimeZoneId { get; set; } = "UTC";
    public bool QuietHoursSuppressBrowserNotifications { get; set; }
    public bool QuietHoursSuppressSmtpNotifications { get; set; }
    public DateTimeOffset NotificationSettingsUpdatedAtUtc { get; set; }
    public bool AccountSaved { get; set; }
    public bool PasswordSaved { get; set; }
    public bool NotificationsSaved { get; set; }
}
