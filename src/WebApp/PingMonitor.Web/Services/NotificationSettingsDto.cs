namespace PingMonitor.Web.Services;

public sealed class NotificationSettingsDto
{
    public bool BrowserNotificationsEnabled { get; set; }
    public bool BrowserNotifyEndpointDown { get; set; }
    public bool BrowserNotifyEndpointRecovered { get; set; }
    public bool BrowserNotifyAgentOffline { get; set; }
    public bool BrowserNotifyAgentOnline { get; set; }

    public string? BrowserNotificationsPermissionState { get; set; }

    public bool TelegramNotificationsEnabled { get; set; }

    public bool SmtpNotificationsEnabled { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; }
    public bool SmtpUseTls { get; set; }
    public string? SmtpUsername { get; set; }
    public bool SmtpPasswordConfigured { get; set; }
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
