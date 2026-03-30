using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services;

public sealed class TelegramChannelSettingsDto
{
    public bool TelegramEnabled { get; set; }
    public string? TelegramBotToken { get; set; }
    public TelegramInboundMode TelegramInboundMode { get; set; } = TelegramInboundMode.Polling;
    public int TelegramPollIntervalSeconds { get; set; } = 10;
    public long TelegramLastProcessedUpdateId { get; set; }
}
