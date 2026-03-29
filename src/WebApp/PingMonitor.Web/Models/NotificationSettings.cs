namespace PingMonitor.Web.Models;

public sealed class NotificationSettings
{
    public const int SingletonId = 1;

    public int NotificationSettingsId { get; set; } = SingletonId;

    // Phase 1 uses explicit global settings to keep notification scope unambiguous.
    public bool BrowserNotificationsEnabled { get; set; }

    public string? BrowserNotificationsPermissionState { get; set; }

    public bool TelegramNotificationsEnabled { get; set; }

    public bool SmtpNotificationsEnabled { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public string? UpdatedByUserId { get; set; }
}
