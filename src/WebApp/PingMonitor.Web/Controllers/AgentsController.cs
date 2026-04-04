using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Agents;
using PingMonitor.Web.Services.EventLogs;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.ViewModels.Agents;

namespace PingMonitor.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Admin)]
[Route("agents")]
public sealed class AgentsController : Controller
{
    private const string StatusMessageKey = "Agents.StatusMessage";
    private const string ErrorMessageKey = "Agents.ErrorMessage";
    private const string RemoveConfirmationKeyword = "REMOVE";

    private readonly IAgentProvisioningService _agentProvisioningService;
    private readonly IAgentManagementQueryService _agentManagementQueryService;
    private readonly IEventLogQueryService _eventLogQueryService;
    private readonly IApplicationSettingsService _applicationSettingsService;

    public AgentsController(
        IAgentProvisioningService agentProvisioningService,
        IAgentManagementQueryService agentManagementQueryService,
        IEventLogQueryService eventLogQueryService,
        IApplicationSettingsService applicationSettingsService)
    {
        _agentProvisioningService = agentProvisioningService;
        _agentManagementQueryService = agentManagementQueryService;
        _eventLogQueryService = eventLogQueryService;
        _applicationSettingsService = applicationSettingsService;
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
                AssignmentCount = row.AssignmentCount,
                UptimePercent = row.UptimePercent
            }).ToList(),
            StatusMessage = TempData[StatusMessageKey] as string,
            ErrorMessage = TempData[ErrorMessageKey] as string
        };

        return View("Index", viewModel);
    }

    [HttpGet("deploy")]
    public async Task<IActionResult> Deploy(CancellationToken cancellationToken)
    {
        return View("Deploy", await BuildDeployViewModelAsync(cancellationToken));
    }

    [HttpPost("deploy/site-url")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSiteUrl([FromForm] DeployAgentPageViewModel model, CancellationToken cancellationToken)
    {
        ValidateDeploySiteUrl(model.SiteUrl);

        if (ModelState.ContainsKey(nameof(DeployAgentPageViewModel.AgentName)))
        {
            ModelState[nameof(DeployAgentPageViewModel.AgentName)]!.ValidationState = Microsoft.AspNetCore.Mvc.ModelBinding.ModelValidationState.Valid;
        }

        if (!ModelState.IsValid)
        {
            return View("Deploy", await BuildDeployViewModelAsync(cancellationToken, model));
        }

        var current = await _applicationSettingsService.GetCurrentAsync(cancellationToken);
        await _applicationSettingsService.UpdateAsync(
            new UpdateApplicationSettingsCommand
            {
                SiteUrl = model.SiteUrl,
                DefaultPingIntervalSeconds = current.DefaultPingIntervalSeconds,
                DefaultRetryIntervalSeconds = current.DefaultRetryIntervalSeconds,
                DefaultTimeoutMs = current.DefaultTimeoutMs,
                DefaultFailureThreshold = current.DefaultFailureThreshold,
                DefaultRecoveryThreshold = current.DefaultRecoveryThreshold
            },
            cancellationToken);

        var updatedModel = await BuildDeployViewModelAsync(cancellationToken, model);
        updatedModel.SiteUrlSaved = true;
        return View("Deploy", updatedModel);
    }

    [HttpGet("{id}/history")]
    public async Task<IActionResult> History(
        [FromRoute] string id,
        [FromQuery] string? search,
        [FromQuery] string? eventType,
        [FromQuery] DateTimeOffset? dateFromUtc,
        [FromQuery] DateTimeOffset? dateToUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var model = await _eventLogQueryService.GetAgentHistoryPageAsync(id, search, eventType, dateFromUtc, dateToUtc, page, pageSize, cancellationToken);
        if (model is null)
        {
            return NotFound();
        }

        return View("History", model);
    }

    [HttpPost("deploy")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deploy([FromForm] DeployAgentPageViewModel model, CancellationToken cancellationToken)
    {
        var settings = await _applicationSettingsService.GetCurrentAsync(cancellationToken);
        var validatedUrl = ValidateDeploySiteUrl(settings.SiteUrl);
        if (!validatedUrl.isValid)
        {
            ModelState.AddModelError(nameof(DeployAgentPageViewModel.SiteUrl), validatedUrl.warningMessage!);
        }

        if (!ModelState.IsValid)
        {
            return View("Deploy", await BuildDeployViewModelAsync(cancellationToken, model));
        }

        try
        {
            var result = await _agentProvisioningService.ProvisionAsync(model.AgentName, cancellationToken);
            return File(result.PackageBytes, "application/zip", result.PackageFileName);
        }
        catch (InvalidOperationException ex)
        {
            var reloadModel = await BuildDeployViewModelAsync(cancellationToken, model);
            reloadModel.ErrorMessage = ex.Message;
            return View("Deploy", reloadModel);
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

    [HttpGet("{id}/remove")]
    public async Task<IActionResult> Remove([FromRoute] string id, CancellationToken cancellationToken)
    {
        var details = await _agentManagementQueryService.GetRemoveDetailsAsync(id, cancellationToken);
        if (details is null)
        {
            return NotFound();
        }

        return View("Remove", BuildRemoveViewModel(details));
    }

    [HttpPost("{id}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove([FromRoute] string id, [FromForm] RemoveAgentPageViewModel model, CancellationToken cancellationToken)
    {
        var details = await _agentManagementQueryService.GetRemoveDetailsAsync(id, cancellationToken);
        if (details is null)
        {
            return NotFound();
        }

        var viewModel = BuildRemoveViewModel(details, model.ConfirmationText);
        if (!string.Equals(model.ConfirmationText?.Trim(), RemoveConfirmationKeyword, StringComparison.Ordinal))
        {
            viewModel.ErrorMessage = $"Type {RemoveConfirmationKeyword} to confirm removing this agent.";
            return View("Remove", viewModel);
        }

        try
        {
            var changed = await _agentProvisioningService.RemoveAsync(id, cancellationToken);
            TempData[StatusMessageKey] = changed
                ? "Agent removed. Authentication is revoked and active assignments are disabled. Historical data is preserved."
                : "Agent was already removed.";
        }
        catch (InvalidOperationException ex)
        {
            TempData[ErrorMessageKey] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<DeployAgentPageViewModel> BuildDeployViewModelAsync(
        CancellationToken cancellationToken,
        DeployAgentPageViewModel? postedModel = null)
    {
        var settings = await _applicationSettingsService.GetCurrentAsync(cancellationToken);
        var siteUrlValue = postedModel?.SiteUrl ?? settings.SiteUrl;
        var validation = ValidateDeploySiteUrl(siteUrlValue);

        return new DeployAgentPageViewModel
        {
            AgentName = postedModel?.AgentName ?? string.Empty,
            SiteUrl = siteUrlValue,
            SiteUrlSaved = postedModel?.SiteUrlSaved ?? false,
            SiteUrlIsValid = validation.isValid,
            SiteUrlWarningMessage = validation.warningMessage,
            ErrorMessage = postedModel?.ErrorMessage
        };
    }

    private static (bool isValid, string? warningMessage) ValidateDeploySiteUrl(string? siteUrl)
    {
        if (string.IsNullOrWhiteSpace(siteUrl))
        {
            return (false, "Agent provisioning site URL is required before you can deploy an agent package.");
        }

        var value = siteUrl.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return (false, "Agent provisioning site URL must be a valid absolute URL.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Agent provisioning site URL must use HTTPS so agents can connect securely.");
        }

        return (true, null);
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

    private static RemoveAgentPageViewModel BuildRemoveViewModel(RemoveAgentDetails details, string? confirmationText = null)
    {
        return new RemoveAgentPageViewModel
        {
            AgentId = details.AgentId,
            AgentName = details.Name,
            InstanceId = details.InstanceId,
            AssignmentCount = details.AssignmentCount,
            ConfirmationText = confirmationText ?? string.Empty,
            RequiredConfirmationText = RemoveConfirmationKeyword
        };
    }
}
