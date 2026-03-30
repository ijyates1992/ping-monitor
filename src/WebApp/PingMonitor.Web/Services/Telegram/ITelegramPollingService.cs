namespace PingMonitor.Web.Services.Telegram;

public interface ITelegramPollingService
{
    Task PollOnceAsync(CancellationToken cancellationToken);
}
