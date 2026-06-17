using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;

namespace PingMonitor.Web.Services.Telegram;

public sealed record TelegramDirectMessageResult(bool Succeeded, string Message);

public interface ITelegramDirectMessageSender
{
    Task<TelegramDirectMessageResult> SendToUserAsync(string userId, string text, CancellationToken cancellationToken);
}

internal sealed class TelegramDirectMessageSender : ITelegramDirectMessageSender
{
    private const int TelegramMessageLimit = 3900;
    private readonly PingMonitorDbContext _dbContext;
    private readonly INotificationSettingsService _settingsService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TelegramDirectMessageSender> _logger;
    public TelegramDirectMessageSender(PingMonitorDbContext dbContext, INotificationSettingsService settingsService, IHttpClientFactory httpClientFactory, ILogger<TelegramDirectMessageSender> logger)
    { _dbContext = dbContext; _settingsService = settingsService; _httpClientFactory = httpClientFactory; _logger = logger; }

    public async Task<TelegramDirectMessageResult> SendToUserAsync(string userId, string text, CancellationToken cancellationToken)
    {
        var settings = await _settingsService.GetTelegramChannelAsync(cancellationToken);
        if (!settings.TelegramEnabled || string.IsNullOrWhiteSpace(settings.TelegramBotToken)) return new(false, "Telegram delivery is not configured.");
        var account = await _dbContext.TelegramAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.Verified && x.IsActive, cancellationToken);
        if (account is null) return new(false, "The task owner does not have a linked Telegram account.");
        var client = _httpClientFactory.CreateClient(nameof(TelegramDirectMessageSender));
        foreach (var part in Split(text))
        {
            var response = await client.PostAsync($"https://api.telegram.org/bot{settings.TelegramBotToken}/sendMessage", new FormUrlEncodedContent(new Dictionary<string, string> { ["chat_id"] = account.ChatId, ["text"] = part }), cancellationToken);
            if (!response.IsSuccessStatusCode) { _logger.LogWarning("Telegram scheduled AI delivery failed with status {StatusCode}.", (int)response.StatusCode); return new(false, "Telegram delivery failed."); }
        }
        return new(true, "Telegram delivery sent.");
    }
    private static IEnumerable<string> Split(string text) { for (var i = 0; i < text.Length; i += TelegramMessageLimit) yield return text.Substring(i, Math.Min(TelegramMessageLimit, text.Length - i)); }
}
