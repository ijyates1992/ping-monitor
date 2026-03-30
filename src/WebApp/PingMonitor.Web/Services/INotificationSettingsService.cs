namespace PingMonitor.Web.Services;

public interface INotificationSettingsService
{
    Task<NotificationSettingsDto> GetCurrentAsync(CancellationToken cancellationToken);
    Task<SmtpChannelSettingsDto> GetSmtpChannelAsync(CancellationToken cancellationToken);
    Task<TelegramChannelSettingsDto> GetTelegramChannelAsync(CancellationToken cancellationToken);

    Task<NotificationSettingsDto> UpdateAsync(UpdateNotificationSettingsCommand command, CancellationToken cancellationToken);
}
