using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Contracts.Hello;
using PingMonitor.Web.Services;

namespace PingMonitor.Web.Controllers;

[ApiController]
[Route("api/v1/agent/hello")]
public sealed class AgentHelloController : ControllerBase
{
    private readonly IAgentAuthenticationService _authenticationService;
    private readonly IAgentConfigurationService _configurationService;

    public AgentHelloController(
        IAgentAuthenticationService authenticationService,
        IAgentConfigurationService configurationService)
    {
        _authenticationService = authenticationService;
        _configurationService = configurationService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(AgentHelloResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AgentHelloResponse>> PostAsync([FromBody] AgentHelloRequest request, CancellationToken cancellationToken)
    {
        var instanceId = Request.Headers["X-Instance-Id"].ToString();

        // TODO: Replace placeholder validation with server-side agent credential verification.
        var isAuthenticated = await _authenticationService.ValidateAsync(instanceId, Request.Headers.Authorization, cancellationToken);
        if (!isAuthenticated)
        {
            return Unauthorized();
        }

        var response = await _configurationService.BuildHelloResponseAsync(instanceId, request, cancellationToken);
        return Ok(response);
    }
}
