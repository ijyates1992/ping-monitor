namespace PingMonitor.Web.Contracts.Results;

public sealed record CheckResultDto(
    string AssignmentId,
    string EndpointId,
    string CheckType,
    DateTimeOffset CheckedAtUtc,
    bool Success,
    int? RoundTripMs,
    string? ErrorCode,
    string? ErrorMessage);
