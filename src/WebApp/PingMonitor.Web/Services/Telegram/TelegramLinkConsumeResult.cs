namespace PingMonitor.Web.Services.Telegram;

public sealed class TelegramLinkConsumeResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
