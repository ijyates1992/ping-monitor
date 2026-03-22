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
        var authenticationResult = await _authenticationService.AuthenticateAsync(Request, cancellationToken);
        if (!authenticationResult.Succeeded)
        {
            return authenticationResult.ToActionResult(HttpContext);
        }

        var response = await _resultIngestionService.IngestAsync(authenticationResult.Agent!, request, cancellationToken);
        return Ok(response);
    }
}
