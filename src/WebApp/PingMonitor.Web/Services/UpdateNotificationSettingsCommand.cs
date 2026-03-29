namespace PingMonitor.Web.Services;

public sealed class UpdateNotificationSettingsCommand
{
    public bool BrowserNotificationsEnabled { get; set; }
    public bool BrowserNotifyEndpointDown { get; set; }
    public bool BrowserNotifyEndpointRecovered { get; set; }
    public bool BrowserNotifyAgentOffline { get; set; }
    public bool BrowserNotifyAgentOnline { get; set; }

    public string? BrowserNotificationsPermissionState { get; set; }

    public string? UpdatedByUserId { get; set; }
}
