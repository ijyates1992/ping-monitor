using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services;

internal sealed class NotificationSettingsService : INotificationSettingsService
{
    private readonly PingMonitorDbContext _dbContext;

    public NotificationSettingsService(PingMonitorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<NotificationSettingsDto> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateEntityAsync(cancellationToken);
        return ToDto(settings);
    }

    public async Task<NotificationSettingsDto> UpdateAsync(UpdateNotificationSettingsCommand command, CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateEntityAsync(cancellationToken);

        settings.BrowserNotificationsEnabled = command.BrowserNotificationsEnabled;
        settings.BrowserNotifyEndpointDown = command.BrowserNotifyEndpointDown;
        settings.BrowserNotifyEndpointRecovered = command.BrowserNotifyEndpointRecovered;
        settings.BrowserNotifyAgentOffline = command.BrowserNotifyAgentOffline;
        settings.BrowserNotifyAgentOnline = command.BrowserNotifyAgentOnline;
        settings.BrowserNotificationsPermissionState = NormalizePermissionState(command.BrowserNotificationsPermissionState);
        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
        settings.UpdatedByUserId = string.IsNullOrWhiteSpace(command.UpdatedByUserId)
            ? null
            : command.UpdatedByUserId.Trim();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(settings);
    }

    private async Task<NotificationSettings> GetOrCreateEntityAsync(CancellationToken cancellationToken)
    {
        var settings = await _dbContext.NotificationSettings
            .SingleOrDefaultAsync(x => x.NotificationSettingsId == NotificationSettings.SingletonId, cancellationToken);

        if (settings is not null)
        {
            return settings;
        }

        settings = new NotificationSettings
        {
            NotificationSettingsId = NotificationSettings.SingletonId,
            BrowserNotificationsEnabled = false,
            BrowserNotifyEndpointDown = true,
            BrowserNotifyEndpointRecovered = true,
            BrowserNotifyAgentOffline = true,
            BrowserNotifyAgentOnline = true,
            BrowserNotificationsPermissionState = "default",
            TelegramNotificationsEnabled = false,
            SmtpNotificationsEnabled = false,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        _dbContext.NotificationSettings.Add(settings);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private static string NormalizePermissionState(string? permissionState)
    {
        var normalized = permissionState?.Trim().ToLowerInvariant();
        return normalized is "default" or "granted" or "denied"
            ? normalized
            : "default";
    }

    private static NotificationSettingsDto ToDto(NotificationSettings settings)
    {
        return new NotificationSettingsDto
        {
            BrowserNotificationsEnabled = settings.BrowserNotificationsEnabled,
            BrowserNotifyEndpointDown = settings.BrowserNotifyEndpointDown,
            BrowserNotifyEndpointRecovered = settings.BrowserNotifyEndpointRecovered,
            BrowserNotifyAgentOffline = settings.BrowserNotifyAgentOffline,
            BrowserNotifyAgentOnline = settings.BrowserNotifyAgentOnline,
            BrowserNotificationsPermissionState = settings.BrowserNotificationsPermissionState,
            TelegramNotificationsEnabled = settings.TelegramNotificationsEnabled,
            SmtpNotificationsEnabled = settings.SmtpNotificationsEnabled,
            UpdatedAtUtc = settings.UpdatedAtUtc,
            UpdatedByUserId = settings.UpdatedByUserId
        };
    }
}
