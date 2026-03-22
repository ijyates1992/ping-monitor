using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Models;
using PingMonitor.Web.Support;

namespace PingMonitor.Web.Services;

public sealed class AgentAuthenticationResult
{
    private AgentAuthenticationResult(bool succeeded, Agent? agent, int statusCode, string errorCode, string message)
    {
        Succeeded = succeeded;
        Agent = agent;
        StatusCode = statusCode;
        ErrorCode = errorCode;
        Message = message;
    }

    public bool Succeeded { get; }
    public Agent? Agent { get; }
    public int StatusCode { get; }
    public string ErrorCode { get; }
    public string Message { get; }

    public static AgentAuthenticationResult Success(Agent agent) => new(true, agent, StatusCodes.Status200OK, string.Empty, string.Empty);

    public static AgentAuthenticationResult Unauthorized(string message) =>
        new(false, null, StatusCodes.Status401Unauthorized, "unauthorized", message);

    public static AgentAuthenticationResult Forbidden(string message) =>
        new(false, null, StatusCodes.Status403Forbidden, "forbidden", message);

    public ActionResult ToActionResult(HttpContext httpContext)
    {
        return StatusCode switch
        {
            StatusCodes.Status403Forbidden => ApiErrorResponses.Forbidden(httpContext, ErrorCode, Message),
            _ => ApiErrorResponses.Unauthorized(httpContext, ErrorCode, Message)
        };
    }
}
