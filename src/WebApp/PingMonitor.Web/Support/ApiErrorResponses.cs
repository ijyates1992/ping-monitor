using Microsoft.AspNetCore.Mvc;

namespace PingMonitor.Web.Support;

public static class ApiErrorResponses
{
    public static ObjectResult BadRequest(HttpContext httpContext, string code, string message, IReadOnlyList<ApiErrorDetail>? details = null)
    {
        return Create(httpContext, StatusCodes.Status400BadRequest, code, message, details);
    }

    public static ObjectResult Unauthorized(HttpContext httpContext, string code, string message, IReadOnlyList<ApiErrorDetail>? details = null)
    {
        return Create(httpContext, StatusCodes.Status401Unauthorized, code, message, details);
    }

    public static ObjectResult Forbidden(HttpContext httpContext, string code, string message, IReadOnlyList<ApiErrorDetail>? details = null)
    {
        return Create(httpContext, StatusCodes.Status403Forbidden, code, message, details);
    }

    private static ObjectResult Create(HttpContext httpContext, int statusCode, string code, string message, IReadOnlyList<ApiErrorDetail>? details)
    {
        var response = new ApiErrorResponse(new ApiErrorBody(
            Code: code,
            Message: message,
            Details: details is { Count: > 0 } ? details : null,
            TraceId: httpContext.TraceIdentifier));

        return new ObjectResult(response)
        {
            StatusCode = statusCode
        };
    }
}
