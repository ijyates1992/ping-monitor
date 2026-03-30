namespace PingMonitor.Web.Services.Telegram;

public sealed class TelegramMessageProcessingResult
{
    public bool Handled { get; init; }
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
