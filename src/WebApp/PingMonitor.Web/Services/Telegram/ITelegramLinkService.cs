namespace PingMonitor.Web.Services.Telegram;

public interface ITelegramLinkService
{
    Task<PendingTelegramLinkDto> GenerateCodeAsync(string userId, CancellationToken cancellationToken);
    Task<PendingTelegramLinkDto?> GetActiveCodeAsync(string userId, CancellationToken cancellationToken);
    Task<TelegramLinkConsumeResult> ConsumeCodeAsync(string code, string chatId, string chatType, string? username, string? displayName, CancellationToken cancellationToken);
    Task<TelegramAccountStatusDto?> GetAccountStatusAsync(string userId, CancellationToken cancellationToken);
    Task<bool> UnlinkAccountAsync(string userId, CancellationToken cancellationToken);
}
