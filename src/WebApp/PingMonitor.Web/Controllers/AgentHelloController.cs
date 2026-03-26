using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Contracts.Hello;
using PingMonitor.Web.Services;
using PingMonitor.Web.Support;

namespace PingMonitor.Web.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/agent/hello")]
public sealed class AgentHelloController : ControllerBase
{
    private static readonly HashSet<string> SupportedCapabilities = ["icmp"];

    private readonly IAgentAuthenticationService _authenticationService;
    private readonly IAgentHelloService _helloService;

    public AgentHelloController(
        IAgentAuthenticationService authenticationService,
        IAgentHelloService helloService)
    {
        _authenticationService = authenticationService;
        _helloService = helloService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(AgentHelloResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AgentHelloResponse>> PostAsync([FromBody] AgentHelloRequest request, CancellationToken cancellationToken)
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

        var response = await _helloService.ProcessHelloAsync(authenticationResult.Agent!, request, cancellationToken);
        return Ok(response);
    }

    private static List<ApiErrorDetail> ValidateRequest(AgentHelloRequest request)
    {
        var errors = new List<ApiErrorDetail>();

        if (!IsUtc(request.StartedAtUtc))
        {
            errors.Add(new ApiErrorDetail("startedAtUtc", "Value must be a valid UTC ISO-8601 timestamp."));
        }

        for (var index = 0; index < request.Capabilities.Count; index++)
        {
            var capability = request.Capabilities[index];
            if (string.IsNullOrWhiteSpace(capability))
            {
                errors.Add(new ApiErrorDetail($"capabilities[{index}]", "Capability must not be blank."));
                continue;
            }

            if (!SupportedCapabilities.Contains(capability.Trim()))
            {
                errors.Add(new ApiErrorDetail($"capabilities[{index}]", $"Capability '{capability}' is not supported."));
            }
        }

        return errors;
    }

    private static bool IsUtc(DateTimeOffset value)
    {
        return value.Offset == TimeSpan.Zero;
    }
}
