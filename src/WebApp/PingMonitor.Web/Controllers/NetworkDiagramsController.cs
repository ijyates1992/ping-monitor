using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Endpoints;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.ViewModels.Endpoints;
using PingMonitor.Web.ViewModels.NetworkDiagrams;

namespace PingMonitor.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Admin)]
[Route("network-diagrams")]
public sealed class NetworkDiagramsController : Controller
{
    private readonly IApplicationSettingsService _applicationSettingsService;
    private readonly IEndpointManagementQueryService _endpointManagementQueryService;

    public NetworkDiagramsController(
        IApplicationSettingsService applicationSettingsService,
        IEndpointManagementQueryService endpointManagementQueryService)
    {
        _applicationSettingsService = applicationSettingsService;
        _endpointManagementQueryService = endpointManagementQueryService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var settings = await _applicationSettingsService.GetCurrentAsync(cancellationToken);
        if (!settings.NetworkDiagramsEnabled)
        {
            return NotFound("Network diagrams are not enabled.");
        }

        var endpoints = await _endpointManagementQueryService.GetManagePageAsync(groupId: null, cancellationToken);

        return View("Index", new NetworkDiagramsEditorPageViewModel
        {
            MonitoredEndpoints = BuildEndpointToolbox(endpoints.Rows)
        });
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
