using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Contracts.Config;
using PingMonitor.Web.Services;

namespace PingMonitor.Web.Controllers;

[ApiController]
[Route("api/v1/agent/config")]
public sealed class AgentConfigController : ControllerBase
{
    private readonly IAgentAuthenticationService _authenticationService;
    private readonly IAgentConfigurationService _configurationService;

    public AgentConfigController(
        IAgentAuthenticationService authenticationService,
        IAgentConfigurationService configurationService)
    {
        _authenticationService = authenticationService;
        _configurationService = configurationService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(AgentConfigResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AgentConfigResponse>> GetAsync(CancellationToken cancellationToken)
    {
        var instanceId = Request.Headers["X-Instance-Id"].ToString();

        // TODO: Enforce authenticated agent access before returning assignments.
        var isAuthenticated = await _authenticationService.ValidateAsync(instanceId, Request.Headers.Authorization, cancellationToken);
        if (!isAuthenticated)
        {
            return Unauthorized();
        }

        var response = await _configurationService.GetConfigurationAsync(instanceId, cancellationToken);
        return Ok(response);
    }
}
