using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Contracts.Results;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Metrics;
using PingMonitor.Web.Support;

namespace PingMonitor.Web.Controllers;

[ApiController]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[Route("api/v1/agent/results")]
public sealed class AgentResultsController : ControllerBase
{
    private readonly IAgentAuthenticationService _authenticationService;
    private const int HydrationRetryAfterSeconds = 30;

    private readonly IResultIngestionService _resultIngestionService;
    private readonly IRollingWindowHydrationState _hydrationState;
    private readonly ILogger<AgentResultsController> _logger;

    public AgentResultsController(
        IAgentAuthenticationService authenticationService,
        IResultIngestionService resultIngestionService,
        IRollingWindowHydrationState hydrationState,
        ILogger<AgentResultsController> logger)
    {
        _authenticationService = authenticationService;
        _resultIngestionService = resultIngestionService;
        _hydrationState = hydrationState;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(SubmitResultsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SubmitResultsResponse>> PostAsync([FromBody] SubmitResultsRequest request, CancellationToken cancellationToken)
    {
        var authenticationResult = await _authenticationService.AuthenticateAsync(Request, cancellationToken);
        if (!authenticationResult.Succeeded)
        {
            return authenticationResult.ToActionResult(HttpContext);
        }

        var hydrationSnapshot = _hydrationState.GetSnapshot();
        if (hydrationSnapshot.Status != RollingWindowHydrationStatus.Complete)
        {
            _logger.LogDebug(
                "Rejecting result ingestion for agent {AgentId} because rolling window hydration status is {HydrationStatus}.",
                authenticationResult.Agent!.AgentId,
                hydrationSnapshot.Status);
            Response.Headers.RetryAfter = HydrationRetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return ApiErrorResponses.ServiceUnavailable(
                HttpContext,
                "ingestion_temporarily_unavailable",
                BuildHydrationUnavailableMessage(hydrationSnapshot));
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

    private static string BuildHydrationUnavailableMessage(RollingWindowHydrationSnapshot hydrationSnapshot)
    {
        return hydrationSnapshot.Status == RollingWindowHydrationStatus.Failed
            ? "Agent result ingestion is unavailable because rolling-window metrics hydration failed. An administrator must review the hydration failure before results can be accepted."
            : "Agent result ingestion is temporarily unavailable while rolling-window metrics hydration completes. Retry later without dropping cached results.";
    }
}
