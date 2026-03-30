namespace PingMonitor.Web.Services.Telegram;

public sealed class TelegramMessageProcessingResult
{
    public TelegramMessageProcessingStatus Status { get; init; } = TelegramMessageProcessingStatus.Ignored;
    public bool Handled { get; init; }
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool ShouldReply { get; init; }
    public string? ReplyText { get; init; }
}
