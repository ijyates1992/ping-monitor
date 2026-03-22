namespace PingMonitor.Web.Contracts.Heartbeat;

public sealed record AgentHeartbeatRequest(
    string AgentVersion,
    DateTimeOffset SentAtUtc,
    string ConfigVersion,
    int ActiveAssignments,
    int QueuedResultCount,
    string Status);
