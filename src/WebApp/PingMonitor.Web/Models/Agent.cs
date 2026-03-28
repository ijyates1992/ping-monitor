namespace PingMonitor.Web.Models;

public sealed class Agent
{
    public string AgentId { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Site { get; set; }
    public bool Enabled { get; set; }
    public string ApiKeyHash { get; set; } = string.Empty;
    public DateTimeOffset ApiKeyCreatedAtUtc { get; set; }
    public bool ApiKeyRevoked { get; set; }
    public DateTimeOffset? LastHeartbeatUtc { get; set; }
    public DateTimeOffset? LastSeenUtc { get; set; }
    public AgentHealthStatus Status { get; set; } = AgentHealthStatus.Offline;
    public string? AgentVersion { get; set; }
    public string? Platform { get; set; }
    public string? MachineName { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? LastHeartbeatEventLoggedAtUtc { get; set; }
}
