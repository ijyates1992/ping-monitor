using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Endpoints;
using PingMonitor.Web.ViewModels.Endpoints;

namespace PingMonitor.Web.Controllers;

[Route("endpoints")]
public sealed class EndpointsController : Controller
{
    private readonly IEndpointCreationQueryService _endpointCreationQueryService;
    private readonly IEndpointManagementQueryService _endpointManagementQueryService;
    private readonly IEndpointManagementService _endpointManagementService;
    private readonly IApplicationSettingsService _applicationSettingsService;

    public EndpointsController(
        IEndpointCreationQueryService endpointCreationQueryService,
        IEndpointManagementQueryService endpointManagementQueryService,
        IEndpointManagementService endpointManagementService,
        IApplicationSettingsService applicationSettingsService)
    {
        _endpointCreationQueryService = endpointCreationQueryService;
        _endpointManagementQueryService = endpointManagementQueryService;
        _endpointManagementService = endpointManagementService;
        _applicationSettingsService = applicationSettingsService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = await _endpointManagementQueryService.GetManagePageAsync(cancellationToken);
        return View("Index", model);
    }

    [HttpGet("new")]
    public async Task<IActionResult> New(CancellationToken cancellationToken)
    {
        var defaults = await _applicationSettingsService.GetCurrentAsync(cancellationToken);
        var model = new CreateEndpointPageViewModel
        {
            PingIntervalSeconds = defaults.DefaultPingIntervalSeconds,
            RetryIntervalSeconds = defaults.DefaultRetryIntervalSeconds,
            TimeoutMs = defaults.DefaultTimeoutMs,
            FailureThreshold = defaults.DefaultFailureThreshold,
            RecoveryThreshold = defaults.DefaultRecoveryThreshold
        };
        await PopulateOptionsAsync(model, cancellationToken);
        return View("New", model);
    }

    [HttpPost("new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New([FromForm] CreateEndpointPageViewModel model, CancellationToken cancellationToken)
    {
        await PopulateOptionsAsync(model, cancellationToken);

        if (!ModelState.IsValid)
        {
            return View("New", model);
        }

        var result = await _endpointManagementService.CreateEndpointWithAssignmentAsync(
            new CreateEndpointCommand
            {
                EndpointName = model.EndpointName,
                Target = model.Target,
                AgentId = model.AgentId,
                DependsOnEndpointIds = model.DependsOnEndpointIds,
                PingIntervalSeconds = model.PingIntervalSeconds,
                RetryIntervalSeconds = model.RetryIntervalSeconds,
                TimeoutMs = model.TimeoutMs,
                FailureThreshold = model.FailureThreshold,
                RecoveryThreshold = model.RecoveryThreshold,
                EndpointEnabled = model.EndpointEnabled,
                AssignmentEnabled = model.AssignmentEnabled
            },
            cancellationToken);

        if (!result.Success)
        {
            AddValidationErrorsToModelState(result.ValidationErrors);
            return View("New", model);
        }

        return Redirect("/endpoints");
    }

    [HttpGet("{assignmentId}/edit")]
    public async Task<IActionResult> Edit([FromRoute] string assignmentId, CancellationToken cancellationToken)
    {
        var editModel = await _endpointManagementService.GetEditModelAsync(assignmentId, cancellationToken);
        if (editModel is null)
        {
            return NotFound();
        }

        var model = new EditEndpointPageViewModel
        {
            AssignmentId = editModel.AssignmentId,
            EndpointId = editModel.EndpointId,
            EndpointName = editModel.EndpointName,
            Target = editModel.Target,
            AgentId = editModel.AgentId,
            DependsOnEndpointIds = editModel.DependsOnEndpointIds.ToList(),
            EndpointEnabled = editModel.EndpointEnabled,
            AssignmentEnabled = editModel.AssignmentEnabled,
            PingIntervalSeconds = editModel.PingIntervalSeconds,
            RetryIntervalSeconds = editModel.RetryIntervalSeconds,
            TimeoutMs = editModel.TimeoutMs,
            FailureThreshold = editModel.FailureThreshold,
            RecoveryThreshold = editModel.RecoveryThreshold
        };

        await PopulateEditOptionsAsync(model, cancellationToken);
        return View("Edit", model);
    }

    [HttpPost("{assignmentId}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit([FromRoute] string assignmentId, [FromForm] EditEndpointPageViewModel model, CancellationToken cancellationToken)
    {
        model.AssignmentId = assignmentId;
        await PopulateEditOptionsAsync(model, cancellationToken);

        if (!ModelState.IsValid)
        {
            return View("Edit", model);
        }

        var result = await _endpointManagementService.UpdateEndpointWithAssignmentAsync(
            new UpdateEndpointCommand
            {
                AssignmentId = model.AssignmentId,
                EndpointId = model.EndpointId,
                EndpointName = model.EndpointName,
                Target = model.Target,
                AgentId = model.AgentId,
                DependsOnEndpointIds = model.DependsOnEndpointIds,
                EndpointEnabled = model.EndpointEnabled,
                AssignmentEnabled = model.AssignmentEnabled,
                PingIntervalSeconds = model.PingIntervalSeconds,
                RetryIntervalSeconds = model.RetryIntervalSeconds,
                TimeoutMs = model.TimeoutMs,
                FailureThreshold = model.FailureThreshold,
                RecoveryThreshold = model.RecoveryThreshold
            },
            cancellationToken);

        if (!result.Success)
        {
            AddValidationErrorsToModelState(result.ValidationErrors);
            return View("Edit", model);
        }

        return Redirect("/endpoints");
    }

    private async Task PopulateOptionsAsync(CreateEndpointPageViewModel model, CancellationToken cancellationToken)
    {
        var options = await _endpointCreationQueryService.GetOptionsAsync(cancellationToken);
        model.AvailableAgents = options.Agents;
        model.AvailableDependencyEndpoints = options.DependencyEndpoints;
    }

    private async Task PopulateEditOptionsAsync(EditEndpointPageViewModel model, CancellationToken cancellationToken)
    {
        var options = await _endpointManagementQueryService.GetEditOptionsAsync(model.AssignmentId, cancellationToken);
        model.AvailableAgents = options.Agents;
        model.AvailableDependencies = options.Dependencies;
    }

    private void AddValidationErrorsToModelState(IReadOnlyList<EndpointValidationError> validationErrors)
    {
        foreach (var validationError in validationErrors)
        {
            var modelKey = validationError.Field;
            ModelState.AddModelError(modelKey, validationError.Message);
        }
    }
}
