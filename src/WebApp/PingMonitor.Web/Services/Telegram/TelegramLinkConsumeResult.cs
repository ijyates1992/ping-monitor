namespace PingMonitor.Web.Services.Telegram;

public sealed class TelegramLinkConsumeResult
{
    public TelegramLinkConsumeStatus Status { get; init; }
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
