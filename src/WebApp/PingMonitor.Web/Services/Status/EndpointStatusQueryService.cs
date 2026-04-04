using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.Endpoints;
using PingMonitor.Web.Services.Metrics;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.Services.EventLogs;
using PingMonitor.Web.Services.Diagnostics;
using PingMonitor.Web.ViewModels.Status;

namespace PingMonitor.Web.Services.Status;

internal sealed class EndpointStatusQueryService : IEndpointStatusQueryService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly IAssignmentMetrics24hService _assignmentMetrics24hService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUserAccessScopeService _userAccessScopeService;
    private readonly IEventLogQueryService _eventLogQueryService;
    private readonly IDbActivityScope _dbActivityScope;
    private readonly IngestRateTracker _ingestRateTracker;

    public EndpointStatusQueryService(PingMonitorDbContext dbContext, IAssignmentMetrics24hService assignmentMetrics24hService, IHttpContextAccessor httpContextAccessor, IUserAccessScopeService userAccessScopeService, IEventLogQueryService eventLogQueryService, IDbActivityScope dbActivityScope, IngestRateTracker ingestRateTracker)
    {
        _dbContext = dbContext;
        _assignmentMetrics24hService = assignmentMetrics24hService;
        _httpContextAccessor = httpContextAccessor;
        _userAccessScopeService = userAccessScopeService;
        _eventLogQueryService = eventLogQueryService;
        _dbActivityScope = dbActivityScope;
        _ingestRateTracker = ingestRateTracker;
    }

    public async Task<EndpointStatusPageViewModel> GetStatusPageAsync(
        string? state,
        string? agent,
        string? groupId,
        string? search,
        CancellationToken cancellationToken)
    {
        using var scope = _dbActivityScope.BeginScope("StatusPage");
        var nowUtc = DateTimeOffset.UtcNow;
        var normalizedState = Normalize(state);

        var principal = _httpContextAccessor.HttpContext?.User;
        var isAdmin = principal is not null && await _userAccessScopeService.IsAdminAsync(principal);
        var visibleEndpointIds = isAdmin || principal is null
            ? null
            : (await _userAccessScopeService.GetVisibleEndpointIdsAsync(principal, cancellationToken)).ToArray();
        var normalizedAgent = Normalize(agent);
        var normalizedGroupId = Normalize(groupId);
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
                IconKey = endpoint.IconKey,
                Target = endpoint.Target,
                AgentId = assignmentAgent.AgentId,
                AgentInstanceId = assignmentAgent.InstanceId,
                AgentName = assignmentAgent.Name ?? assignmentAgent.InstanceId,
                CurrentState = endpointState != null ? endpointState.CurrentState : EndpointStateKind.Unknown,
                StatusCssClass = GetStatusCssClass(endpointState != null ? endpointState.CurrentState : EndpointStateKind.Unknown),
                LastCheckUtc = endpointState != null ? endpointState.LastCheckUtc : null,
                LastStateChangeUtc = endpointState != null ? endpointState.LastStateChangeUtc : null,
                CurrentStateDuration = endpointState != null ? GetCurrentStateDuration(endpointState.CurrentState, endpointState.LastStateChangeUtc, nowUtc) : null,
                CurrentStateDurationDisplay = endpointState != null ? FormatCurrentStateDuration(endpointState.CurrentState, endpointState.LastStateChangeUtc, nowUtc) : "—",
                ConsecutiveFailureCount = endpointState != null ? endpointState.ConsecutiveFailureCount : 0,
                ConsecutiveSuccessCount = endpointState != null ? endpointState.ConsecutiveSuccessCount : 0,
                CheckType = assignment.CheckType.ToString(),
                AssignmentEnabled = assignment.Enabled,
                EndpointEnabled = endpoint.Enabled,
                SuppressedByEndpointId = endpointState != null ? endpointState.SuppressedByEndpointId : null,
                SuppressedByEndpointName = suppressedByEndpoint != null ? suppressedByEndpoint.Name : null
            };

        if (visibleEndpointIds is not null)
        {
            baseQuery = baseQuery.Where(row => visibleEndpointIds.Contains(row.EndpointId));
        }

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

        var availableGroups = await _dbContext.Groups.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new StatusGroupOptionViewModel
            {
                GroupId = x.GroupId,
                Name = x.Name
            })
            .ToArrayAsync(cancellationToken);
        var groupNameLookup = availableGroups.ToDictionary(x => x.GroupId, x => x.Name, StringComparer.Ordinal);

        var rows = await baseQuery
            .OrderBy(row => row.CurrentState)
            .ThenBy(row => row.EndpointName)
            .ThenBy(row => row.AgentInstanceId)
            .ToArrayAsync(cancellationToken);

        var memberships = await _dbContext.EndpointGroupMemberships.AsNoTracking().ToArrayAsync(cancellationToken);
        var groupLookup = memberships
            .GroupBy(x => x.EndpointId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(x => x.GroupId).ToArray(), StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(normalizedGroupId))
        {
            rows = rows.Where(x => groupLookup.GetValueOrDefault(x.EndpointId, Array.Empty<string>()).Contains(normalizedGroupId, StringComparer.Ordinal)).ToArray();
        }

        var endpointIds = rows.Select(x => x.EndpointId).Distinct(StringComparer.Ordinal).ToArray();
        var endpointNames = await _dbContext.Endpoints.AsNoTracking()
            .ToDictionaryAsync(x => x.EndpointId, x => x.Name, cancellationToken);
        var endpointIdSet = endpointIds.ToHashSet(StringComparer.Ordinal);
        var dependencyLookup = endpointIdSet.Count == 0
            ? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            : (await _dbContext.EndpointDependencies.AsNoTracking().ToArrayAsync(cancellationToken))
                .Where(x => endpointIdSet.Contains(x.EndpointId))
                .GroupBy(x => x.EndpointId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<string>)group.Select(x => x.DependsOnEndpointId).OrderBy(x => x).ToArray(),
                    StringComparer.Ordinal);

        var projectedRows = rows.Select(row =>
        {
            var parentIds = dependencyLookup.GetValueOrDefault(row.EndpointId, Array.Empty<string>());
            return new EndpointStatusRowViewModel
            {
                AssignmentId = row.AssignmentId,
                EndpointId = row.EndpointId,
                EndpointName = row.EndpointName,
                IconKey = EndpointIconCatalog.Normalize(row.IconKey),
                Target = row.Target,
                AgentId = row.AgentId,
                AgentInstanceId = row.AgentInstanceId,
                AgentName = row.AgentName,
                CurrentState = row.CurrentState,
                StatusCssClass = GetStatusCssClass(row.CurrentState),
                LastCheckUtc = row.LastCheckUtc,
                LastStateChangeUtc = row.LastStateChangeUtc,
                CurrentStateDuration = row.CurrentStateDuration,
                CurrentStateDurationDisplay = row.CurrentStateDurationDisplay,
                ConsecutiveFailureCount = row.ConsecutiveFailureCount,
                ConsecutiveSuccessCount = row.ConsecutiveSuccessCount,
                CheckType = row.CheckType,
                AssignmentEnabled = row.AssignmentEnabled,
                EndpointEnabled = row.EndpointEnabled,
                ParentEndpointIds = parentIds,
                ParentEndpointNames = parentIds.Select(x => endpointNames.GetValueOrDefault(x, x)).OrderBy(x => x).ToArray(),
                SuppressedByEndpointId = row.SuppressedByEndpointId,
                SuppressedByEndpointName = row.SuppressedByEndpointName,
                GroupNames = groupLookup.GetValueOrDefault(row.EndpointId, Array.Empty<string>())
                    .Select(selectedGroupId => groupNameLookup.GetValueOrDefault(selectedGroupId, selectedGroupId))
                    .OrderBy(x => x)
                    .ToArray()
            };
        }).ToArray();

        var projectedAssignmentIds = projectedRows.Select(x => x.AssignmentId).ToArray();
        var endpointMetricsByAssignmentId = await _assignmentMetrics24hService.GetSummariesAsync(
            projectedAssignmentIds,
            cancellationToken);

        var missingSummaryAssignmentIds = projectedAssignmentIds
            .Where(assignmentId => !endpointMetricsByAssignmentId.ContainsKey(assignmentId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (missingSummaryAssignmentIds.Length > 0)
        {
            await _assignmentMetrics24hService.RefreshAssignmentsAsync(missingSummaryAssignmentIds, cancellationToken);
            endpointMetricsByAssignmentId = await _assignmentMetrics24hService.GetSummariesAsync(projectedAssignmentIds, cancellationToken);
        }

        var rowsWithMetrics = projectedRows.Select(row =>
        {
            endpointMetricsByAssignmentId.TryGetValue(row.AssignmentId, out var metrics);
            return new EndpointStatusRowViewModel
            {
                AssignmentId = row.AssignmentId,
                EndpointId = row.EndpointId,
                EndpointName = row.EndpointName,
                IconKey = row.IconKey,
                Target = row.Target,
                AgentId = row.AgentId,
                AgentInstanceId = row.AgentInstanceId,
                AgentName = row.AgentName,
                CurrentState = row.CurrentState,
                StatusCssClass = row.StatusCssClass,
                LastCheckUtc = row.LastCheckUtc,
                LastStateChangeUtc = row.LastStateChangeUtc,
                CurrentStateDuration = row.CurrentStateDuration,
                CurrentStateDurationDisplay = row.CurrentStateDurationDisplay,
                ConsecutiveFailureCount = row.ConsecutiveFailureCount,
                ConsecutiveSuccessCount = row.ConsecutiveSuccessCount,
                CheckType = row.CheckType,
                AssignmentEnabled = row.AssignmentEnabled,
                EndpointEnabled = row.EndpointEnabled,
                ParentEndpointIds = row.ParentEndpointIds,
                ParentEndpointNames = row.ParentEndpointNames,
                SuppressedByEndpointId = row.SuppressedByEndpointId,
                SuppressedByEndpointName = row.SuppressedByEndpointName,
                GroupNames = row.GroupNames,
                UptimePercent = metrics?.UptimePercent,
                LastRttMs = metrics?.LastRttMs,
                AverageRttMs = null
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
                GroupId = normalizedGroupId,
                Search = normalizedSearch,
                AvailableAgents = availableAgents,
                AvailableGroups = availableGroups
            },
            IngestPerMinute = _ingestRateTracker.GetIngestPerMinute(),
            DropPerMinute = _ingestRateTracker.GetDropPerMinute(),
            Rows = rowsWithMetrics,
            RecentEvents = await _eventLogQueryService.GetRecentEventsAsync(50, cancellationToken)
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

    private static string GetStatusCssClass(EndpointStateKind state)
    {
        return state switch
        {
            EndpointStateKind.Up => "status-up",
            EndpointStateKind.Down => "status-down",
            EndpointStateKind.Degraded => "status-degraded",
            EndpointStateKind.Suppressed => "status-suppressed",
            _ => "status-unknown"
        };
    }

    private static TimeSpan? GetCurrentStateDuration(EndpointStateKind state, DateTimeOffset? lastStateChangeUtc, DateTimeOffset nowUtc)
    {
        if (!lastStateChangeUtc.HasValue)
        {
            return null;
        }

        if (state == EndpointStateKind.Unknown)
        {
            return null;
        }

        var duration = nowUtc - lastStateChangeUtc.Value;
        return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
    }

    private static string FormatCurrentStateDuration(EndpointStateKind state, DateTimeOffset? lastStateChangeUtc, DateTimeOffset nowUtc)
    {
        var duration = GetCurrentStateDuration(state, lastStateChangeUtc, nowUtc);
        if (!duration.HasValue)
        {
            return "—";
        }

        var prefix = state switch
        {
            EndpointStateKind.Up => "Up for",
            EndpointStateKind.Down => "Down for",
            EndpointStateKind.Suppressed => "Suppressed for",
            EndpointStateKind.Degraded => "Degraded for",
            _ => null
        };

        if (prefix is null)
        {
            return "—";
        }

        return $"{prefix} {FormatDuration(duration.Value)}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1d)
        {
            return $"{(int)duration.TotalDays}d {duration.Hours:00}h {duration.Minutes:00}m";
        }

        if (duration.TotalHours >= 1d)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes:00}m";
        }

        return $"{duration.Minutes:00}m {duration.Seconds:00}s";
    }
}
