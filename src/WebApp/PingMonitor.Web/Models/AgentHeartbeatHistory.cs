namespace PingMonitor.Web.Models;

public sealed class AgentHeartbeatHistory
{
    public string AgentHeartbeatHistoryId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public DateTimeOffset HeartbeatAtUtc { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}
