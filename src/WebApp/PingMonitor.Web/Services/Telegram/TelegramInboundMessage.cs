namespace PingMonitor.Web.Services.Telegram;

public sealed class TelegramInboundMessage
{
    public long UpdateId { get; init; }
    public string ChatId { get; init; } = string.Empty;
    public string ChatType { get; init; } = string.Empty;
    public string? Username { get; init; }
    public string? DisplayName { get; init; }
    public string Text { get; init; } = string.Empty;
}
