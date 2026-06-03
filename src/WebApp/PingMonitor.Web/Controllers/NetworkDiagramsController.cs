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

[Authorize]
[Route("network-diagrams")]
public sealed class NetworkDiagramsController : Controller
{
    private readonly IApplicationSettingsService _applicationSettingsService;
    private readonly IEndpointManagementQueryService _endpointManagementQueryService;
    private readonly INetworkDiagramService _networkDiagramService;
    private readonly INetworkDiagramPdfExportService _pdfExportService;
    private readonly INetworkDiagramLiveOverlayService _liveOverlayService;
    private readonly IUserAccessScopeService _userAccessScopeService;

    public NetworkDiagramsController(
        IApplicationSettingsService applicationSettingsService,
        IEndpointManagementQueryService endpointManagementQueryService,
        INetworkDiagramService networkDiagramService,
        INetworkDiagramPdfExportService pdfExportService,
        INetworkDiagramLiveOverlayService liveOverlayService,
        IUserAccessScopeService userAccessScopeService)
    {
        _applicationSettingsService = applicationSettingsService;
        _endpointManagementQueryService = endpointManagementQueryService;
        _networkDiagramService = networkDiagramService;
        _pdfExportService = pdfExportService;
        _liveOverlayService = liveOverlayService;
        _userAccessScopeService = userAccessScopeService;
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
            IsAdmin = await IsAdminAsync(),
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

    [Authorize(Roles = ApplicationRoles.Admin)]
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
                IsAdmin = await IsAdminAsync(),
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
    public async Task<IActionResult> ViewDiagram(string diagramId, CancellationToken cancellationToken)
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

        var isAdmin = await IsAdminAsync();
        return View("View", new NetworkDiagramViewerPageViewModel
        {
            PageTitle = diagram.Name,
            DiagramId = diagram.DiagramId,
            DiagramName = diagram.Name,
            DiagramDescription = diagram.Description,
            LoadUrl = Url?.Action(nameof(Load), new { diagramId = diagram.DiagramId }) ?? string.Empty,
            LiveDataUrl = Url?.Action(nameof(LiveData), new { diagramId = diagram.DiagramId }) ?? string.Empty,
            EditUrl = isAdmin ? Url?.Action(nameof(Edit), new { diagramId = diagram.DiagramId }) : null,
            ExportPdfUrl = isAdmin ? Url?.Action(nameof(ExportPdf), new { diagramId = diagram.DiagramId }) : null,
            IsAdmin = isAdmin
        });
    }

    [Authorize(Roles = ApplicationRoles.Admin)]
    [HttpGet("{diagramId}/edit")]
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
            LoadUrl = Url?.Action(nameof(Load), new { diagramId = diagram.DiagramId }) ?? string.Empty,
            SaveUrl = Url?.Action(nameof(Save), new { diagramId = diagram.DiagramId }) ?? string.Empty,
            ExportPdfUrl = Url?.Action(nameof(ExportPdf), new { diagramId = diagram.DiagramId }) ?? string.Empty,
            ViewerUrl = Url?.Action(nameof(ViewDiagram), new { diagramId = diagram.DiagramId }) ?? string.Empty,
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
        if (diagram is null)
        {
            return NotFound(new { error = "Network diagram was not found." });
        }

        if (!await IsAdminAsync())
        {
            diagram = await FilterDiagramForViewerAsync(diagram, cancellationToken);
        }

        return Json(diagram);
    }

    [HttpGet("{diagramId}/live-data")]
    public async Task<IActionResult> LiveData(string diagramId, CancellationToken cancellationToken)
    {
        if (!await NetworkDiagramsEnabledAsync(cancellationToken))
        {
            return NotFound(new { error = "Network diagrams are not enabled." });
        }

        var diagram = await _networkDiagramService.GetDiagramAsync(diagramId, cancellationToken);
        if (diagram is null)
        {
            return NotFound(new { error = "Network diagram was not found." });
        }

        return Json(await _liveOverlayService.GetOverlayAsync(diagramId, User, cancellationToken));
    }

    [Authorize(Roles = ApplicationRoles.Admin)]
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


    [Authorize(Roles = ApplicationRoles.Admin)]
    [HttpGet("{diagramId}/export/pdf")]
    public async Task<IActionResult> ExportPdf(string diagramId, [FromQuery] string paper = "A4", CancellationToken cancellationToken = default)
    {
        if (!await NetworkDiagramsEnabledAsync(cancellationToken))
        {
            return NotFound("Network diagrams are not enabled.");
        }

        var diagram = await _networkDiagramService.LoadAsync(diagramId, cancellationToken);
        if (diagram is null)
        {
            return NotFound("Network diagram was not found.");
        }

        var export = _pdfExportService.Export(diagram, new NetworkDiagramPdfExportOptions(paper, DateTimeOffset.UtcNow));
        return File(export.Content, export.ContentType, export.FileName);
    }

    [Authorize(Roles = ApplicationRoles.Admin)]
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


    private async Task<bool> IsAdminAsync() => await _userAccessScopeService.IsAdminAsync(User);

    private async Task<NetworkDiagramDto> FilterDiagramForViewerAsync(NetworkDiagramDto diagram, CancellationToken cancellationToken)
    {
        var visibleEndpointIds = await _userAccessScopeService.GetVisibleEndpointIdsAsync(User, cancellationToken);
        var visibleNodes = diagram.Nodes
            .Where(node => !string.Equals(node.NodeType, nameof(NetworkDiagramNodeType.MonitoredEndpoint), StringComparison.Ordinal) ||
                (!string.IsNullOrWhiteSpace(node.EndpointId) && visibleEndpointIds.Contains(node.EndpointId)))
            .ToArray();
        var visibleNodeIds = visibleNodes.Select(node => node.NodeId).ToHashSet(StringComparer.Ordinal);

        return new NetworkDiagramDto
        {
            DiagramId = diagram.DiagramId,
            Name = diagram.Name,
            Description = diagram.Description,
            CanvasWidth = diagram.CanvasWidth,
            CanvasHeight = diagram.CanvasHeight,
            ViewportPanX = diagram.ViewportPanX,
            ViewportPanY = diagram.ViewportPanY,
            ViewportZoom = diagram.ViewportZoom,
            UpdatedAtUtc = diagram.UpdatedAtUtc,
            Nodes = visibleNodes,
            Links = diagram.Links.Where(link => visibleNodeIds.Contains(link.SourceNodeId) && visibleNodeIds.Contains(link.TargetNodeId)).ToArray()
        };
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
            EndpointStateKind.Down => 5,
            EndpointStateKind.Unknown => 4,
            EndpointStateKind.Suppressed => 3,
            EndpointStateKind.Degraded => 2,
            EndpointStateKind.Up => 1,
            _ => 4
        };
    }
}
