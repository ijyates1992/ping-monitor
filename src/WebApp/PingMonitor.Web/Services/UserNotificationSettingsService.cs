using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services;

internal sealed class UserNotificationSettingsService : IUserNotificationSettingsService
{
    private readonly PingMonitorDbContext _dbContext;

    public UserNotificationSettingsService(PingMonitorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UserNotificationSettingsDto> GetCurrentAsync(string userId, CancellationToken cancellationToken)
    {
        var row = await GetOrCreateEntityAsync(userId, cancellationToken);
        return ToDto(row);
    }

    public async Task<UserNotificationSettingsDto> UpdateAsync(UpdateUserNotificationSettingsCommand command, CancellationToken cancellationToken)
    {
        var row = await GetOrCreateEntityAsync(command.UserId, cancellationToken);
        row.BrowserNotificationsEnabled = command.BrowserNotificationsEnabled;
        row.BrowserNotifyEndpointDown = command.BrowserNotifyEndpointDown;
        row.BrowserNotifyEndpointRecovered = command.BrowserNotifyEndpointRecovered;
        row.BrowserNotifyAgentOffline = command.BrowserNotifyAgentOffline;
        row.BrowserNotifyAgentOnline = command.BrowserNotifyAgentOnline;
        row.BrowserNotificationsPermissionState = NormalizePermissionState(command.BrowserNotificationsPermissionState);
        row.SmtpNotificationsEnabled = command.SmtpNotificationsEnabled;
        row.SmtpNotifyEndpointDown = command.SmtpNotifyEndpointDown;
        row.SmtpNotifyEndpointRecovered = command.SmtpNotifyEndpointRecovered;
        row.SmtpNotifyAgentOffline = command.SmtpNotifyAgentOffline;
        row.SmtpNotifyAgentOnline = command.SmtpNotifyAgentOnline;
        row.QuietHoursEnabled = command.QuietHoursEnabled;
        row.QuietHoursStartLocalTime = NormalizeQuietHoursTime(command.QuietHoursStartLocalTime, "22:00");
        row.QuietHoursEndLocalTime = NormalizeQuietHoursTime(command.QuietHoursEndLocalTime, "07:00");
        row.QuietHoursTimeZoneId = NormalizeTimeZoneId(command.QuietHoursTimeZoneId);
        row.QuietHoursSuppressBrowserNotifications = command.QuietHoursSuppressBrowserNotifications;
        row.QuietHoursSuppressSmtpNotifications = command.QuietHoursSuppressSmtpNotifications;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(row);
    }

    private async Task<UserNotificationSettings> GetOrCreateEntityAsync(string userId, CancellationToken cancellationToken)
    {
        var normalizedUserId = userId.Trim();
        var row = await _dbContext.UserNotificationSettings.SingleOrDefaultAsync(x => x.UserId == normalizedUserId, cancellationToken);
        if (row is not null)
        {
            return row;
        }

        var global = await _dbContext.NotificationSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.NotificationSettingsId == NotificationSettings.SingletonId, cancellationToken);

        row = new UserNotificationSettings
        {
            UserId = normalizedUserId,
            BrowserNotificationsEnabled = global?.BrowserNotificationsEnabled ?? false,
            BrowserNotifyEndpointDown = global?.BrowserNotifyEndpointDown ?? true,
            BrowserNotifyEndpointRecovered = global?.BrowserNotifyEndpointRecovered ?? true,
            BrowserNotifyAgentOffline = global?.BrowserNotifyAgentOffline ?? true,
            BrowserNotifyAgentOnline = global?.BrowserNotifyAgentOnline ?? true,
            BrowserNotificationsPermissionState = NormalizePermissionState(global?.BrowserNotificationsPermissionState),
            SmtpNotificationsEnabled = global?.SmtpNotificationsEnabled ?? false,
            SmtpNotifyEndpointDown = global?.SmtpNotifyEndpointDown ?? true,
            SmtpNotifyEndpointRecovered = global?.SmtpNotifyEndpointRecovered ?? true,
            SmtpNotifyAgentOffline = global?.SmtpNotifyAgentOffline ?? true,
            SmtpNotifyAgentOnline = global?.SmtpNotifyAgentOnline ?? true,
            QuietHoursEnabled = global?.QuietHoursEnabled ?? false,
            QuietHoursStartLocalTime = NormalizeQuietHoursTime(global?.QuietHoursStartLocalTime, "22:00"),
            QuietHoursEndLocalTime = NormalizeQuietHoursTime(global?.QuietHoursEndLocalTime, "07:00"),
            QuietHoursTimeZoneId = NormalizeTimeZoneId(global?.QuietHoursTimeZoneId),
            QuietHoursSuppressBrowserNotifications = global?.QuietHoursSuppressBrowserNotifications ?? true,
            QuietHoursSuppressSmtpNotifications = global?.QuietHoursSuppressSmtpNotifications ?? true,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        _dbContext.UserNotificationSettings.Add(row);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return row;
    }

    private static UserNotificationSettingsDto ToDto(UserNotificationSettings row) => new()
    {
        UserId = row.UserId,
        BrowserNotificationsEnabled = row.BrowserNotificationsEnabled,
        BrowserNotifyEndpointDown = row.BrowserNotifyEndpointDown,
        BrowserNotifyEndpointRecovered = row.BrowserNotifyEndpointRecovered,
        BrowserNotifyAgentOffline = row.BrowserNotifyAgentOffline,
        BrowserNotifyAgentOnline = row.BrowserNotifyAgentOnline,
        BrowserNotificationsPermissionState = NormalizePermissionState(row.BrowserNotificationsPermissionState),
        SmtpNotificationsEnabled = row.SmtpNotificationsEnabled,
        SmtpNotifyEndpointDown = row.SmtpNotifyEndpointDown,
        SmtpNotifyEndpointRecovered = row.SmtpNotifyEndpointRecovered,
        SmtpNotifyAgentOffline = row.SmtpNotifyAgentOffline,
        SmtpNotifyAgentOnline = row.SmtpNotifyAgentOnline,
        QuietHoursEnabled = row.QuietHoursEnabled,
        QuietHoursStartLocalTime = NormalizeQuietHoursTime(row.QuietHoursStartLocalTime, "22:00"),
        QuietHoursEndLocalTime = NormalizeQuietHoursTime(row.QuietHoursEndLocalTime, "07:00"),
        QuietHoursTimeZoneId = NormalizeTimeZoneId(row.QuietHoursTimeZoneId),
        QuietHoursSuppressBrowserNotifications = row.QuietHoursSuppressBrowserNotifications,
        QuietHoursSuppressSmtpNotifications = row.QuietHoursSuppressSmtpNotifications,
        UpdatedAtUtc = row.UpdatedAtUtc
    };

    private static string NormalizePermissionState(string? permissionState)
    {
        var normalized = permissionState?.Trim().ToLowerInvariant();
        return normalized is "default" or "granted" or "denied" ? normalized : "default";
    }

    private static string NormalizeQuietHoursTime(string? value, string fallback)
    {
        if (TimeOnly.TryParseExact(value?.Trim(), "HH:mm", out var parsed))
        {
            return parsed.ToString("HH:mm");
        }

        return fallback;
    }

    private static string NormalizeTimeZoneId(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "UTC" : value.Trim();
    }
}
