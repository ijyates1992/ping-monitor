using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.ViewModels.NetworkDiagrams;

namespace PingMonitor.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Admin)]
[Route("network-diagrams")]
public sealed class NetworkDiagramsController : Controller
{
    private readonly IApplicationSettingsService _applicationSettingsService;

    public NetworkDiagramsController(IApplicationSettingsService applicationSettingsService)
    {
        _applicationSettingsService = applicationSettingsService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var settings = await _applicationSettingsService.GetCurrentAsync(cancellationToken);
        if (!settings.NetworkDiagramsEnabled)
        {
            return NotFound("Network diagrams are not enabled.");
        }

        return View("Index", new NetworkDiagramsEditorPageViewModel());
    }
}
