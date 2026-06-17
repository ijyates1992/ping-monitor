using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;

namespace PingMonitor.Web.Services.Telegram;

public sealed record TelegramDirectMessageResult(bool Succeeded, string Message);

public interface ITelegramDirectMessageSender
{
    Task<TelegramDirectMessageResult> SendToUserAsync(string userId, string text, CancellationToken cancellationToken, TelegramMessageFormat format = TelegramMessageFormat.PlainText);
}

internal sealed class TelegramDirectMessageSender : ITelegramDirectMessageSender
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly INotificationSettingsService _settingsService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TelegramDirectMessageSender> _logger;
    public TelegramDirectMessageSender(PingMonitorDbContext dbContext, INotificationSettingsService settingsService, IHttpClientFactory httpClientFactory, ILogger<TelegramDirectMessageSender> logger)
    { _dbContext = dbContext; _settingsService = settingsService; _httpClientFactory = httpClientFactory; _logger = logger; }

    public async Task<TelegramDirectMessageResult> SendToUserAsync(string userId, string text, CancellationToken cancellationToken, TelegramMessageFormat format = TelegramMessageFormat.PlainText)
    {
        var settings = await _settingsService.GetTelegramChannelAsync(cancellationToken);
        if (!settings.TelegramEnabled || string.IsNullOrWhiteSpace(settings.TelegramBotToken)) return new(false, "Telegram delivery is not configured.");
        var account = await _dbContext.TelegramAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.Verified && x.IsActive, cancellationToken);
        if (account is null) return new(false, "The task owner does not have a linked Telegram account.");
        var client = _httpClientFactory.CreateClient(nameof(TelegramDirectMessageSender));
        foreach (var part in TelegramAiMarkdownFormatter.BuildMessages(text, format))
        {
            if (!await SendPartAsync(client, settings.TelegramBotToken, account.ChatId, part, cancellationToken)) return new(false, "Telegram delivery failed.");
        }
        return new(true, "Telegram delivery sent.");
    }

    private async Task<bool> SendPartAsync(HttpClient client, string botToken, string chatId, TelegramOutgoingMessage part, CancellationToken cancellationToken)
    {
        var response = await client.PostAsync($"https://api.telegram.org/bot{botToken}/sendMessage", BuildContent(chatId, part), cancellationToken);
        if (response.IsSuccessStatusCode) return true;
        if (part.Format == TelegramMessageFormat.Html)
        {
            _logger.LogWarning("Telegram AI formatted delivery failed with status {StatusCode}; retrying the chunk as plain text.", (int)response.StatusCode);
            response = await client.PostAsync($"https://api.telegram.org/bot{botToken}/sendMessage", BuildContent(chatId, new TelegramOutgoingMessage(part.Text, TelegramMessageFormat.PlainText)), cancellationToken);
            if (response.IsSuccessStatusCode) return true;
        }
        _logger.LogWarning("Telegram direct delivery failed with status {StatusCode}.", (int)response.StatusCode);
        return false;
    }

    private static FormUrlEncodedContent BuildContent(string chatId, TelegramOutgoingMessage part)
    {
        var values = new Dictionary<string, string> { ["chat_id"] = chatId, ["text"] = part.Text };
        if (part.Format == TelegramMessageFormat.Html) values["parse_mode"] = "HTML";
        return new FormUrlEncodedContent(values);
    }
}
