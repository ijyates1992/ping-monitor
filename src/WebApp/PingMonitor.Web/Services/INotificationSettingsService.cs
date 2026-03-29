namespace PingMonitor.Web.Services;

public interface INotificationSettingsService
{
    Task<NotificationSettingsDto> GetCurrentAsync(CancellationToken cancellationToken);

    Task<NotificationSettingsDto> UpdateAsync(UpdateNotificationSettingsCommand command, CancellationToken cancellationToken);
}
