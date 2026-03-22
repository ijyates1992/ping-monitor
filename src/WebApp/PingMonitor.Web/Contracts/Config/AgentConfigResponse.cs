namespace PingMonitor.Web.Contracts.Config;

public sealed record AgentConfigResponse(
    string ConfigVersion,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<MonitorAssignmentDto> Assignments);
