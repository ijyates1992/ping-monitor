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

        var lastProcessedUpdateId = settings.TelegramLastProcessedUpdateId;
        var offset = lastProcessedUpdateId + 1;
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

        var seenUpdateIds = new HashSet<long>();
        var updates = result.EnumerateArray()
            .OrderBy(item => item.TryGetProperty("update_id", out var updateIdElement) ? updateIdElement.GetInt64() : long.MaxValue)
            .ToList();

        foreach (var item in updates)
        {
            if (!item.TryGetProperty("update_id", out var updateIdElement))
            {
                _logger.LogWarning("Telegram update did not contain update_id and was skipped.");
                continue;
            }

            var updateId = updateIdElement.GetInt64();
            if (updateId <= lastProcessedUpdateId)
            {
                continue;
            }

            if (!seenUpdateIds.Add(updateId))
            {
                _logger.LogWarning("Duplicate Telegram update {UpdateId} encountered in same poll cycle and skipped.", updateId);
                continue;
            }

            var handledSuccessfully = await ProcessUpdateAsync(settings.TelegramBotToken, updateId, item, cancellationToken);
            if (!handledSuccessfully)
            {
                _logger.LogWarning("Telegram update {UpdateId} failed processing and will be retried in a future poll cycle.", updateId);
                continue;
            }

            await _notificationSettingsService.AdvanceTelegramLastProcessedUpdateIdAsync(updateId, cancellationToken);
            lastProcessedUpdateId = updateId;
        }
    }

    private async Task<bool> ProcessUpdateAsync(string botToken, long updateId, JsonElement item, CancellationToken cancellationToken)
    {
        try
        {
            if (!item.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            {
                _logger.LogDebug("Telegram update {UpdateId} contained no message payload and was acknowledged.", updateId);
                return true;
            }

            if (!message.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var chatIdElement))
            {
                _logger.LogWarning("Telegram update {UpdateId} had message payload without chat id and was acknowledged.", updateId);
                return true;
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
                _logger.LogInformation(
                    "Telegram inbound message processed. UpdateId={UpdateId} Success={Success} Status={Status} Detail={Detail}",
                    updateId,
                    processingResult.Success,
                    processingResult.Status,
                    processingResult.Message);
            }

            if (processingResult.ShouldReply && !string.IsNullOrWhiteSpace(processingResult.ReplyText))
            {
                await SendReplyAsync(botToken, chatId, updateId, processingResult.ReplyText, cancellationToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram update {UpdateId} processing failed.", updateId);
            return false;
        }
    }

    private async Task SendReplyAsync(string botToken, string chatId, long updateId, string text, CancellationToken cancellationToken)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["chat_id"] = chatId,
            ["text"] = text
        });

        var url = $"https://api.telegram.org/bot{botToken}/sendMessage";

        try
        {
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Telegram verification reply failed for update {UpdateId} chat {ChatId} with status {StatusCode}.",
                    updateId,
                    chatId,
                    (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram verification reply request failed for update {UpdateId} chat {ChatId}.", updateId, chatId);
        }
    }
}
