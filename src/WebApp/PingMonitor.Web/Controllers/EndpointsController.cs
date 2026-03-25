using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services.Endpoints;
using PingMonitor.Web.ViewModels.Endpoints;

namespace PingMonitor.Web.Controllers;

[Route("endpoints")]
public sealed class EndpointsController : Controller
{
    private readonly IEndpointCreationQueryService _endpointCreationQueryService;
    private readonly IEndpointManagementService _endpointManagementService;

    public EndpointsController(
        IEndpointCreationQueryService endpointCreationQueryService,
        IEndpointManagementService endpointManagementService)
    {
        _endpointCreationQueryService = endpointCreationQueryService;
        _endpointManagementService = endpointManagementService;
    }

    [HttpGet("new")]
    public async Task<IActionResult> New(CancellationToken cancellationToken)
    {
        var model = new CreateEndpointPageViewModel();
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
                DependsOnEndpointId = model.DependsOnEndpointId,
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
            foreach (var validationError in result.ValidationErrors)
            {
                var modelKey = validationError.Field switch
                {
                    nameof(CreateEndpointCommand.EndpointName) => nameof(CreateEndpointPageViewModel.EndpointName),
                    nameof(CreateEndpointCommand.Target) => nameof(CreateEndpointPageViewModel.Target),
                    nameof(CreateEndpointCommand.AgentId) => nameof(CreateEndpointPageViewModel.AgentId),
                    nameof(CreateEndpointCommand.DependsOnEndpointId) => nameof(CreateEndpointPageViewModel.DependsOnEndpointId),
                    nameof(CreateEndpointCommand.PingIntervalSeconds) => nameof(CreateEndpointPageViewModel.PingIntervalSeconds),
                    nameof(CreateEndpointCommand.RetryIntervalSeconds) => nameof(CreateEndpointPageViewModel.RetryIntervalSeconds),
                    nameof(CreateEndpointCommand.TimeoutMs) => nameof(CreateEndpointPageViewModel.TimeoutMs),
                    nameof(CreateEndpointCommand.FailureThreshold) => nameof(CreateEndpointPageViewModel.FailureThreshold),
                    nameof(CreateEndpointCommand.RecoveryThreshold) => nameof(CreateEndpointPageViewModel.RecoveryThreshold),
                    _ => string.Empty
                };

                ModelState.AddModelError(modelKey, validationError.Message);
            }

            return View("New", model);
        }

        return Redirect("/status");
    }

    private async Task PopulateOptionsAsync(CreateEndpointPageViewModel model, CancellationToken cancellationToken)
    {
        var options = await _endpointCreationQueryService.GetOptionsAsync(cancellationToken);
        model.AvailableAgents = options.Agents;
        model.AvailableDependencyEndpoints = options.DependencyEndpoints;
    }
}
