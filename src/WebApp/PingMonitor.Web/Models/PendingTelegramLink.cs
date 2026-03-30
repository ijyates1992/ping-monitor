namespace PingMonitor.Web.Models;

public sealed class PendingTelegramLink
{
    public string PendingTelegramLinkId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }
    public string? ConsumedByChatId { get; set; }
    public PendingTelegramLinkStatus Status { get; set; } = PendingTelegramLinkStatus.Pending;
}
