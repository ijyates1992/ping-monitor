using System.Linq.Expressions;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.Diagnostics;
using PingMonitor.Web.Services.Identity;

namespace PingMonitor.Web.Services.AiTools;

internal sealed class AiMonitoringContextService : IAiMonitoringContextService
{
    private const int MaxEndpointItemsPerState = 10;
    private const int MaxAgentItems = 10;
    private const int MaxRecentStateChanges = 10;
    private static readonly TimeSpan RecentStateChangeWindow = TimeSpan.FromHours(1);

    private readonly PingMonitorDbContext _dbContext;
    private readonly IUserAccessScopeService _userAccessScopeService;
    private readonly IDbActivityScope _dbActivityScope;

    public AiMonitoringContextService(
        PingMonitorDbContext dbContext,
        IUserAccessScopeService userAccessScopeService,
        IDbActivityScope dbActivityScope)
    {
        _dbContext = dbContext;
        _userAccessScopeService = userAccessScopeService;
        _dbActivityScope = dbActivityScope;
    }

    public async Task<AiMonitoringContextResult> GetNetworkHealthSummaryAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        using var scope = _dbActivityScope.BeginScope("Ai.NetworkHealthSummary");

        var nowUtc = DateTimeOffset.UtcNow;
        var isAdmin = await _userAccessScopeService.IsAdminAsync(user);
        var visibleEndpointIds = isAdmin
            ? null
            : (await _userAccessScopeService.GetVisibleEndpointIdsAsync(user, cancellationToken)).ToArray();

        var rows = await GetVisibleStateRowsAsync(visibleEndpointIds, cancellationToken);
        var visibleEndpointIdSet = rows
            .Select(x => x.EndpointId)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var endpointNamesById = rows
            .GroupBy(x => x.EndpointId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().EndpointName, StringComparer.Ordinal);

        var staleAndOfflineAgents = await GetRelevantAgentRowsAsync(isAdmin, rows, cancellationToken);
        var recentTransitions = await GetRecentStateChangesAsync(
            visibleEndpointIds,
            nowUtc - RecentStateChangeWindow,
            cancellationToken);

        var summary = new AiNetworkHealthSummary
        {
            GeneratedAtUtc = nowUtc,
            VisibleEndpointCount = visibleEndpointIdSet.Count,
            VisibleAssignmentCount = rows.Count,
            StateCounts = new AiNetworkStateCounts
            {
                Up = rows.Count(x => x.CurrentState == EndpointStateKind.Up),
                Degraded = rows.Count(x => x.CurrentState == EndpointStateKind.Degraded),
                Down = rows.Count(x => x.CurrentState == EndpointStateKind.Down),
                Suppressed = rows.Count(x => x.CurrentState == EndpointStateKind.Suppressed),
                Unknown = rows.Count(x => x.CurrentState == EndpointStateKind.Unknown)
            },
            DownEndpoints = ToEndpointItems(rows, EndpointStateKind.Down, endpointNamesById),
            DownEndpointOmittedCount = OmittedCount(rows, EndpointStateKind.Down, MaxEndpointItemsPerState),
            DegradedEndpoints = ToEndpointItems(rows, EndpointStateKind.Degraded, endpointNamesById),
            DegradedEndpointOmittedCount = OmittedCount(rows, EndpointStateKind.Degraded, MaxEndpointItemsPerState),
            UnknownEndpoints = ToEndpointItems(rows, EndpointStateKind.Unknown, endpointNamesById),
            UnknownEndpointOmittedCount = OmittedCount(rows, EndpointStateKind.Unknown, MaxEndpointItemsPerState),
            SuppressedEndpoints = ToEndpointItems(rows, EndpointStateKind.Suppressed, endpointNamesById),
            SuppressedEndpointOmittedCount = OmittedCount(rows, EndpointStateKind.Suppressed, MaxEndpointItemsPerState),
            StaleAgents = staleAndOfflineAgents
                .Where(x => x.Status == AgentHealthStatus.Stale)
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.InstanceId, StringComparer.OrdinalIgnoreCase)
                .Take(MaxAgentItems)
                .Select(ToAgentItem)
                .ToArray(),
            StaleAgentOmittedCount = Math.Max(0, staleAndOfflineAgents.Count(x => x.Status == AgentHealthStatus.Stale) - MaxAgentItems),
            OfflineAgents = staleAndOfflineAgents
                .Where(x => x.Status == AgentHealthStatus.Offline)
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.InstanceId, StringComparer.OrdinalIgnoreCase)
                .Take(MaxAgentItems)
                .Select(ToAgentItem)
                .ToArray(),
            OfflineAgentOmittedCount = Math.Max(0, staleAndOfflineAgents.Count(x => x.Status == AgentHealthStatus.Offline) - MaxAgentItems),
            RecentStateChangeCount = recentTransitions.TotalCount,
            RecentStateChanges = recentTransitions.Items,
            RecentStateChangeOmittedCount = Math.Max(0, recentTransitions.TotalCount - MaxRecentStateChanges)
        };

        return AiMonitoringContextResult.Success(summary);
    }

    private async Task<List<StateRow>> GetVisibleStateRowsAsync(
        IReadOnlyCollection<string>? visibleEndpointIds,
        CancellationToken cancellationToken)
    {
        var assignments = _dbContext.MonitorAssignments.AsNoTracking();
        if (visibleEndpointIds is not null)
        {
            assignments = ApplyVisibleEndpointFilter(assignments, visibleEndpointIds);
        }

        return await (
                from assignment in assignments
                join endpoint in _dbContext.Endpoints.AsNoTracking() on assignment.EndpointId equals endpoint.EndpointId
                join agent in _dbContext.Agents.AsNoTracking() on assignment.AgentId equals agent.AgentId
                join endpointState in _dbContext.EndpointStates.AsNoTracking() on assignment.AssignmentId equals endpointState.AssignmentId into endpointStateJoin
                from endpointState in endpointStateJoin.DefaultIfEmpty()
                select new StateRow
                {
                    AssignmentId = assignment.AssignmentId,
                    EndpointId = endpoint.EndpointId,
                    EndpointName = endpoint.Name,
                    Target = endpoint.Target,
                    AgentId = agent.AgentId,
                    AgentName = agent.Name ?? agent.InstanceId,
                    CurrentState = endpointState != null ? endpointState.CurrentState : EndpointStateKind.Unknown,
                    LastChangedUtc = endpointState != null ? endpointState.LastStateChangeUtc : null,
                    LastCheckUtc = endpointState != null ? endpointState.LastCheckUtc : null,
                    SuppressedByEndpointId = endpointState != null ? endpointState.SuppressedByEndpointId : null
                })
            .ToListAsync(cancellationToken);
    }

    private async Task<List<AgentRow>> GetRelevantAgentRowsAsync(
        bool isAdmin,
        IReadOnlyCollection<StateRow> rows,
        CancellationToken cancellationToken)
    {
        var visibleAssignmentCountsByAgentId = rows
            .GroupBy(x => x.AgentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var agents = _dbContext.Agents.AsNoTracking()
            .Where(x => x.Status == AgentHealthStatus.Stale || x.Status == AgentHealthStatus.Offline);

        if (!isAdmin)
        {
            agents = ApplyRelevantAgentFilter(agents, visibleAssignmentCountsByAgentId.Keys.ToArray());
        }

        var agentRows = await agents
            .Select(agent => new AgentRow
            {
                AgentId = agent.AgentId,
                InstanceId = agent.InstanceId,
                Name = agent.Name ?? agent.InstanceId,
                Status = agent.Status,
                LastHeartbeatUtc = agent.LastHeartbeatUtc
            })
            .ToListAsync(cancellationToken);

        return agentRows.Select(agent => new AgentRow
            {
                AgentId = agent.AgentId,
                InstanceId = agent.InstanceId,
                Name = agent.Name,
                Status = agent.Status,
                LastHeartbeatUtc = agent.LastHeartbeatUtc,
                VisibleAssignmentCount = visibleAssignmentCountsByAgentId.GetValueOrDefault(agent.AgentId, 0)
            })
            .ToList();
    }

    private async Task<RecentStateChangeRows> GetRecentStateChangesAsync(
        IReadOnlyCollection<string>? visibleEndpointIds,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        var transitions = _dbContext.StateTransitions.AsNoTracking()
            .Where(x => x.TransitionAtUtc >= cutoffUtc);

        if (visibleEndpointIds is not null)
        {
            transitions = ApplyVisibleEndpointFilter(transitions, visibleEndpointIds);
        }

        var projected =
            from transition in transitions
            join endpoint in _dbContext.Endpoints.AsNoTracking() on transition.EndpointId equals endpoint.EndpointId
            join agent in _dbContext.Agents.AsNoTracking() on transition.AgentId equals agent.AgentId
            select new AiRecentStateChangeSummaryItem
            {
                EndpointId = transition.EndpointId,
                AssignmentId = transition.AssignmentId,
                EndpointName = endpoint.Name,
                AgentId = agent.AgentId,
                AgentName = agent.Name ?? agent.InstanceId,
                PreviousState = transition.PreviousState,
                NewState = transition.NewState,
                TransitionAtUtc = transition.TransitionAtUtc,
                ReasonCode = transition.ReasonCode
            };

        var totalCount = await projected.CountAsync(cancellationToken);
        var items = await projected
            .OrderByDescending(x => x.TransitionAtUtc)
            .ThenBy(x => x.EndpointName)
            .Take(MaxRecentStateChanges)
            .ToArrayAsync(cancellationToken);

        return new RecentStateChangeRows(totalCount, items);
    }

    private static IReadOnlyList<AiEndpointStateSummaryItem> ToEndpointItems(
        IReadOnlyCollection<StateRow> rows,
        EndpointStateKind state,
        IReadOnlyDictionary<string, string> visibleEndpointNamesById)
    {
        return rows
            .Where(x => x.CurrentState == state)
            .OrderBy(x => x.EndpointName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.AgentName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxEndpointItemsPerState)
            .Select(row =>
            {
                var suppressedByEndpointId = !string.IsNullOrWhiteSpace(row.SuppressedByEndpointId) &&
                                             visibleEndpointNamesById.ContainsKey(row.SuppressedByEndpointId)
                    ? row.SuppressedByEndpointId
                    : null;

                return new AiEndpointStateSummaryItem
                {
                    EndpointId = row.EndpointId,
                    AssignmentId = row.AssignmentId,
                    Name = row.EndpointName,
                    Target = row.Target,
                    State = row.CurrentState,
                    LastChangedUtc = row.LastChangedUtc,
                    LastCheckUtc = row.LastCheckUtc,
                    AgentId = row.AgentId,
                    AgentName = row.AgentName,
                    SuppressedByEndpointId = suppressedByEndpointId,
                    SuppressedByEndpointName = suppressedByEndpointId is not null
                        ? visibleEndpointNamesById.GetValueOrDefault(suppressedByEndpointId)
                        : null
                };
            })
            .ToArray();
    }

    private static AiAgentHealthSummaryItem ToAgentItem(AgentRow row) => new()
    {
        AgentId = row.AgentId,
        InstanceId = row.InstanceId,
        Name = row.Name,
        Status = row.Status,
        LastHeartbeatUtc = row.LastHeartbeatUtc,
        VisibleAssignmentCount = row.VisibleAssignmentCount
    };

    private static int OmittedCount(IReadOnlyCollection<StateRow> rows, EndpointStateKind state, int limit)
        => Math.Max(0, rows.Count(x => x.CurrentState == state) - limit);

    private static IQueryable<MonitorAssignment> ApplyVisibleEndpointFilter(
        IQueryable<MonitorAssignment> query,
        IReadOnlyCollection<string> visibleEndpointIds)
        => ApplyStringFilter(query, visibleEndpointIds, assignment => assignment.EndpointId);

    private static IQueryable<StateTransition> ApplyVisibleEndpointFilter(
        IQueryable<StateTransition> query,
        IReadOnlyCollection<string> visibleEndpointIds)
        => ApplyStringFilter(query, visibleEndpointIds, transition => transition.EndpointId);

    private static IQueryable<Agent> ApplyRelevantAgentFilter(
        IQueryable<Agent> query,
        IReadOnlyCollection<string> visibleAgentIds)
        => ApplyStringFilter(query, visibleAgentIds, agent => agent.AgentId);

    private static IQueryable<T> ApplyStringFilter<T>(
        IQueryable<T> query,
        IReadOnlyCollection<string> values,
        Expression<Func<T, string>> propertySelector)
    {
        if (values.Count == 0)
        {
            return query.Where(static _ => false);
        }

        var parameter = propertySelector.Parameters[0];
        Expression? combinedPredicate = null;
        foreach (var value in values)
        {
            var equals = Expression.Equal(propertySelector.Body, Expression.Constant(value));
            combinedPredicate = combinedPredicate is null ? equals : Expression.OrElse(combinedPredicate, equals);
        }

        var lambda = Expression.Lambda<Func<T, bool>>(combinedPredicate!, parameter);
        return query.Where(lambda);
    }

    private sealed class StateRow
    {
        public string AssignmentId { get; init; } = string.Empty;
        public string EndpointId { get; init; } = string.Empty;
        public string EndpointName { get; init; } = string.Empty;
        public string Target { get; init; } = string.Empty;
        public string AgentId { get; init; } = string.Empty;
        public string AgentName { get; init; } = string.Empty;
        public EndpointStateKind CurrentState { get; init; } = EndpointStateKind.Unknown;
        public DateTimeOffset? LastChangedUtc { get; init; }
        public DateTimeOffset? LastCheckUtc { get; init; }
        public string? SuppressedByEndpointId { get; init; }
    }

    private sealed class AgentRow
    {
        public string AgentId { get; init; } = string.Empty;
        public string InstanceId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public AgentHealthStatus Status { get; init; } = AgentHealthStatus.Offline;
        public DateTimeOffset? LastHeartbeatUtc { get; init; }
        public int VisibleAssignmentCount { get; init; }
    }

    private sealed record RecentStateChangeRows(int TotalCount, IReadOnlyList<AiRecentStateChangeSummaryItem> Items);
}
