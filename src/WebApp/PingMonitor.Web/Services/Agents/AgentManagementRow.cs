namespace PingMonitor.Web.Services.Agents;

public sealed class AgentManagementRow
{
    public required string AgentId { get; init; }
    public required string Name { get; init; }
    public required string InstanceId { get; init; }
    public required bool Enabled { get; init; }
    public required bool ApiKeyRevoked { get; init; }
    public required DateTimeOffset? LastSeenUtc { get; init; }
    public required DateTimeOffset? LastHeartbeatUtc { get; init; }
    public required string AgentVersion { get; init; }
    public required string MachineName { get; init; }
    public required string Platform { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required int AssignmentCount { get; init; }
}
