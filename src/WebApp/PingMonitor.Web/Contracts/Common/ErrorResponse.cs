namespace PingMonitor.Web.Contracts.Common;

public sealed record ErrorResponse(
    ErrorEnvelope Error);

public sealed record ErrorEnvelope(
    string Code,
    string Message,
    IReadOnlyList<ErrorDetail>? Details,
    string? TraceId);

public sealed record ErrorDetail(
    string Field,
    string Message);
