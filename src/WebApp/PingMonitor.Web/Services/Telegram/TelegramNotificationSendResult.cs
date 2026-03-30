namespace PingMonitor.Web.Services.Telegram;

public sealed class TelegramNotificationSendResult
{
    public bool Success { get; init; }
    public bool Skipped { get; init; }
    public string Message { get; init; } = string.Empty;

    public static TelegramNotificationSendResult Sent(string message) => new() { Success = true, Message = message };
    public static TelegramNotificationSendResult Skip(string message) => new() { Skipped = true, Message = message };
    public static TelegramNotificationSendResult Failed(string message) => new() { Message = message };
}
