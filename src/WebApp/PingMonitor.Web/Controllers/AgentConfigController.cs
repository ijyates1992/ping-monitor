using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Contracts.Config;
using PingMonitor.Web.Services;

namespace PingMonitor.Web.Controllers;

[ApiController]
[AllowAnonymous]
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
        var authenticationResult = await _authenticationService.AuthenticateAsync(Request, cancellationToken);
        if (!authenticationResult.Succeeded)
        {
            return authenticationResult.ToActionResult(HttpContext);
        }

        var response = await _configurationService.GetConfigurationAsync(authenticationResult.Agent!, cancellationToken);
        return Ok(response);
    }
}
