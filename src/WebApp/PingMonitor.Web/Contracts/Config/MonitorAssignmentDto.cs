namespace PingMonitor.Web.Contracts.Config;

public sealed record MonitorAssignmentDto(
    string AssignmentId,
    string EndpointId,
    string Name,
    string Target,
    string CheckType,
    bool Enabled,
    int PingIntervalSeconds,
    int RetryIntervalSeconds,
    int TimeoutMs,
    int FailureThreshold,
    int RecoveryThreshold,
    string? DependsOnEndpointId,
    IReadOnlyList<string> Tags);
