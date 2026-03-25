using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Agents;
using PingMonitor.Web.ViewModels.Agents;

namespace PingMonitor.Web.Controllers;

[Route("agents")]
public sealed class AgentsController : Controller
{
    private const string StatusMessageKey = "Agents.StatusMessage";
    private const string ErrorMessageKey = "Agents.ErrorMessage";

    private readonly IAgentProvisioningService _agentProvisioningService;
    private readonly IAgentManagementQueryService _agentManagementQueryService;

    public AgentsController(
        IAgentProvisioningService agentProvisioningService,
        IAgentManagementQueryService agentManagementQueryService)
    {
        _agentProvisioningService = agentProvisioningService;
        _agentManagementQueryService = agentManagementQueryService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var rows = await _agentManagementQueryService.ListAsync(cancellationToken);
        var viewModel = new ManageAgentsPageViewModel
        {
            Agents = rows.Select(row => new ManageAgentRowViewModel
            {
                AgentId = row.AgentId,
                Name = row.Name,
                InstanceId = row.InstanceId,
                Enabled = row.Enabled,
                ApiKeyRevoked = row.ApiKeyRevoked,
                LastSeenUtc = row.LastSeenUtc,
                LastHeartbeatUtc = row.LastHeartbeatUtc,
                AgentVersion = row.AgentVersion,
                MachineName = row.MachineName,
                Platform = row.Platform,
                CreatedAtUtc = row.CreatedAtUtc,
                AssignmentCount = row.AssignmentCount
            }).ToList(),
            StatusMessage = TempData[StatusMessageKey] as string,
            ErrorMessage = TempData[ErrorMessageKey] as string
        };

        return View("Index", viewModel);
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

    [HttpPost("{id}/enable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enable([FromRoute] string id, CancellationToken cancellationToken)
    {
        return await SetEnabledAsync(id, true, cancellationToken);
    }

    [HttpPost("{id}/disable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disable([FromRoute] string id, CancellationToken cancellationToken)
    {
        return await SetEnabledAsync(id, false, cancellationToken);
    }

    [HttpPost("{id}/rotate-package")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RotatePackage([FromRoute] string id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _agentProvisioningService.RotatePackageAsync(id, cancellationToken);
            return File(result.PackageBytes, "application/zip", result.PackageFileName);
        }
        catch (InvalidOperationException ex)
        {
            TempData[ErrorMessageKey] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    private async Task<IActionResult> SetEnabledAsync(string agentId, bool enabled, CancellationToken cancellationToken)
    {
        try
        {
            var changed = await _agentProvisioningService.SetEnabledAsync(agentId, enabled, cancellationToken);
            if (changed)
            {
                TempData[StatusMessageKey] = enabled
                    ? "Agent enabled. Future authenticated requests are allowed."
                    : "Agent disabled. Future authenticated requests are blocked.";
            }
            else
            {
                TempData[StatusMessageKey] = enabled
                    ? "Agent was already enabled."
                    : "Agent was already disabled.";
            }
        }
        catch (InvalidOperationException ex)
        {
            TempData[ErrorMessageKey] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}
