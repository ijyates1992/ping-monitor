namespace PingMonitor.Web.Services.Telegram;

public interface ITelegramBotIdentityResolver
{
    Task<string?> ResolveBotIdentifierAsync(string botToken, CancellationToken cancellationToken);
}
