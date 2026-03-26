using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Contracts.Heartbeat;
using PingMonitor.Web.Services;
using PingMonitor.Web.Support;

namespace PingMonitor.Web.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/agent/heartbeat")]
public sealed class AgentHeartbeatController : ControllerBase
{
    private static readonly HashSet<string> AllowedStatuses = ["online", "degraded"];

    private readonly IAgentAuthenticationService _authenticationService;
    private readonly IHeartbeatService _heartbeatService;
    private readonly ILogger<AgentHeartbeatController> _logger;

    public AgentHeartbeatController(
        IAgentAuthenticationService authenticationService,
        IHeartbeatService heartbeatService,
        ILogger<AgentHeartbeatController> logger)
    {
        _authenticationService = authenticationService;
        _heartbeatService = heartbeatService;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(AgentHeartbeatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<AgentHeartbeatResponse>> PostAsync([FromBody] AgentHeartbeatRequest request, CancellationToken cancellationToken)
    {
        var authenticationResult = await _authenticationService.AuthenticateAsync(Request, cancellationToken);
        if (!authenticationResult.Succeeded)
        {
            return authenticationResult.ToActionResult(HttpContext);
        }

        var validationErrors = ValidateRequest(request);
        if (validationErrors.Count > 0)
        {
            return ApiErrorResponses.BadRequest(HttpContext, "invalid_request", "One or more fields are invalid.", validationErrors);
        }

        try
        {
            var response = await _heartbeatService.ProcessHeartbeatAsync(authenticationResult.Agent!, request, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heartbeat processing failed for agent {AgentId}.", authenticationResult.Agent!.AgentId);
            return ApiErrorResponses.ServerError(HttpContext, "server_error", "The server could not process the heartbeat.");
        }
    }

    private static List<ApiErrorDetail> ValidateRequest(AgentHeartbeatRequest request)
    {
        var errors = new List<ApiErrorDetail>();

        if (!IsUtc(request.SentAtUtc))
        {
            errors.Add(new ApiErrorDetail("sentAtUtc", "Value must be a valid UTC ISO-8601 timestamp."));
        }

        var normalizedStatus = request.Status.Trim();
        if (!AllowedStatuses.Contains(normalizedStatus))
        {
            errors.Add(new ApiErrorDetail("status", $"Status '{request.Status}' is not supported."));
        }

        return errors;
    }

    private static bool IsUtc(DateTimeOffset value)
    {
        return value.Offset == TimeSpan.Zero;
    }
}
