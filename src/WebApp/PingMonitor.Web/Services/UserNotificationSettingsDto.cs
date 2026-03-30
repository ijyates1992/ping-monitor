namespace PingMonitor.Web.Services;

public sealed class UserNotificationSettingsDto
{
    public required string UserId { get; init; }
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
    public bool TelegramNotificationsEnabled { get; set; }
    public bool TelegramNotifyEndpointDown { get; set; }
    public bool TelegramNotifyEndpointRecovered { get; set; }
    public bool TelegramNotifyAgentOffline { get; set; }
    public bool TelegramNotifyAgentOnline { get; set; }
    public bool QuietHoursEnabled { get; set; }
    public string QuietHoursStartLocalTime { get; set; } = "22:00";
    public string QuietHoursEndLocalTime { get; set; } = "07:00";
    public string QuietHoursTimeZoneId { get; set; } = "UTC";
    public bool QuietHoursSuppressBrowserNotifications { get; set; } = true;
    public bool QuietHoursSuppressSmtpNotifications { get; set; } = true;
    public bool QuietHoursSuppressTelegramNotifications { get; set; } = true;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
