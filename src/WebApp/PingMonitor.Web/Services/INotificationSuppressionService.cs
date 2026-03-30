namespace PingMonitor.Web.Services;

public interface INotificationSuppressionService
{
    NotificationSuppressionDecision IsBrowserNotificationSuppressed(UserNotificationSettingsDto settings);
    NotificationSuppressionDecision IsSmtpNotificationSuppressed(UserNotificationSettingsDto settings);
    NotificationSuppressionStatus GetCurrentStatus(UserNotificationSettingsDto settings);
}
