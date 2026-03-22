namespace PingMonitor.Web.Support;

public sealed record ApiErrorResponse(ApiErrorBody Error);

public sealed record ApiErrorBody(
    string Code,
    string Message,
    IReadOnlyList<ApiErrorDetail>? Details,
    string? TraceId);

public sealed record ApiErrorDetail(string Field, string Message);
