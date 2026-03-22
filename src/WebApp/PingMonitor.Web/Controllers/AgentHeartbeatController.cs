using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Contracts.Heartbeat;
using PingMonitor.Web.Services;

namespace PingMonitor.Web.Controllers;

[ApiController]
[Route("api/v1/agent/heartbeat")]
public sealed class AgentHeartbeatController : ControllerBase
{
    private readonly IAgentAuthenticationService _authenticationService;
    private readonly IHeartbeatService _heartbeatService;

    public AgentHeartbeatController(
        IAgentAuthenticationService authenticationService,
        IHeartbeatService heartbeatService)
    {
        _authenticationService = authenticationService;
        _heartbeatService = heartbeatService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(AgentHeartbeatResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AgentHeartbeatResponse>> PostAsync([FromBody] AgentHeartbeatRequest request, CancellationToken cancellationToken)
    {
        var authenticationResult = await _authenticationService.AuthenticateAsync(Request, cancellationToken);
        if (!authenticationResult.Succeeded)
        {
            return authenticationResult.ToActionResult(HttpContext);
        }

        var response = await _heartbeatService.ProcessHeartbeatAsync(authenticationResult.Agent!, request, cancellationToken);
        return Ok(response);
    }
}
