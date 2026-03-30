namespace PingMonitor.Web.Services.Telegram;

public enum TelegramLinkConsumeStatus
{
    Success = 0,
    InvalidCode = 1,
    ExpiredCode = 2,
    AlreadyUsedCode = 3,
    UnsupportedChatType = 4
}
