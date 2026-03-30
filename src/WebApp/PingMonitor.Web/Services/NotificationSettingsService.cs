using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using System.Security.Cryptography;
using System.Text;

namespace PingMonitor.Web.Services;

internal sealed class NotificationSettingsService : INotificationSettingsService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly ILogger<NotificationSettingsService> _logger;

    public NotificationSettingsService(PingMonitorDbContext dbContext, ILogger<NotificationSettingsService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<NotificationSettingsDto> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateEntityAsync(cancellationToken);
        return ToDto(settings);
    }

    public async Task<SmtpChannelSettingsDto> GetSmtpChannelAsync(CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateEntityAsync(cancellationToken);
        return new SmtpChannelSettingsDto
        {
            SmtpNotificationsEnabled = settings.SmtpNotificationsEnabled,
            SmtpHost = settings.SmtpHost,
            SmtpPort = settings.SmtpPort <= 0 ? 25 : settings.SmtpPort,
            SmtpUseTls = settings.SmtpUseTls,
            SmtpUsername = settings.SmtpUsername,
            SmtpPassword = UnprotectSecret(settings.SmtpPasswordProtected),
            SmtpFromAddress = settings.SmtpFromAddress,
            SmtpFromDisplayName = settings.SmtpFromDisplayName
        };
    }

    public async Task<TelegramChannelSettingsDto> GetTelegramChannelAsync(CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateEntityAsync(cancellationToken);
        return new TelegramChannelSettingsDto
        {
            TelegramEnabled = settings.TelegramEnabled,
            TelegramBotToken = UnprotectSecret(settings.TelegramBotTokenProtected),
            TelegramInboundMode = settings.TelegramInboundMode,
            TelegramPollIntervalSeconds = settings.TelegramPollIntervalSeconds <= 0 ? 10 : settings.TelegramPollIntervalSeconds,
            TelegramLastProcessedUpdateId = settings.TelegramLastProcessedUpdateId
        };
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
        settings.TelegramEnabled = command.TelegramEnabled;
        settings.TelegramInboundMode = command.TelegramInboundMode;
        settings.TelegramPollIntervalSeconds = command.TelegramPollIntervalSeconds <= 0 ? 10 : command.TelegramPollIntervalSeconds;
        settings.TelegramWebhookUrl = NormalizeString(command.TelegramWebhookUrl);
        if (command.TelegramLastProcessedUpdateId >= 0)
        {
            settings.TelegramLastProcessedUpdateId = command.TelegramLastProcessedUpdateId;
        }
        settings.TelegramWebhookSecretToken = NormalizeString(command.TelegramWebhookSecretToken);

        if (command.TelegramClearBotToken)
        {
            settings.TelegramBotTokenProtected = null;
        }
        else if (!string.IsNullOrWhiteSpace(command.TelegramBotToken))
        {
            settings.TelegramBotTokenProtected = ProtectSecret(command.TelegramBotToken.Trim(), "Telegram bot token");
        }

        settings.QuietHoursEnabled = command.QuietHoursEnabled;
        settings.QuietHoursStartLocalTime = NormalizeQuietHoursTime(command.QuietHoursStartLocalTime, fallback: "22:00");
        settings.QuietHoursEndLocalTime = NormalizeQuietHoursTime(command.QuietHoursEndLocalTime, fallback: "07:00");
        settings.QuietHoursTimeZoneId = NormalizeTimeZoneId(command.QuietHoursTimeZoneId);
        settings.QuietHoursSuppressBrowserNotifications = command.QuietHoursSuppressBrowserNotifications;
        settings.QuietHoursSuppressSmtpNotifications = command.QuietHoursSuppressSmtpNotifications;
        settings.SmtpNotificationsEnabled = command.SmtpNotificationsEnabled;
        settings.SmtpHost = NormalizeString(command.SmtpHost);
        settings.SmtpPort = command.SmtpPort <= 0 ? 25 : command.SmtpPort;
        settings.SmtpUseTls = command.SmtpUseTls;
        settings.SmtpUsername = NormalizeString(command.SmtpUsername);
        settings.SmtpFromAddress = NormalizeString(command.SmtpFromAddress);
        settings.SmtpFromDisplayName = NormalizeString(command.SmtpFromDisplayName);
        settings.SmtpRecipientAddresses = NormalizeRecipients(command.SmtpRecipientAddresses);
        settings.SmtpNotifyEndpointDown = command.SmtpNotifyEndpointDown;
        settings.SmtpNotifyEndpointRecovered = command.SmtpNotifyEndpointRecovered;
        settings.SmtpNotifyAgentOffline = command.SmtpNotifyAgentOffline;
        settings.SmtpNotifyAgentOnline = command.SmtpNotifyAgentOnline;

        if (command.SmtpClearPassword)
        {
            settings.SmtpPasswordProtected = null;
        }
        else if (!string.IsNullOrWhiteSpace(command.SmtpPassword))
        {
            settings.SmtpPasswordProtected = ProtectSecret(command.SmtpPassword.Trim(), "SMTP password");
        }

        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
        settings.UpdatedByUserId = string.IsNullOrWhiteSpace(command.UpdatedByUserId)
            ? null
            : command.UpdatedByUserId.Trim();

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Notification settings updated by {UpdatedByUserId}. BrowserEnabled={BrowserEnabled} SmtpEnabled={SmtpEnabled} QuietHoursEnabled={QuietHoursEnabled} QuietHours={QuietHoursStart}->{QuietHoursEnd} QuietHoursTimeZoneId={QuietHoursTimeZoneId}",
            settings.UpdatedByUserId ?? "(unknown)",
            settings.BrowserNotificationsEnabled,
            settings.SmtpNotificationsEnabled,
            settings.QuietHoursEnabled,
            settings.QuietHoursStartLocalTime,
            settings.QuietHoursEndLocalTime,
            settings.QuietHoursTimeZoneId);

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
            TelegramEnabled = false,
            TelegramInboundMode = TelegramInboundMode.Polling,
            TelegramPollIntervalSeconds = 10,
            TelegramLastProcessedUpdateId = 0,
            QuietHoursEnabled = false,
            QuietHoursStartLocalTime = "22:00",
            QuietHoursEndLocalTime = "07:00",
            QuietHoursTimeZoneId = "UTC",
            QuietHoursSuppressBrowserNotifications = true,
            QuietHoursSuppressSmtpNotifications = true,
            SmtpNotificationsEnabled = false,
            SmtpPort = 25,
            SmtpUseTls = true,
            SmtpNotifyEndpointDown = true,
            SmtpNotifyEndpointRecovered = true,
            SmtpNotifyAgentOffline = true,
            SmtpNotifyAgentOnline = true,
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

    private static string? NormalizeString(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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

    private static string? NormalizeRecipients(string? recipients)
    {
        if (string.IsNullOrWhiteSpace(recipients))
        {
            return null;
        }

        var split = recipients
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return split.Length == 0 ? null : string.Join(Environment.NewLine, split);
    }

    private string ProtectSecret(string secret, string secretName)
    {
        var payload = Encoding.UTF8.GetBytes(secret);
        if (OperatingSystem.IsWindows())
        {
            payload = ProtectedData.Protect(payload, optionalEntropy: null, DataProtectionScope.LocalMachine);
        }
        else
        {
            _logger.LogWarning("{SecretName} fallback storage is in use because DPAPI is only available on Windows.", secretName);
        }

        return Convert.ToBase64String(payload);
    }

    private string? UnprotectSecret(string? protectedSecret)
    {
        if (string.IsNullOrWhiteSpace(protectedSecret))
        {
            return null;
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(protectedSecret);
        }
        catch (FormatException)
        {
            _logger.LogWarning("Stored protected secret could not be decoded.");
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            payload = ProtectedData.Unprotect(payload, optionalEntropy: null, DataProtectionScope.LocalMachine);
        }

        return Encoding.UTF8.GetString(payload);
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
            TelegramEnabled = settings.TelegramEnabled,
            TelegramInboundMode = settings.TelegramInboundMode,
            TelegramPollIntervalSeconds = settings.TelegramPollIntervalSeconds <= 0 ? 10 : settings.TelegramPollIntervalSeconds,
            TelegramBotTokenConfigured = !string.IsNullOrWhiteSpace(settings.TelegramBotTokenProtected),
            QuietHoursEnabled = settings.QuietHoursEnabled,
            QuietHoursStartLocalTime = NormalizeQuietHoursTime(settings.QuietHoursStartLocalTime, "22:00"),
            QuietHoursEndLocalTime = NormalizeQuietHoursTime(settings.QuietHoursEndLocalTime, "07:00"),
            QuietHoursTimeZoneId = NormalizeTimeZoneId(settings.QuietHoursTimeZoneId),
            QuietHoursSuppressBrowserNotifications = settings.QuietHoursSuppressBrowserNotifications,
            QuietHoursSuppressSmtpNotifications = settings.QuietHoursSuppressSmtpNotifications,
            SmtpNotificationsEnabled = settings.SmtpNotificationsEnabled,
            SmtpHost = settings.SmtpHost,
            SmtpPort = settings.SmtpPort <= 0 ? 25 : settings.SmtpPort,
            SmtpUseTls = settings.SmtpUseTls,
            SmtpUsername = settings.SmtpUsername,
            SmtpPasswordConfigured = !string.IsNullOrWhiteSpace(settings.SmtpPasswordProtected),
            SmtpFromAddress = settings.SmtpFromAddress,
            SmtpFromDisplayName = settings.SmtpFromDisplayName,
            SmtpRecipientAddresses = settings.SmtpRecipientAddresses,
            SmtpNotifyEndpointDown = settings.SmtpNotifyEndpointDown,
            SmtpNotifyEndpointRecovered = settings.SmtpNotifyEndpointRecovered,
            SmtpNotifyAgentOffline = settings.SmtpNotifyAgentOffline,
            SmtpNotifyAgentOnline = settings.SmtpNotifyAgentOnline,
            UpdatedAtUtc = settings.UpdatedAtUtc,
            UpdatedByUserId = settings.UpdatedByUserId
        };
    }
}
