using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Endpoints;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.Services.NetworkDiagrams;
using PingMonitor.Web.ViewModels.Endpoints;
using PingMonitor.Web.ViewModels.NetworkDiagrams;

namespace PingMonitor.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Admin)]
[Route("network-diagrams")]
public sealed class NetworkDiagramsController : Controller
{
    private readonly IApplicationSettingsService _applicationSettingsService;
    private readonly IEndpointManagementQueryService _endpointManagementQueryService;
    private readonly INetworkDiagramService _networkDiagramService;

    public NetworkDiagramsController(
        IApplicationSettingsService applicationSettingsService,
        IEndpointManagementQueryService endpointManagementQueryService,
        INetworkDiagramService networkDiagramService)
    {
        _applicationSettingsService = applicationSettingsService;
        _endpointManagementQueryService = endpointManagementQueryService;
        _networkDiagramService = networkDiagramService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!await NetworkDiagramsEnabledAsync(cancellationToken))
        {
            return NotFound("Network diagrams are not enabled.");
        }

        var diagrams = await _networkDiagramService.ListAsync(cancellationToken);
        return View("Index", new NetworkDiagramListPageViewModel
        {
            Diagrams = diagrams.Select(x => new NetworkDiagramListItemViewModel
            {
                DiagramId = x.DiagramId,
                Name = x.Name,
                Description = x.Description,
                NodeCount = x.NodeCount,
                LinkCount = x.LinkCount,
                UpdatedAtUtc = x.UpdatedAtUtc
            }).ToArray()
        });
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateNetworkDiagramViewModel model, CancellationToken cancellationToken)
    {
        if (!await NetworkDiagramsEnabledAsync(cancellationToken))
        {
            return NotFound("Network diagrams are not enabled.");
        }

        if (!ModelState.IsValid)
        {
            var diagrams = await _networkDiagramService.ListAsync(cancellationToken);
            return View("Index", new NetworkDiagramListPageViewModel
            {
                CreateDiagram = model,
                Diagrams = diagrams.Select(x => new NetworkDiagramListItemViewModel
                {
                    DiagramId = x.DiagramId,
                    Name = x.Name,
                    Description = x.Description,
                    NodeCount = x.NodeCount,
                    LinkCount = x.LinkCount,
                    UpdatedAtUtc = x.UpdatedAtUtc
                }).ToArray()
            });
        }

        var diagram = await _networkDiagramService.CreateAsync(model.Name, model.Description, User.Identity?.Name, cancellationToken);
        return RedirectToAction(nameof(Edit), new { diagramId = diagram.DiagramId });
    }

    [HttpGet("{diagramId}")]
    public async Task<IActionResult> Edit(string diagramId, CancellationToken cancellationToken)
    {
        if (!await NetworkDiagramsEnabledAsync(cancellationToken))
        {
            return NotFound("Network diagrams are not enabled.");
        }

        var diagram = await _networkDiagramService.GetDiagramAsync(diagramId, cancellationToken);
        if (diagram is null)
        {
            return NotFound("Network diagram was not found.");
        }

        var endpoints = await _endpointManagementQueryService.GetManagePageAsync(groupId: null, cancellationToken);

        return View("Edit", new NetworkDiagramEditorPageViewModel
        {
            PageTitle = diagram.Name,
            DiagramId = diagram.DiagramId,
            DiagramName = diagram.Name,
            DiagramDescription = diagram.Description,
            LoadUrl = Url.Action(nameof(Load), new { diagramId = diagram.DiagramId }) ?? string.Empty,
            SaveUrl = Url.Action(nameof(Save), new { diagramId = diagram.DiagramId }) ?? string.Empty,
            MonitoredEndpoints = BuildEndpointToolbox(endpoints.Rows)
        });
    }

    [HttpGet("{diagramId}/data")]
    public async Task<IActionResult> Load(string diagramId, CancellationToken cancellationToken)
    {
        if (!await NetworkDiagramsEnabledAsync(cancellationToken))
        {
            return NotFound(new { error = "Network diagrams are not enabled." });
        }

        var diagram = await _networkDiagramService.LoadAsync(diagramId, cancellationToken);
        return diagram is null ? NotFound(new { error = "Network diagram was not found." }) : Json(diagram);
    }

    [HttpPost("{diagramId}/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string diagramId, [FromBody] NetworkDiagramSaveRequest request, CancellationToken cancellationToken)
    {
        if (!await NetworkDiagramsEnabledAsync(cancellationToken))
        {
            return NotFound(new { error = "Network diagrams are not enabled." });
        }

        try
        {
            var saved = await _networkDiagramService.SaveAsync(diagramId, request, User.Identity?.Name, cancellationToken);
            return Json(saved);
        }
        catch (NetworkDiagramValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{diagramId}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string diagramId, CancellationToken cancellationToken)
    {
        if (!await NetworkDiagramsEnabledAsync(cancellationToken))
        {
            return NotFound("Network diagrams are not enabled.");
        }

        await _networkDiagramService.DeleteAsync(diagramId, cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> NetworkDiagramsEnabledAsync(CancellationToken cancellationToken)
    {
        var settings = await _applicationSettingsService.GetCurrentAsync(cancellationToken);
        return settings.NetworkDiagramsEnabled;
    }

    private static IReadOnlyList<NetworkDiagramEndpointToolboxItemViewModel> BuildEndpointToolbox(
        IReadOnlyList<ManageEndpointRowViewModel> rows)
    {
        return rows
            .GroupBy(row => row.EndpointId, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.OrderBy(row => row.EndpointName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(row => row.Target, StringComparer.OrdinalIgnoreCase)
                    .First();

                return new NetworkDiagramEndpointToolboxItemViewModel
                {
                    EndpointId = first.EndpointId,
                    Name = first.EndpointName,
                    Target = first.Target,
                    IconKey = first.IconKey,
                    SummaryState = SelectSummaryState(group.Select(row => row.CurrentState))
                };
            })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Target, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static EndpointStateKind SelectSummaryState(IEnumerable<EndpointStateKind> states)
    {
        var summary = EndpointStateKind.Unknown;
        var priority = -1;

        foreach (var state in states)
        {
            var statePriority = GetSummaryPriority(state);
            if (statePriority > priority)
            {
                summary = state;
                priority = statePriority;
            }
        }

        return summary;
    }

    private static int GetSummaryPriority(EndpointStateKind state)
    {
        return state switch
        {
            EndpointStateKind.Down => 4,
            EndpointStateKind.Suppressed => 3,
            EndpointStateKind.Degraded => 2,
            EndpointStateKind.Unknown => 1,
            EndpointStateKind.Up => 0,
            _ => 1
        };
    }
}
