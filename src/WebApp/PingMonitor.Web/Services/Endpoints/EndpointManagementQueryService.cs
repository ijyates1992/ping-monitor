using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.Metrics;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.Services.Diagnostics;
using PingMonitor.Web.ViewModels.Endpoints;

namespace PingMonitor.Web.Services.Endpoints;

internal sealed class EndpointManagementQueryService : IEndpointManagementQueryService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly IAssignmentMetrics24hService _assignmentMetrics24hService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUserAccessScopeService _userAccessScopeService;
    private readonly IDbActivityScope _dbActivityScope;

    public EndpointManagementQueryService(PingMonitorDbContext dbContext, IAssignmentMetrics24hService assignmentMetrics24hService, IHttpContextAccessor httpContextAccessor, IUserAccessScopeService userAccessScopeService, IDbActivityScope dbActivityScope)
    {
        _dbContext = dbContext;
        _assignmentMetrics24hService = assignmentMetrics24hService;
        _httpContextAccessor = httpContextAccessor;
        _userAccessScopeService = userAccessScopeService;
        _dbActivityScope = dbActivityScope;
    }

    public async Task<ManageEndpointsPageViewModel> GetManagePageAsync(string? groupId, CancellationToken cancellationToken)
    {
        using var scope = _dbActivityScope.BeginScope("EndpointsPage");
        var normalizedGroupId = string.IsNullOrWhiteSpace(groupId) ? null : groupId.Trim();
        var principal = _httpContextAccessor.HttpContext?.User;
        var isAdmin = principal is not null && await _userAccessScopeService.IsAdminAsync(principal);
        var visibleEndpointIds = isAdmin || principal is null ? null : await _userAccessScopeService.GetVisibleEndpointIdsAsync(principal, cancellationToken);

        var rows = await (
            from assignment in _dbContext.MonitorAssignments.AsNoTracking()
            join endpoint in _dbContext.Endpoints.AsNoTracking() on assignment.EndpointId equals endpoint.EndpointId
            join agent in _dbContext.Agents.AsNoTracking() on assignment.AgentId equals agent.AgentId
            join state in _dbContext.EndpointStates.AsNoTracking() on assignment.AssignmentId equals state.AssignmentId into stateJoin
            from state in stateJoin.DefaultIfEmpty()
            orderby endpoint.Name, agent.InstanceId
            select new
            {
                assignment.AssignmentId,
                endpoint.EndpointId,
                EndpointName = endpoint.Name,
                IconKey = endpoint.IconKey,
                endpoint.Target,
                AgentDisplay = string.IsNullOrWhiteSpace(agent.Name)
                    ? agent.InstanceId
                    : $"{agent.Name} ({agent.InstanceId})",
                endpoint.Enabled,
                AssignmentEnabled = assignment.Enabled,
                assignment.PingIntervalSeconds,
                assignment.RetryIntervalSeconds,
                assignment.TimeoutMs,
                assignment.FailureThreshold,
                assignment.RecoveryThreshold,
                CurrentState = state != null ? state.CurrentState : EndpointStateKind.Unknown
            }).ToArrayAsync(cancellationToken);

        if (visibleEndpointIds is not null)
        {
            rows = rows.Where(x => visibleEndpointIds.Contains(x.EndpointId)).ToArray();
        }

        var groupLookup = (await _dbContext.EndpointGroupMemberships.AsNoTracking().ToArrayAsync(cancellationToken))
            .GroupBy(x => x.EndpointId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(x => x.GroupId).ToArray(), StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(normalizedGroupId))
        {
            rows = rows.Where(x => groupLookup.GetValueOrDefault(x.EndpointId, Array.Empty<string>()).Contains(normalizedGroupId, StringComparer.Ordinal)).ToArray();
        }

        var endpointIds = rows.Select(x => x.EndpointId).Distinct(StringComparer.Ordinal).ToArray();
        var endpointIdSet = endpointIds.ToHashSet(StringComparer.Ordinal);
        var endpointNames = await _dbContext.Endpoints.AsNoTracking()
            .ToDictionaryAsync(x => x.EndpointId, x => x.Name, cancellationToken);
        var groups = await _dbContext.Groups.AsNoTracking()
            .OrderBy(x => x.Name)
            .ToArrayAsync(cancellationToken);

        var groupNameLookup = groups.ToDictionary(x => x.GroupId, x => x.Name, StringComparer.Ordinal);

        var dependencies = await _dbContext.EndpointDependencies.AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var dependencyLookup = dependencies
            .Where(x => endpointIdSet.Contains(x.EndpointId))
            .GroupBy(x => x.EndpointId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(x => x.DependsOnEndpointId).ToArray(),
                StringComparer.Ordinal);

        var assignmentIds = rows.Select(x => x.AssignmentId).ToArray();
        var metricsByAssignmentId = await _assignmentMetrics24hService.GetSummariesAsync(
            assignmentIds,
            cancellationToken);

        var missingSummaryAssignmentIds = assignmentIds
            .Where(assignmentId => !metricsByAssignmentId.ContainsKey(assignmentId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (missingSummaryAssignmentIds.Length > 0)
        {
            await _assignmentMetrics24hService.RefreshAssignmentsAsync(missingSummaryAssignmentIds, cancellationToken);
            metricsByAssignmentId = await _assignmentMetrics24hService.GetSummariesAsync(assignmentIds, cancellationToken);
        }

        return new ManageEndpointsPageViewModel
        {
            GroupId = normalizedGroupId,
            AvailableGroups = groups.Select(x => new EndpointGroupOptionViewModel { GroupId = x.GroupId, Name = x.Name }).ToArray(),
            Rows = rows.Select(row =>
            {
                metricsByAssignmentId.TryGetValue(row.AssignmentId, out var metrics);
                return new ManageEndpointRowViewModel
                {
                    LastRttMs = metrics?.LastRttMs,
                    AverageRttMs = metrics?.AverageRttMs,
                    HighestRttMs = metrics?.HighestRttMs,
                    LowestRttMs = metrics?.LowestRttMs,
                    JitterMs = metrics?.JitterMs,
                AssignmentId = row.AssignmentId,
                EndpointId = row.EndpointId,
                EndpointName = row.EndpointName,
                IconKey = EndpointIconCatalog.Normalize(row.IconKey),
                Target = row.Target,
                AgentDisplay = row.AgentDisplay,
                DependencyEndpointNames = dependencyLookup.GetValueOrDefault(row.EndpointId, Array.Empty<string>())
                    .Select(endpointId => endpointNames.GetValueOrDefault(endpointId, endpointId))
                    .OrderBy(x => x)
                    .ToArray(),
                GroupNames = groupLookup.GetValueOrDefault(row.EndpointId, Array.Empty<string>())
                    .Select(selectedGroupId => groupNameLookup.GetValueOrDefault(selectedGroupId, selectedGroupId))
                    .OrderBy(x => x)
                    .ToArray(),
                EndpointEnabled = row.Enabled,
                AssignmentEnabled = row.AssignmentEnabled,
                PingIntervalSeconds = row.PingIntervalSeconds,
                RetryIntervalSeconds = row.RetryIntervalSeconds,
                TimeoutMs = row.TimeoutMs,
                FailureThreshold = row.FailureThreshold,
                RecoveryThreshold = row.RecoveryThreshold,
                CurrentState = row.CurrentState
                };
            }).ToArray()
        };
    }

    public async Task<EditEndpointOptionsViewModel> GetEditOptionsAsync(string assignmentId, CancellationToken cancellationToken)
    {
        using var scope = _dbActivityScope.BeginScope("EndpointsPage");
        var normalizedAssignmentId = assignmentId.Trim();

        var endpointId = await _dbContext.MonitorAssignments.AsNoTracking()
            .Where(x => x.AssignmentId == normalizedAssignmentId)
            .Select(x => x.EndpointId)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(endpointId))
        {
            return new EditEndpointOptionsViewModel();
        }

        var agents = await _dbContext.Agents.AsNoTracking()
            .Where(x => x.Enabled && !x.ApiKeyRevoked)
            .OrderBy(x => x.InstanceId)
            .Select(x => new EndpointAgentOptionViewModel
            {
                AgentId = x.AgentId,
                DisplayName = string.IsNullOrWhiteSpace(x.Name)
                    ? x.InstanceId
                    : $"{x.Name} ({x.InstanceId})"
            })
            .ToArrayAsync(cancellationToken);

        var dependencies = await _dbContext.Endpoints.AsNoTracking()
            .Where(x => x.EndpointId != endpointId)
            .OrderBy(x => x.Name)
            .Select(x => new EndpointDependencyOptionViewModel
            {
                EndpointId = x.EndpointId,
                EndpointName = x.Name
            })
            .ToArrayAsync(cancellationToken);

        var groups = await _dbContext.Groups.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new EndpointGroupOptionViewModel
            {
                GroupId = x.GroupId,
                Name = x.Name
            })
            .ToArrayAsync(cancellationToken);

        return new EditEndpointOptionsViewModel
        {
            Agents = agents,
            Dependencies = dependencies,
            Groups = groups
        };
    }

    public async Task<RemoveEndpointDetails?> GetRemoveDetailsAsync(string assignmentId, CancellationToken cancellationToken)
    {
        using var scope = _dbActivityScope.BeginScope("EndpointsPage");
        var normalizedAssignmentId = assignmentId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedAssignmentId))
        {
            return null;
        }

        var details = await (
            from assignment in _dbContext.MonitorAssignments.AsNoTracking()
            join endpoint in _dbContext.Endpoints.AsNoTracking() on assignment.EndpointId equals endpoint.EndpointId
            join agent in _dbContext.Agents.AsNoTracking() on assignment.AgentId equals agent.AgentId
            where assignment.AssignmentId == normalizedAssignmentId
            select new RemoveEndpointDetails
            {
                AssignmentId = assignment.AssignmentId,
                EndpointId = endpoint.EndpointId,
                EndpointName = endpoint.Name,
                Target = endpoint.Target,
                AgentDisplay = string.IsNullOrWhiteSpace(agent.Name)
                    ? agent.InstanceId
                    : $"{agent.Name} ({agent.InstanceId})"
            })
            .SingleOrDefaultAsync(cancellationToken);

        return details;
    }
}
