using System.Text.Json;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.Telegram;

internal sealed class TelegramPollingService : ITelegramPollingService
{
    private readonly INotificationSettingsService _notificationSettingsService;
    private readonly ITelegramMessageProcessor _telegramMessageProcessor;
    private readonly ILogger<TelegramPollingService> _logger;
    private readonly HttpClient _httpClient = new();

    public TelegramPollingService(
        INotificationSettingsService notificationSettingsService,
        ITelegramMessageProcessor telegramMessageProcessor,
        ILogger<TelegramPollingService> logger)
    {
        _notificationSettingsService = notificationSettingsService;
        _telegramMessageProcessor = telegramMessageProcessor;
        _logger = logger;
    }

    public async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var settings = await _notificationSettingsService.GetTelegramChannelAsync(cancellationToken);
        if (!settings.TelegramEnabled || settings.TelegramInboundMode != TelegramInboundMode.Polling || string.IsNullOrWhiteSpace(settings.TelegramBotToken))
        {
            return;
        }

        var offset = settings.TelegramLastProcessedUpdateId + 1;
        var url = $"https://api.telegram.org/bot{settings.TelegramBotToken}/getUpdates?timeout=25&offset={offset}";

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(url, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram polling request failed.");
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Telegram polling returned non-success status code {StatusCode}.", (int)response.StatusCode);
            return;
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        long maxUpdateId = settings.TelegramLastProcessedUpdateId;
        foreach (var item in result.EnumerateArray())
        {
            var updateId = item.GetProperty("update_id").GetInt64();
            if (updateId > maxUpdateId)
            {
                maxUpdateId = updateId;
            }

            if (!item.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!message.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var chatIdElement))
            {
                continue;
            }

            var chatId = chatIdElement.ToString();
            var chatType = chat.TryGetProperty("type", out var chatTypeElement) ? chatTypeElement.GetString() ?? string.Empty : string.Empty;
            var text = message.TryGetProperty("text", out var textElement) ? textElement.GetString() ?? string.Empty : string.Empty;
            var from = message.TryGetProperty("from", out var fromElement) ? fromElement : default;
            var username = from.ValueKind == JsonValueKind.Object && from.TryGetProperty("username", out var usernameElement) ? usernameElement.GetString() : null;
            var displayName = from.ValueKind == JsonValueKind.Object && from.TryGetProperty("first_name", out var firstNameElement) ? firstNameElement.GetString() : null;

            var processingResult = await _telegramMessageProcessor.ProcessAsync(new TelegramInboundMessage
            {
                UpdateId = updateId,
                ChatId = chatId,
                ChatType = chatType,
                Text = text,
                Username = username,
                DisplayName = displayName
            }, cancellationToken);

            if (processingResult.Handled)
            {
                _logger.LogInformation("Telegram inbound message processed. Success={Success} Detail={Detail}", processingResult.Success, processingResult.Message);
            }
        }

        if (maxUpdateId > settings.TelegramLastProcessedUpdateId)
        {
            var current = await _notificationSettingsService.GetCurrentAsync(cancellationToken);
            await _notificationSettingsService.UpdateAsync(new UpdateNotificationSettingsCommand
            {
                BrowserNotificationsEnabled = current.BrowserNotificationsEnabled,
                BrowserNotifyEndpointDown = current.BrowserNotifyEndpointDown,
                BrowserNotifyEndpointRecovered = current.BrowserNotifyEndpointRecovered,
                BrowserNotifyAgentOffline = current.BrowserNotifyAgentOffline,
                BrowserNotifyAgentOnline = current.BrowserNotifyAgentOnline,
                BrowserNotificationsPermissionState = current.BrowserNotificationsPermissionState,
                TelegramEnabled = current.TelegramEnabled,
                TelegramInboundMode = current.TelegramInboundMode,
                TelegramPollIntervalSeconds = current.TelegramPollIntervalSeconds,
                TelegramLastProcessedUpdateId = maxUpdateId,
                QuietHoursEnabled = current.QuietHoursEnabled,
                QuietHoursStartLocalTime = current.QuietHoursStartLocalTime,
                QuietHoursEndLocalTime = current.QuietHoursEndLocalTime,
                QuietHoursTimeZoneId = current.QuietHoursTimeZoneId,
                QuietHoursSuppressBrowserNotifications = current.QuietHoursSuppressBrowserNotifications,
                QuietHoursSuppressSmtpNotifications = current.QuietHoursSuppressSmtpNotifications,
                SmtpNotificationsEnabled = current.SmtpNotificationsEnabled,
                SmtpHost = current.SmtpHost,
                SmtpPort = current.SmtpPort,
                SmtpUseTls = current.SmtpUseTls,
                SmtpUsername = current.SmtpUsername,
                SmtpFromAddress = current.SmtpFromAddress,
                SmtpFromDisplayName = current.SmtpFromDisplayName,
                SmtpRecipientAddresses = current.SmtpRecipientAddresses,
                SmtpNotifyEndpointDown = current.SmtpNotifyEndpointDown,
                SmtpNotifyEndpointRecovered = current.SmtpNotifyEndpointRecovered,
                SmtpNotifyAgentOffline = current.SmtpNotifyAgentOffline,
                SmtpNotifyAgentOnline = current.SmtpNotifyAgentOnline,
                UpdatedByUserId = current.UpdatedByUserId
            }, cancellationToken);
        }
    }
}
