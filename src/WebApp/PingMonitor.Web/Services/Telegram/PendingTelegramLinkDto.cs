namespace PingMonitor.Web.Services.Telegram;

public sealed class PendingTelegramLinkDto
{
    public string Code { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; init; }
}
