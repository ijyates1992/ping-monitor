using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Endpoints;
using PingMonitor.Web.Services.Groups;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.Services.EventLogs;
using PingMonitor.Web.ViewModels.Endpoints;

namespace PingMonitor.Web.Controllers;

[Authorize]
[Route("endpoints")]
public sealed class EndpointsController : Controller
{
    private const string RemoveConfirmationKeyword = "REMOVE";
    private const string StatusMessageKey = "Endpoints.StatusMessage";
    private const string ErrorMessageKey = "Endpoints.ErrorMessage";

    private readonly IEndpointCreationQueryService _endpointCreationQueryService;
    private readonly IEndpointManagementQueryService _endpointManagementQueryService;
    private readonly IEndpointManagementService _endpointManagementService;
    private readonly IApplicationSettingsService _applicationSettingsService;
    private readonly IGroupManagementService _groupManagementService;
    private readonly IEndpointPerformanceQueryService _endpointPerformanceQueryService;
    private readonly IUserAccessScopeService _userAccessScopeService;
    private readonly IEventLogQueryService _eventLogQueryService;

    public EndpointsController(
        IEndpointCreationQueryService endpointCreationQueryService,
        IEndpointManagementQueryService endpointManagementQueryService,
        IEndpointManagementService endpointManagementService,
        IApplicationSettingsService applicationSettingsService,
        IGroupManagementService groupManagementService,
        IEndpointPerformanceQueryService endpointPerformanceQueryService,
        IUserAccessScopeService userAccessScopeService,
        IEventLogQueryService eventLogQueryService)
    {
        _endpointCreationQueryService = endpointCreationQueryService;
        _endpointManagementQueryService = endpointManagementQueryService;
        _endpointManagementService = endpointManagementService;
        _applicationSettingsService = applicationSettingsService;
        _groupManagementService = groupManagementService;
        _endpointPerformanceQueryService = endpointPerformanceQueryService;
        _userAccessScopeService = userAccessScopeService;
        _eventLogQueryService = eventLogQueryService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] string? groupId, CancellationToken cancellationToken)
    {
        var model = await _endpointManagementQueryService.GetManagePageAsync(groupId, cancellationToken);
        model.StatusMessage = TempData[StatusMessageKey] as string;
        model.ErrorMessage = TempData[ErrorMessageKey] as string;
        return View("Index", model);
    }

    [HttpGet("refresh/assignments")]
    public async Task<IActionResult> RefreshAssignments([FromQuery] string? groupId, CancellationToken cancellationToken)
    {
        var model = await _endpointManagementQueryService.GetManagePageAsync(groupId, cancellationToken);
        return PartialView("_AssignmentsSection", model);
    }

    [Authorize(Roles = ApplicationRoles.Admin)]
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

    [Authorize(Roles = ApplicationRoles.Admin)]
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
                IconKey = model.IconKey,
                AgentId = model.AgentId,
                DependsOnEndpointIds = model.DependsOnEndpointIds,
                GroupIds = model.GroupIds,
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

    [Authorize(Roles = ApplicationRoles.Admin)]
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
            IconKey = editModel.IconKey,
            AgentId = editModel.AgentId,
            DependsOnEndpointIds = editModel.DependsOnEndpointIds.ToList(),
            GroupIds = editModel.GroupIds.ToList(),
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

    [Authorize(Roles = ApplicationRoles.Admin)]
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
                IconKey = model.IconKey,
                AgentId = model.AgentId,
                DependsOnEndpointIds = model.DependsOnEndpointIds,
                GroupIds = model.GroupIds,
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

    [HttpGet("{assignmentId}/performance")]
    public async Task<IActionResult> Performance([FromRoute] string assignmentId, [FromQuery] string? range, CancellationToken cancellationToken)
    {
        var model = await _endpointPerformanceQueryService.GetPerformancePageAsync(assignmentId, range, cancellationToken);
        if (model is null)
        {
            return NotFound();
        }

        return View("Performance", model);
    }

    [HttpGet("{endpointId}/history")]
    public async Task<IActionResult> History(
        [FromRoute] string endpointId,
        [FromQuery] string? search,
        [FromQuery] string? eventType,
        [FromQuery] DateTimeOffset? dateFromUtc,
        [FromQuery] DateTimeOffset? dateToUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var model = await _eventLogQueryService.GetEndpointHistoryPageAsync(endpointId, search, eventType, dateFromUtc, dateToUtc, page, pageSize, cancellationToken);
        if (model is null)
        {
            return NotFound();
        }

        return View("History", model);
    }

    [Authorize(Roles = ApplicationRoles.Admin)]
    [HttpGet("{assignmentId}/remove")]
    public async Task<IActionResult> Remove([FromRoute] string assignmentId, CancellationToken cancellationToken)
    {
        var details = await _endpointManagementQueryService.GetRemoveDetailsAsync(assignmentId, cancellationToken);
        if (details is null)
        {
            return NotFound();
        }

        return View("Remove", BuildRemoveViewModel(details));
    }

    [Authorize(Roles = ApplicationRoles.Admin)]
    [HttpPost("{assignmentId}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(
        [FromRoute] string assignmentId,
        [FromForm] RemoveEndpointPageViewModel model,
        CancellationToken cancellationToken)
    {
        var details = await _endpointManagementQueryService.GetRemoveDetailsAsync(assignmentId, cancellationToken);
        if (details is null)
        {
            return NotFound();
        }

        var viewModel = BuildRemoveViewModel(details, model.ConfirmationText);
        if (!string.Equals(model.ConfirmationText?.Trim(), RemoveConfirmationKeyword, StringComparison.Ordinal))
        {
            viewModel.ErrorMessage = $"Type {RemoveConfirmationKeyword} to confirm removing this endpoint.";
            return View("Remove", viewModel);
        }

        var result = await _endpointManagementService.RemoveByAssignmentAsync(assignmentId, cancellationToken);
        if (!result.Found)
        {
            return NotFound();
        }

        TempData[StatusMessageKey] = result.Changed
            ? "Endpoint removed. Endpoint and related assignments are disabled. Historical data is preserved."
            : "Endpoint was already removed.";
        return Redirect("/endpoints");
    }

    private async Task PopulateOptionsAsync(CreateEndpointPageViewModel model, CancellationToken cancellationToken)
    {
        var options = await _endpointCreationQueryService.GetOptionsAsync(cancellationToken);
        model.AvailableAgents = options.Agents;
        model.AvailableDependencyEndpoints = options.DependencyEndpoints;
        model.AvailableGroups = await _groupManagementService.GetGroupOptionsAsync(cancellationToken);
        model.AvailableIcons = EndpointIconCatalog.Options
            .Select(x => new EndpointIconOptionViewModel { Key = x.Key, DisplayName = x.DisplayName, Symbol = x.Symbol })
            .ToArray();
        model.IconKey = EndpointIconCatalog.Normalize(model.IconKey);
    }

    private async Task PopulateEditOptionsAsync(EditEndpointPageViewModel model, CancellationToken cancellationToken)
    {
        var options = await _endpointManagementQueryService.GetEditOptionsAsync(model.AssignmentId, cancellationToken);
        model.AvailableAgents = options.Agents;
        model.AvailableDependencies = options.Dependencies;
        model.AvailableGroups = await _groupManagementService.GetGroupOptionsAsync(cancellationToken);
        model.AvailableIcons = EndpointIconCatalog.Options
            .Select(x => new EndpointIconOptionViewModel { Key = x.Key, DisplayName = x.DisplayName, Symbol = x.Symbol })
            .ToArray();
        model.IconKey = EndpointIconCatalog.Normalize(model.IconKey);
    }

    private void AddValidationErrorsToModelState(IReadOnlyList<EndpointValidationError> validationErrors)
    {
        foreach (var validationError in validationErrors)
        {
            var modelKey = validationError.Field;
            ModelState.AddModelError(modelKey, validationError.Message);
        }
    }

    private static RemoveEndpointPageViewModel BuildRemoveViewModel(RemoveEndpointDetails details, string? confirmationText = null)
    {
        return new RemoveEndpointPageViewModel
        {
            AssignmentId = details.AssignmentId,
            EndpointId = details.EndpointId,
            EndpointName = details.EndpointName,
            Target = details.Target,
            AgentDisplay = details.AgentDisplay,
            ConfirmationText = confirmationText ?? string.Empty,
            RequiredConfirmationText = RemoveConfirmationKeyword
        };
    }
}
