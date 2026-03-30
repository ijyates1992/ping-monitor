using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.Telegram;

public interface ITelegramNotificationSender
{
    Task<TelegramNotificationSendResult> SendForEventAsync(EventLog eventLog, CancellationToken cancellationToken);
}
