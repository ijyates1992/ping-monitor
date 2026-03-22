using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Contracts.Results;
using PingMonitor.Web.Services;

namespace PingMonitor.Web.Controllers;

[ApiController]
[Route("api/v1/agent/results")]
public sealed class AgentResultsController : ControllerBase
{
    private readonly IAgentAuthenticationService _authenticationService;
    private readonly IResultIngestionService _resultIngestionService;

    public AgentResultsController(
        IAgentAuthenticationService authenticationService,
        IResultIngestionService resultIngestionService)
    {
        _authenticationService = authenticationService;
        _resultIngestionService = resultIngestionService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(SubmitResultsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SubmitResultsResponse>> PostAsync([FromBody] SubmitResultsRequest request, CancellationToken cancellationToken)
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
