namespace PingMonitor.Web.Models;

public sealed class NotificationSettings
{
    public const int SingletonId = 1;

    public int NotificationSettingsId { get; set; } = SingletonId;

    // Phase 1 uses explicit global settings to keep notification scope unambiguous.
    public bool BrowserNotificationsEnabled { get; set; }
    public bool BrowserNotifyEndpointDown { get; set; }
    public bool BrowserNotifyEndpointRecovered { get; set; }
    public bool BrowserNotifyAgentOffline { get; set; }
    public bool BrowserNotifyAgentOnline { get; set; }

    public string? BrowserNotificationsPermissionState { get; set; }

    public bool TelegramEnabled { get; set; }
    public string? TelegramBotTokenProtected { get; set; }
    public TelegramInboundMode TelegramInboundMode { get; set; } = TelegramInboundMode.Polling;
    public int TelegramPollIntervalSeconds { get; set; } = 10;
    public long TelegramLastProcessedUpdateId { get; set; }
    public string? TelegramWebhookUrl { get; set; }
    public string? TelegramWebhookSecretToken { get; set; }

    public bool QuietHoursEnabled { get; set; }
    public string QuietHoursStartLocalTime { get; set; } = "22:00";
    public string QuietHoursEndLocalTime { get; set; } = "07:00";
    public string QuietHoursTimeZoneId { get; set; } = "UTC";
    public bool QuietHoursSuppressBrowserNotifications { get; set; }
    public bool QuietHoursSuppressSmtpNotifications { get; set; }

    public bool SmtpNotificationsEnabled { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; }
    public bool SmtpUseTls { get; set; }
    public string? SmtpUsername { get; set; }
    public string? SmtpPasswordProtected { get; set; }
    public string? SmtpFromAddress { get; set; }
    public string? SmtpFromDisplayName { get; set; }
    public string? SmtpRecipientAddresses { get; set; }
    public bool SmtpNotifyEndpointDown { get; set; }
    public bool SmtpNotifyEndpointRecovered { get; set; }
    public bool SmtpNotifyAgentOffline { get; set; }
    public bool SmtpNotifyAgentOnline { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public string? UpdatedByUserId { get; set; }
}
