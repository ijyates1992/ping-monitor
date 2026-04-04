using System.Text;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.Diagnostics;

namespace PingMonitor.Web.Services.Telegram;

internal sealed class TelegramNotificationSender : ITelegramNotificationSender
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly INotificationSettingsService _notificationSettingsService;
    private readonly IUserNotificationSettingsService _userNotificationSettingsService;
    private readonly INotificationSuppressionService _notificationSuppressionService;
    private readonly ILogger<TelegramNotificationSender> _logger;
    private readonly IDbActivityScope _dbActivityScope;
    private readonly HttpClient _httpClient = new();

    public TelegramNotificationSender(
        PingMonitorDbContext dbContext,
        INotificationSettingsService notificationSettingsService,
        IUserNotificationSettingsService userNotificationSettingsService,
        INotificationSuppressionService notificationSuppressionService,
        IDbActivityScope dbActivityScope,
        ILogger<TelegramNotificationSender> logger)
    {
        _dbContext = dbContext;
        _notificationSettingsService = notificationSettingsService;
        _userNotificationSettingsService = userNotificationSettingsService;
        _notificationSuppressionService = notificationSuppressionService;
        _dbActivityScope = dbActivityScope;
        _logger = logger;
    }

    public async Task<TelegramNotificationSendResult> SendForEventAsync(EventLog eventLog, CancellationToken cancellationToken)
    {
        using var scope = _dbActivityScope.BeginScope("Notifications.Telegram");
        var mapped = MapEvent(eventLog);
        if (mapped is null)
        {
            return TelegramNotificationSendResult.Skip("Event type is not supported for Telegram notifications.");
        }

        var channel = await _notificationSettingsService.GetTelegramChannelAsync(cancellationToken);
        if (!channel.TelegramEnabled)
        {
            return TelegramNotificationSendResult.Skip("Telegram notifications are disabled globally.");
        }

        if (string.IsNullOrWhiteSpace(channel.TelegramBotToken))
        {
            return TelegramNotificationSendResult.Skip("Telegram bot token is not configured.");
        }

        var accounts = await _dbContext.TelegramAccounts.AsNoTracking().Where(x => x.Verified && x.IsActive).ToArrayAsync(cancellationToken);
        var eligibleChatIds = new List<string>();

        foreach (var account in accounts)
        {
            var settings = await _userNotificationSettingsService.GetCurrentAsync(account.UserId, cancellationToken);
            if (!settings.TelegramNotificationsEnabled || !IsEventEnabled(mapped.Value.Toggle, settings))
            {
                continue;
            }

            if (_notificationSuppressionService.IsTelegramNotificationSuppressed(settings).IsSuppressed)
            {
                continue;
            }

            eligibleChatIds.Add(account.ChatId);
        }

        if (eligibleChatIds.Count == 0)
        {
            return TelegramNotificationSendResult.Skip("No users are eligible for Telegram delivery for this event.");
        }

        var text = mapped.Value.Title + " - " + eventLog.Message;
        foreach (var chatId in eligibleChatIds)
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["chat_id"] = chatId,
                ["text"] = text
            });

            var url = $"https://api.telegram.org/bot{channel.TelegramBotToken}/sendMessage";
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Telegram send failed for chat {ChatId} with status {StatusCode}.", chatId, (int)response.StatusCode);
            }
        }

        return TelegramNotificationSendResult.Sent("Telegram notifications processed.");
    }

    private static bool IsEventEnabled(TelegramToggle toggle, UserNotificationSettingsDto settings)
        => toggle switch
        {
            TelegramToggle.EndpointDown => settings.TelegramNotifyEndpointDown,
            TelegramToggle.EndpointRecovered => settings.TelegramNotifyEndpointRecovered,
            TelegramToggle.AgentOffline => settings.TelegramNotifyAgentOffline,
            TelegramToggle.AgentOnline => settings.TelegramNotifyAgentOnline,
            _ => false
        };

    private static MappedEvent? MapEvent(EventLog eventLog)
    {
        if (eventLog.EventType == EventType.EndpointStateChanged)
        {
            if (eventLog.Message.Contains("went down.", StringComparison.Ordinal))
            {
                return new MappedEvent(TelegramToggle.EndpointDown, "Ping Monitor: Endpoint Down");
            }

            if (eventLog.Message.Contains("recovered", StringComparison.Ordinal))
            {
                return new MappedEvent(TelegramToggle.EndpointRecovered, "Ping Monitor: Endpoint Recovered");
            }
        }

        if (eventLog.EventType == EventType.AgentBecameOffline)
        {
            return new MappedEvent(TelegramToggle.AgentOffline, "Ping Monitor: Agent Offline");
        }

        if (eventLog.EventType == EventType.AgentBecameOnline)
        {
            return new MappedEvent(TelegramToggle.AgentOnline, "Ping Monitor: Agent Online");
        }

        return null;
    }

    private enum TelegramToggle
    {
        EndpointDown,
        EndpointRecovered,
        AgentOffline,
        AgentOnline
    }

    private readonly record struct MappedEvent(TelegramToggle Toggle, string Title);
}
