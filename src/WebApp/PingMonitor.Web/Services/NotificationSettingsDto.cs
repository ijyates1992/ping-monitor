namespace PingMonitor.Web.Services;

public sealed class NotificationSettingsDto
{
    public bool BrowserNotificationsEnabled { get; set; }

    public string? BrowserNotificationsPermissionState { get; set; }

    public bool TelegramNotificationsEnabled { get; set; }

    public bool SmtpNotificationsEnabled { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public string? UpdatedByUserId { get; set; }
}
