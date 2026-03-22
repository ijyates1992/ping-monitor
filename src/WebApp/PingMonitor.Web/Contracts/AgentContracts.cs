namespace PingMonitor.Web.Contracts;

public sealed record AgentHelloRequest(
    string AgentVersion,
    string MachineName,
    string Platform,
    IReadOnlyList<string> Capabilities,
    DateTimeOffset StartedAtUtc);

public sealed record AgentHelloResponse(
    string AgentId,
    DateTimeOffset ServerTimeUtc,
    int ConfigRefreshSeconds,
    int HeartbeatIntervalSeconds,
    int ResultBatchIntervalSeconds,
    int MaxResultBatchSize,
    string ConfigVersion);

public sealed record AgentConfigResponse(
    string ConfigVersion,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<MonitorAssignmentDto> Assignments);

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

public sealed record AgentHeartbeatRequest(
    string AgentVersion,
    DateTimeOffset SentAtUtc,
    string ConfigVersion,
    int ActiveAssignments,
    int QueuedResultCount,
    string Status);

public sealed record AgentHeartbeatResponse(
    bool Ok,
    DateTimeOffset ServerTimeUtc,
    bool ConfigChanged);

public sealed record SubmitResultsRequest(
    DateTimeOffset SentAtUtc,
    string BatchId,
    IReadOnlyList<CheckResultDto> Results);

public sealed record CheckResultDto(
    string AssignmentId,
    string EndpointId,
    string CheckType,
    DateTimeOffset CheckedAtUtc,
    bool Success,
    int? RoundTripMs,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record SubmitResultsResponse(
    bool Accepted,
    int AcceptedCount,
    bool Duplicate,
    DateTimeOffset ServerTimeUtc);

public sealed record ErrorResponseDto(
    ErrorEnvelopeDto Error);

public sealed record ErrorEnvelopeDto(
    string Code,
    string Message,
    IReadOnlyList<ErrorDetailDto>? Details,
    string? TraceId);

public sealed record ErrorDetailDto(
    string Field,
    string Message);
