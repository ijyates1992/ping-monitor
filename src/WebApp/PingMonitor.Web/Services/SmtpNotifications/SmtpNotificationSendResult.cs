namespace PingMonitor.Web.Services.SmtpNotifications;

public sealed class SmtpNotificationSendResult
{
    public bool Success { get; init; }
    public bool Skipped { get; init; }
    public string Message { get; init; } = string.Empty;

    public static SmtpNotificationSendResult Sent(string message) => new() { Success = true, Message = message };
    public static SmtpNotificationSendResult Failed(string message) => new() { Success = false, Message = message };
    public static SmtpNotificationSendResult Skip(string message) => new() { Skipped = true, Message = message };
}
