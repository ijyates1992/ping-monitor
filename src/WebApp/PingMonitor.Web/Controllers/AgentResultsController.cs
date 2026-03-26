using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Contracts.Results;
using PingMonitor.Web.Services;
using PingMonitor.Web.Support;

namespace PingMonitor.Web.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/agent/results")]
public sealed class AgentResultsController : ControllerBase
{
    private readonly IAgentAuthenticationService _authenticationService;
    private readonly IResultIngestionService _resultIngestionService;
    private readonly ILogger<AgentResultsController> _logger;

    public AgentResultsController(
        IAgentAuthenticationService authenticationService,
        IResultIngestionService resultIngestionService,
        ILogger<AgentResultsController> logger)
    {
        _authenticationService = authenticationService;
        _resultIngestionService = resultIngestionService;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(SubmitResultsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SubmitResultsResponse>> PostAsync([FromBody] SubmitResultsRequest request, CancellationToken cancellationToken)
    {
        var authenticationResult = await _authenticationService.AuthenticateAsync(Request, cancellationToken);
        if (!authenticationResult.Succeeded)
        {
            return authenticationResult.ToActionResult(HttpContext);
        }

        try
        {
            var response = await _resultIngestionService.IngestAsync(authenticationResult.Agent!, request, cancellationToken);
            return Ok(response);
        }
        catch (ResultIngestionValidationException ex)
        {
            return ApiErrorResponses.BadRequest(HttpContext, "invalid_request", "One or more fields are invalid.", ex.Errors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Result ingestion failed for agent {AgentId}.", authenticationResult.Agent!.AgentId);
            return ApiErrorResponses.ServerError(HttpContext, "server_error", "The server could not process the results batch.");
        }
    }
}
