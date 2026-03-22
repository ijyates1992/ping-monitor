namespace PingMonitor.Web.Contracts.Heartbeat;

public sealed record AgentHeartbeatResponse(
    bool Ok,
    DateTimeOffset ServerTimeUtc,
    bool ConfigChanged);
