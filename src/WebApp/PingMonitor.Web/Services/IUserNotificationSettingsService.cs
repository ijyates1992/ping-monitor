namespace PingMonitor.Web.Services;

public interface IUserNotificationSettingsService
{
    Task<UserNotificationSettingsDto> GetCurrentAsync(string userId, CancellationToken cancellationToken);
    Task<UserNotificationSettingsDto> UpdateAsync(UpdateUserNotificationSettingsCommand command, CancellationToken cancellationToken);
}
