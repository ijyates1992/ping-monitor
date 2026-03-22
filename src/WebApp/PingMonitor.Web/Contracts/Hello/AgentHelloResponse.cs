namespace PingMonitor.Web.Contracts.Hello;

public sealed record AgentHelloResponse(
    string AgentId,
    DateTimeOffset ServerTimeUtc,
    int ConfigRefreshSeconds,
    int HeartbeatIntervalSeconds,
    int ResultBatchIntervalSeconds,
    int MaxResultBatchSize,
    string ConfigVersion);
