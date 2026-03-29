namespace PingMonitor.Web.Models;

public sealed class SecurityIpBlock
{
    public string SecurityIpBlockId { get; set; } = $"sib_{Guid.NewGuid():N}";
    public SecurityAuthType AuthType { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public SecurityIpBlockType BlockType { get; set; }
    public DateTimeOffset BlockedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public string? Reason { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTimeOffset? RemovedAtUtc { get; set; }
    public string? RemovedByUserId { get; set; }
}
