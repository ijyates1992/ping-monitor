namespace PingMonitor.Web.Services.Telegram;

public interface ITelegramMessageProcessor
{
    Task<TelegramMessageProcessingResult> ProcessAsync(TelegramInboundMessage inboundMessage, CancellationToken cancellationToken);
}
