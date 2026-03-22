using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Contracts;
using PingMonitor.Web.Services;

namespace PingMonitor.Web.Controllers;

[ApiController]
[Route("api/v1/agent")]
public sealed class AgentController : ControllerBase
{
    private readonly IAgentAuthenticationService _authenticationService;
    private readonly IAgentConfigurationService _configurationService;
    private readonly IHeartbeatService _heartbeatService;
    private readonly IResultIngestionService _resultIngestionService;

    public AgentController(
        IAgentAuthenticationService authenticationService,
        IAgentConfigurationService configurationService,
        IHeartbeatService heartbeatService,
        IResultIngestionService resultIngestionService)
    {
        _authenticationService = authenticationService;
        _configurationService = configurationService;
        _heartbeatService = heartbeatService;
        _resultIngestionService = resultIngestionService;
    }

    [HttpPost("hello")]
    [ProducesResponseType(typeof(AgentHelloResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AgentHelloResponse>> HelloAsync([FromBody] AgentHelloRequest request, CancellationToken cancellationToken)
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

    [HttpGet("config")]
    [ProducesResponseType(typeof(AgentConfigResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AgentConfigResponse>> GetConfigAsync(CancellationToken cancellationToken)
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

    [HttpPost("heartbeat")]
    [ProducesResponseType(typeof(AgentHeartbeatResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AgentHeartbeatResponse>> HeartbeatAsync([FromBody] AgentHeartbeatRequest request, CancellationToken cancellationToken)
    {
        var instanceId = Request.Headers["X-Instance-Id"].ToString();

        // TODO: Enforce authenticated agent access and persist heartbeat metadata.
        var isAuthenticated = await _authenticationService.ValidateAsync(instanceId, Request.Headers.Authorization, cancellationToken);
        if (!isAuthenticated)
        {
            return Unauthorized();
        }

        var response = await _heartbeatService.ProcessHeartbeatAsync(instanceId, request, cancellationToken);
        return Ok(response);
    }

    [HttpPost("results")]
    [ProducesResponseType(typeof(SubmitResultsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SubmitResultsResponse>> SubmitResultsAsync([FromBody] SubmitResultsRequest request, CancellationToken cancellationToken)
    {
        var instanceId = Request.Headers["X-Instance-Id"].ToString();

        // TODO: Enforce authenticated agent access and hand off raw results for storage and later state evaluation.
        var isAuthenticated = await _authenticationService.ValidateAsync(instanceId, Request.Headers.Authorization, cancellationToken);
        if (!isAuthenticated)
        {
            return Unauthorized();
        }

        var response = await _resultIngestionService.IngestAsync(instanceId, request, cancellationToken);
        return Ok(response);
    }
}
