namespace PingMonitor.Web.Models;

public sealed class TelegramAccount
{
    public string TelegramAccountId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
    public bool Verified { get; set; }
    public DateTimeOffset LinkedAtUtc { get; set; }
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
}
