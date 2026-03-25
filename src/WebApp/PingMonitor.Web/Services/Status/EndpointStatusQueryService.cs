using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.ViewModels.Status;

namespace PingMonitor.Web.Services.Status;

internal sealed class EndpointStatusQueryService : IEndpointStatusQueryService
{
    private readonly PingMonitorDbContext _dbContext;

    public EndpointStatusQueryService(PingMonitorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EndpointStatusPageViewModel> GetStatusPageAsync(
        string? state,
        string? agent,
        string? search,
        CancellationToken cancellationToken)
    {
        var normalizedState = Normalize(state);
        var normalizedAgent = Normalize(agent);
        var normalizedSearch = Normalize(search);
        var parsedState = TryParseState(normalizedState);

        var baseQuery =
            from assignment in _dbContext.MonitorAssignments.AsNoTracking()
            join endpoint in _dbContext.Endpoints.AsNoTracking() on assignment.EndpointId equals endpoint.EndpointId
            join assignmentAgent in _dbContext.Agents.AsNoTracking() on assignment.AgentId equals assignmentAgent.AgentId
            join endpointState in _dbContext.EndpointStates.AsNoTracking() on assignment.AssignmentId equals endpointState.AssignmentId into endpointStateJoin
            from endpointState in endpointStateJoin.DefaultIfEmpty()
            join suppressedByEndpoint in _dbContext.Endpoints.AsNoTracking() on endpointState!.SuppressedByEndpointId equals suppressedByEndpoint.EndpointId into suppressedByEndpointJoin
            from suppressedByEndpoint in suppressedByEndpointJoin.DefaultIfEmpty()
            select new EndpointStatusRowViewModel
            {
                AssignmentId = assignment.AssignmentId,
                EndpointId = endpoint.EndpointId,
                EndpointName = endpoint.Name,
                Target = endpoint.Target,
                AgentId = assignmentAgent.AgentId,
                AgentInstanceId = assignmentAgent.InstanceId,
                AgentName = assignmentAgent.Name ?? assignmentAgent.InstanceId,
                CurrentState = endpointState != null ? endpointState.CurrentState : EndpointStateKind.Unknown,
                LastCheckUtc = endpointState != null ? endpointState.LastCheckUtc : null,
                LastStateChangeUtc = endpointState != null ? endpointState.LastStateChangeUtc : null,
                ConsecutiveFailureCount = endpointState != null ? endpointState.ConsecutiveFailureCount : 0,
                ConsecutiveSuccessCount = endpointState != null ? endpointState.ConsecutiveSuccessCount : 0,
                CheckType = assignment.CheckType.ToString(),
                AssignmentEnabled = assignment.Enabled,
                EndpointEnabled = endpoint.Enabled,
                SuppressedByEndpointId = endpointState != null ? endpointState.SuppressedByEndpointId : null,
                SuppressedByEndpointName = suppressedByEndpoint != null ? suppressedByEndpoint.Name : null
            };

        if (parsedState.HasValue)
        {
            baseQuery = baseQuery.Where(row => row.CurrentState == parsedState.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedAgent))
        {
            baseQuery = baseQuery.Where(row => row.AgentInstanceId == normalizedAgent);
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            baseQuery = baseQuery.Where(row =>
                EF.Functions.Like(row.EndpointName, $"%{normalizedSearch}%") ||
                EF.Functions.Like(row.Target, $"%{normalizedSearch}%"));
        }

        var availableAgents = await _dbContext.Agents.AsNoTracking()
            .OrderBy(x => x.InstanceId)
            .Select(x => x.InstanceId)
            .ToArrayAsync(cancellationToken);

        var rows = await baseQuery
            .OrderBy(row => row.CurrentState)
            .ThenBy(row => row.EndpointName)
            .ThenBy(row => row.AgentInstanceId)
            .ToArrayAsync(cancellationToken);

        var endpointIds = rows.Select(x => x.EndpointId).Distinct(StringComparer.Ordinal).ToArray();
        var endpointNames = await _dbContext.Endpoints.AsNoTracking()
            .ToDictionaryAsync(x => x.EndpointId, x => x.Name, cancellationToken);
        var dependencyLookup = endpointIds.Length == 0
            ? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            : await _dbContext.EndpointDependencies.AsNoTracking()
                .Where(x => endpointIds.Contains(x.EndpointId))
                .GroupBy(x => x.EndpointId)
                .ToDictionaryAsync(
                    group => group.Key,
                    group => (IReadOnlyList<string>)group.Select(x => x.DependsOnEndpointId).OrderBy(x => x).ToArray(),
                    cancellationToken);

        var projectedRows = rows.Select(row =>
        {
            var parentIds = dependencyLookup.GetValueOrDefault(row.EndpointId, Array.Empty<string>());
            return new EndpointStatusRowViewModel
            {
                AssignmentId = row.AssignmentId,
                EndpointId = row.EndpointId,
                EndpointName = row.EndpointName,
                Target = row.Target,
                AgentId = row.AgentId,
                AgentInstanceId = row.AgentInstanceId,
                AgentName = row.AgentName,
                CurrentState = row.CurrentState,
                LastCheckUtc = row.LastCheckUtc,
                LastStateChangeUtc = row.LastStateChangeUtc,
                ConsecutiveFailureCount = row.ConsecutiveFailureCount,
                ConsecutiveSuccessCount = row.ConsecutiveSuccessCount,
                CheckType = row.CheckType,
                AssignmentEnabled = row.AssignmentEnabled,
                EndpointEnabled = row.EndpointEnabled,
                ParentEndpointIds = parentIds,
                ParentEndpointNames = parentIds.Select(x => endpointNames.GetValueOrDefault(x, x)).OrderBy(x => x).ToArray(),
                SuppressedByEndpointId = row.SuppressedByEndpointId,
                SuppressedByEndpointName = row.SuppressedByEndpointName
            };
        }).ToArray();

        return new EndpointStatusPageViewModel
        {
            Summary = new EndpointStatusSummaryViewModel
            {
                TotalAssignments = projectedRows.Length,
                UnknownCount = projectedRows.Count(row => row.CurrentState == EndpointStateKind.Unknown),
                UpCount = projectedRows.Count(row => row.CurrentState == EndpointStateKind.Up),
                DegradedCount = projectedRows.Count(row => row.CurrentState == EndpointStateKind.Degraded),
                DownCount = projectedRows.Count(row => row.CurrentState == EndpointStateKind.Down),
                SuppressedCount = projectedRows.Count(row => row.CurrentState == EndpointStateKind.Suppressed)
            },
            Filters = new EndpointStatusFiltersViewModel
            {
                State = normalizedState,
                Agent = normalizedAgent,
                Search = normalizedSearch,
                AvailableAgents = availableAgents
            },
            Rows = projectedRows
        };
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static EndpointStateKind? TryParseState(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Enum.TryParse<EndpointStateKind>(value, ignoreCase: true, out var parsedValue)
            ? parsedValue
            : null;
    }
}
