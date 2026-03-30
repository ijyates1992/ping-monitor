namespace PingMonitor.Web.Services;

public interface INotificationSuppressionService
{
    Task<NotificationSuppressionDecision> IsBrowserNotificationSuppressedAsync(CancellationToken cancellationToken);
    Task<NotificationSuppressionDecision> IsSmtpNotificationSuppressedAsync(CancellationToken cancellationToken);
    Task<NotificationSuppressionStatus> GetCurrentStatusAsync(CancellationToken cancellationToken);
}
