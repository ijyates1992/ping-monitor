using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services.Status;

namespace PingMonitor.Web.Controllers;

[Authorize]
[Route("status")]
public sealed class StatusController : Controller
{
    private readonly IEndpointStatusQueryService _endpointStatusQueryService;

    public StatusController(IEndpointStatusQueryService endpointStatusQueryService)
    {
        _endpointStatusQueryService = endpointStatusQueryService;
    }

    [HttpGet("")]
    [HttpGet("/")]
    public async Task<IActionResult> Index([FromQuery] string? state, [FromQuery] string? agent, [FromQuery] string? groupId, [FromQuery] string? search, CancellationToken cancellationToken)
    {
        var viewModel = await _endpointStatusQueryService.GetStatusPageAsync(state, agent, groupId, search, cancellationToken);
        return View("Index", viewModel);
    }

    [HttpGet("refresh/recent-events")]
    public async Task<IActionResult> RefreshRecentEvents([FromQuery] string? state, [FromQuery] string? agent, [FromQuery] string? groupId, [FromQuery] string? search, CancellationToken cancellationToken)
    {
        var viewModel = await _endpointStatusQueryService.GetStatusPageAsync(state, agent, groupId, search, cancellationToken);
        return PartialView("_RecentEventsSection", viewModel);
    }

    [HttpGet("refresh/summary")]
    public async Task<IActionResult> RefreshSummary([FromQuery] string? state, [FromQuery] string? agent, [FromQuery] string? groupId, [FromQuery] string? search, CancellationToken cancellationToken)
    {
        var viewModel = await _endpointStatusQueryService.GetStatusPageAsync(state, agent, groupId, search, cancellationToken);
        return PartialView("_SummarySection", viewModel);
    }

    [HttpGet("refresh/assignments")]
    public async Task<IActionResult> RefreshAssignments([FromQuery] string? state, [FromQuery] string? agent, [FromQuery] string? groupId, [FromQuery] string? search, CancellationToken cancellationToken)
    {
        var viewModel = await _endpointStatusQueryService.GetStatusPageAsync(state, agent, groupId, search, cancellationToken);
        return PartialView("_AssignmentsSection", viewModel);
    }
}
