namespace PingMonitor.Web.Services.Telegram;

public sealed class TelegramAccountStatusDto
{
    public bool Verified { get; init; }
    public string ChatId { get; init; } = string.Empty;
    public string? Username { get; init; }
    public string? DisplayName { get; init; }
    public DateTimeOffset LinkedAtUtc { get; init; }
}
