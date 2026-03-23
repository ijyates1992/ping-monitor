using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services.Status;

namespace PingMonitor.Web.Controllers;

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
    public async Task<IActionResult> Index([FromQuery] string? state, [FromQuery] string? agent, [FromQuery] string? search, CancellationToken cancellationToken)
    {
        var viewModel = await _endpointStatusQueryService.GetStatusPageAsync(state, agent, search, cancellationToken);
        return View("Index", viewModel);
    }
}
