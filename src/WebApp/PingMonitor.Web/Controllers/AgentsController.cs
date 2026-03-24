using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services;
using PingMonitor.Web.ViewModels.Agents;

namespace PingMonitor.Web.Controllers;

[Route("agents")]
public sealed class AgentsController : Controller
{
    private readonly IAgentProvisioningService _agentProvisioningService;

    public AgentsController(IAgentProvisioningService agentProvisioningService)
    {
        _agentProvisioningService = agentProvisioningService;
    }

    [HttpGet("deploy")]
    public IActionResult Deploy()
    {
        return View("Deploy", new DeployAgentPageViewModel());
    }

    [HttpPost("deploy")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deploy([FromForm] DeployAgentPageViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("Deploy", model);
        }

        try
        {
            var result = await _agentProvisioningService.ProvisionAsync(model.AgentName, cancellationToken);
            return File(result.PackageBytes, "application/zip", result.PackageFileName);
        }
        catch (InvalidOperationException ex)
        {
            model.ErrorMessage = ex.Message;
            return View("Deploy", model);
        }
    }
}
