namespace PingMonitor.Web.Services.Telegram;

public enum TelegramMessageProcessingStatus
{
    Ignored = 0,
    VerificationSucceeded = 1,
    InvalidCode = 2,
    ExpiredCode = 3,
    AlreadyUsedCode = 4,
    UnsupportedChatType = 5
}
