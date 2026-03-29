namespace PingMonitor.Web.Services;

public sealed class UpdateNotificationSettingsCommand
{
    public bool BrowserNotificationsEnabled { get; set; }

    public string? BrowserNotificationsPermissionState { get; set; }

    public string? UpdatedByUserId { get; set; }
}
