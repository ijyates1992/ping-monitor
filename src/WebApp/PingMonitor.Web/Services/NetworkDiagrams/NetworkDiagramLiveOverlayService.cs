using System.Linq.Expressions;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.Diagnostics;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.Services.Metrics;

namespace PingMonitor.Web.Services.NetworkDiagrams;

internal sealed class NetworkDiagramLiveOverlayService : INetworkDiagramLiveOverlayService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly INetworkDiagramService _networkDiagramService;
    private readonly IAssignmentMetrics24hService _assignmentMetrics24hService;
    private readonly IUserAccessScopeService _userAccessScopeService;
    private readonly IDbActivityScope _dbActivityScope;

    public NetworkDiagramLiveOverlayService(
        PingMonitorDbContext dbContext,
        INetworkDiagramService networkDiagramService,
        IAssignmentMetrics24hService assignmentMetrics24hService,
        IUserAccessScopeService userAccessScopeService,
        IDbActivityScope dbActivityScope)
    {
        _dbContext = dbContext;
        _networkDiagramService = networkDiagramService;
        _assignmentMetrics24hService = assignmentMetrics24hService;
        _userAccessScopeService = userAccessScopeService;
        _dbActivityScope = dbActivityScope;
    }

    public async Task<NetworkDiagramLiveOverlayResponse> GetOverlayAsync(string diagramId, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        using var scope = _dbActivityScope.BeginScope("NetworkDiagramLiveOverlay");
        var diagram = await _networkDiagramService.LoadAsync(diagramId, cancellationToken);
        if (diagram is null)
        {
            return new NetworkDiagramLiveOverlayResponse
            {
                DiagramId = diagramId,
                RefreshedAtUtc = DateTimeOffset.UtcNow
            };
        }

        var nodeEndpointPairs = diagram.Nodes
            .Where(node => string.Equals(node.NodeType, nameof(NetworkDiagramNodeType.MonitoredEndpoint), StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(node.EndpointId))
            .Select(node => new { node.NodeId, EndpointId = node.EndpointId!.Trim() })
            .Distinct()
            .ToArray();

        if (nodeEndpointPairs.Length == 0)
        {
            return new NetworkDiagramLiveOverlayResponse
            {
                DiagramId = diagram.DiagramId,
                RefreshedAtUtc = DateTimeOffset.UtcNow
            };
        }

        var endpointIds = nodeEndpointPairs.Select(x => x.EndpointId).Distinct(StringComparer.Ordinal).ToArray();
        var isAdmin = await _userAccessScopeService.IsAdminAsync(user);
        if (!isAdmin)
        {
            var visibleEndpointIds = await _userAccessScopeService.GetVisibleEndpointIdsAsync(user, cancellationToken);
            endpointIds = endpointIds.Where(visibleEndpointIds.Contains).ToArray();
        }

        if (endpointIds.Length == 0)
        {
            return new NetworkDiagramLiveOverlayResponse
            {
                DiagramId = diagram.DiagramId,
                RefreshedAtUtc = DateTimeOffset.UtcNow
            };
        }

        var assignmentsQuery = ApplyEndpointFilter(_dbContext.MonitorAssignments.AsNoTracking(), endpointIds);
        var assignmentRows = await (
            from assignment in assignmentsQuery
            join endpoint in _dbContext.Endpoints.AsNoTracking() on assignment.EndpointId equals endpoint.EndpointId
            join agent in _dbContext.Agents.AsNoTracking() on assignment.AgentId equals agent.AgentId
            join state in _dbContext.EndpointStates.AsNoTracking() on assignment.AssignmentId equals state.AssignmentId into stateJoin
            from state in stateJoin.DefaultIfEmpty()
            join suppressedByEndpoint in _dbContext.Endpoints.AsNoTracking() on state!.SuppressedByEndpointId equals suppressedByEndpoint.EndpointId into suppressedByEndpointJoin
            from suppressedByEndpoint in suppressedByEndpointJoin.DefaultIfEmpty()
            orderby endpoint.Name, agent.InstanceId
            select new
            {
                assignment.AssignmentId,
                assignment.EndpointId,
                EndpointName = endpoint.Name,
                endpoint.Target,
                agent.AgentId,
                AgentName = string.IsNullOrWhiteSpace(agent.Name) ? agent.InstanceId : agent.Name,
                State = state != null ? state.CurrentState : EndpointStateKind.Unknown,
                LastCheckUtc = state != null ? state.LastCheckUtc : null,
                SuppressedByEndpointId = state != null ? state.SuppressedByEndpointId : null,
                SuppressedByEndpointName = suppressedByEndpoint != null ? suppressedByEndpoint.Name : null
            }).ToArrayAsync(cancellationToken);

        var metricsByAssignmentId = await _assignmentMetrics24hService.GetSummariesAsync(
            assignmentRows.Select(x => x.AssignmentId).ToArray(),
            cancellationToken);

        var assignmentsByEndpointId = assignmentRows
            .Select(row =>
            {
                metricsByAssignmentId.TryGetValue(row.AssignmentId, out var metrics);
                return new EndpointAssignmentOverlay(row.EndpointId, new NetworkDiagramAssignmentLiveOverlayDto
                {
                    AssignmentId = row.AssignmentId,
                    AgentId = row.AgentId,
                    AgentName = row.AgentName,
                    State = row.State,
                    StateLabel = FormatState(row.State),
                    UptimePercent24h = metrics?.UptimePercent,
                    UptimeDisplay = FormatPercent(metrics?.UptimePercent),
                    LastRttMs = metrics?.LastRttMs,
                    AverageRttMs = metrics?.AverageRttMs,
                    LastCheckUtc = row.LastCheckUtc,
                    LastSuccessfulCheckUtc = metrics?.LastSuccessfulCheckUtc,
                    SuppressedByEndpointId = row.SuppressedByEndpointId,
                    SuppressedByEndpointName = row.SuppressedByEndpointName
                }, row.EndpointName, row.Target);
            })
            .GroupBy(x => x.EndpointId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var nodes = new List<NetworkDiagramNodeLiveOverlayDto>();
        foreach (var pair in nodeEndpointPairs)
        {
            if (!assignmentsByEndpointId.TryGetValue(pair.EndpointId, out var endpointAssignments))
            {
                continue;
            }

            var assignmentDetails = endpointAssignments.Select(x => x.Assignment).ToArray();
            var summaryState = SelectSummaryState(assignmentDetails.Select(x => x.State));
            nodes.Add(new NetworkDiagramNodeLiveOverlayDto
            {
                NodeId = pair.NodeId,
                EndpointId = pair.EndpointId,
                EndpointName = endpointAssignments[0].EndpointName,
                Target = endpointAssignments[0].Target,
                SummaryState = summaryState,
                SummaryStateLabel = FormatState(summaryState),
                UptimePercent24h = SummarizeUptime(assignmentDetails),
                UptimeDisplay = FormatPercent(SummarizeUptime(assignmentDetails)),
                LastRttMs = assignmentDetails.Where(x => x.LastRttMs.HasValue).OrderByDescending(x => x.LastCheckUtc).Select(x => x.LastRttMs).FirstOrDefault(),
                AverageRttMs = AverageNullable(assignmentDetails.Select(x => x.AverageRttMs)),
                LastCheckUtc = assignmentDetails.Select(x => x.LastCheckUtc).Where(x => x.HasValue).DefaultIfEmpty().Max(),
                LastSuccessfulCheckUtc = assignmentDetails.Select(x => x.LastSuccessfulCheckUtc).Where(x => x.HasValue).DefaultIfEmpty().Max(),
                Assignments = assignmentDetails
            });
        }

        return new NetworkDiagramLiveOverlayResponse
        {
            DiagramId = diagram.DiagramId,
            RefreshedAtUtc = DateTimeOffset.UtcNow,
            Nodes = nodes
        };
    }

    private static IQueryable<MonitorAssignment> ApplyEndpointFilter(IQueryable<MonitorAssignment> assignments, IReadOnlyCollection<string> endpointIds)
    {
        if (endpointIds.Count == 0)
        {
            return assignments.Where(static _ => false);
        }

        var parameter = Expression.Parameter(typeof(MonitorAssignment), "assignment");
        var endpointId = Expression.Property(parameter, nameof(MonitorAssignment.EndpointId));
        Expression? predicate = null;
        foreach (var id in endpointIds)
        {
            var equals = Expression.Equal(endpointId, Expression.Constant(id));
            predicate = predicate is null ? equals : Expression.OrElse(predicate, equals);
        }

        return assignments.Where(Expression.Lambda<Func<MonitorAssignment, bool>>(predicate!, parameter));
    }

    private static EndpointStateKind SelectSummaryState(IEnumerable<EndpointStateKind> states)
    {
        var summary = EndpointStateKind.Unknown;
        var priority = -1;
        foreach (var state in states)
        {
            var current = GetSummaryPriority(state);
            if (current > priority)
            {
                summary = state;
                priority = current;
            }
        }

        return summary;
    }

    private static int GetSummaryPriority(EndpointStateKind state) => state switch
    {
        EndpointStateKind.Down => 5,
        EndpointStateKind.Unknown => 4,
        EndpointStateKind.Suppressed => 3,
        EndpointStateKind.Degraded => 2,
        EndpointStateKind.Up => 1,
        _ => 4
    };

    private static string FormatState(EndpointStateKind state) => state switch
    {
        EndpointStateKind.Up => "Up",
        EndpointStateKind.Degraded => "Degraded",
        EndpointStateKind.Down => "Down",
        EndpointStateKind.Suppressed => "Suppressed",
        _ => "Unknown"
    };

    private static double? SummarizeUptime(IReadOnlyCollection<NetworkDiagramAssignmentLiveOverlayDto> assignments)
    {
        var values = assignments.Where(x => x.UptimePercent24h.HasValue).Select(x => x.UptimePercent24h!.Value).ToArray();
        return values.Length == 0 ? null : values.Min();
    }

    private static double? AverageNullable(IEnumerable<double?> values)
    {
        var concrete = values.Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        return concrete.Length == 0 ? null : concrete.Average();
    }

    private static string FormatPercent(double? value) => value.HasValue ? $"{value.Value:0.##}%" : "—";

    private sealed record EndpointAssignmentOverlay(string EndpointId, NetworkDiagramAssignmentLiveOverlayDto Assignment, string EndpointName, string Target);
}
