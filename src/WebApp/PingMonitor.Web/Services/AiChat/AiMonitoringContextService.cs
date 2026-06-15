using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Models.Identity;
using PingMonitor.Web.Services.Identity;

namespace PingMonitor.Web.Services.AiChat;

internal sealed class AiMonitoringContextService : IAiMonitoringContextService
{
    private const int EndpointListLimit = 10;
    private const int RecentChangeLimit = 10;
    private static readonly TimeSpan RecentChangeWindow = TimeSpan.FromHours(1);
    private readonly PingMonitorDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserAccessScopeService _userAccessScopeService;

    public AiMonitoringContextService(PingMonitorDbContext dbContext, UserManager<ApplicationUser> userManager, IUserAccessScopeService userAccessScopeService)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _userAccessScopeService = userAccessScopeService;
    }

    public async Task<AiMonitoringContextResult> TryGetNetworkHealthSummaryAsync(AiMonitoringContextRequest request, CancellationToken cancellationToken)
    {
        if (!LooksLikeHealthQuestion(request.UserMessage))
        {
            return new AiMonitoringContextResult { ShouldInclude = false, Succeeded = true };
        }

        var scope = await ResolveScopeAsync(request, cancellationToken);
        if (!scope.Resolved)
        {
            return new AiMonitoringContextResult { ShouldInclude = true, Succeeded = false, ErrorMessage = "Network health summary is unavailable because no authenticated application user could be resolved." };
        }

        var now = DateTimeOffset.UtcNow;
        var visibleEndpointIds = scope.IsAdmin ? null : scope.VisibleEndpointIds.ToArray();
        SummaryEndpointRow[] rows;
        if (visibleEndpointIds is { Length: 0 })
        {
            rows = [];
        }
        else
        {
            var assignmentQuery = _dbContext.MonitorAssignments.AsNoTracking();
            if (visibleEndpointIds is not null)
            {
                assignmentQuery = assignmentQuery.Where(x => visibleEndpointIds.Contains(x.EndpointId));
            }

            rows = await (from assignment in assignmentQuery
            join endpoint in _dbContext.Endpoints.AsNoTracking() on assignment.EndpointId equals endpoint.EndpointId
            join state in _dbContext.EndpointStates.AsNoTracking() on assignment.AssignmentId equals state.AssignmentId into stateJoin
            from state in stateJoin.DefaultIfEmpty()
            select new SummaryEndpointRow(
                endpoint.EndpointId,
                endpoint.Name,
                endpoint.Target,
                state != null ? state.CurrentState : EndpointStateKind.Unknown,
                state != null ? state.LastStateChangeUtc : null,
                assignment.AgentId))
                .ToArrayAsync(cancellationToken);
        }

        var visibleIdsFromRows = rows.Select(x => x.EndpointId).Distinct(StringComparer.Ordinal).ToArray();
        var visibleIdSet = visibleIdsFromRows.ToHashSet(StringComparer.Ordinal);
        var agentIds = rows.Select(x => x.AgentId).Distinct(StringComparer.Ordinal).ToArray();

        AiNetworkHealthAgent[] staleAgents = [];
        if (agentIds.Length > 0)
        {
            staleAgents = await _dbContext.Agents.AsNoTracking()
                .Where(x => agentIds.Contains(x.AgentId) && (x.Status == AgentHealthStatus.Stale || x.Status == AgentHealthStatus.Offline))
                .OrderBy(x => x.Name ?? x.InstanceId)
                .ThenBy(x => x.InstanceId)
                .Take(EndpointListLimit)
                .Select(x => new AiNetworkHealthAgent
                {
                    AgentId = x.AgentId,
                    Name = string.IsNullOrWhiteSpace(x.Name) ? "(unnamed agent)" : x.Name!,
                    InstanceId = x.InstanceId,
                    Status = x.Status.ToString().ToUpperInvariant(),
                    LastHeartbeatUtc = x.LastHeartbeatUtc
                })
                .ToArrayAsync(cancellationToken);
        }

        var recentStart = now - RecentChangeWindow;
        AiNetworkHealthStateChange[] recentStateChanges = [];
        if (visibleIdsFromRows.Length > 0)
        {
            recentStateChanges = await (from transition in _dbContext.StateTransitions.AsNoTracking()
                join endpoint in _dbContext.Endpoints.AsNoTracking() on transition.EndpointId equals endpoint.EndpointId
                where visibleIdsFromRows.Contains(transition.EndpointId) && transition.TransitionAtUtc >= recentStart
                orderby transition.TransitionAtUtc descending
                select new AiNetworkHealthStateChange
                {
                    EndpointId = transition.EndpointId,
                    Name = endpoint.Name,
                    PreviousState = transition.PreviousState.ToString().ToUpperInvariant(),
                    NewState = transition.NewState.ToString().ToUpperInvariant(),
                    ChangedAtUtc = transition.TransitionAtUtc,
                    ReasonCode = transition.ReasonCode
                })
                .Take(RecentChangeLimit)
                .ToArrayAsync(cancellationToken);
        }

        var summary = new AiNetworkHealthSummary
        {
            GeneratedAtUtc = now,
            VisibleEndpointCount = visibleIdSet.Count,
            StateCounts = new AiNetworkHealthStateCounts
            {
                Up = rows.Count(x => x.State == EndpointStateKind.Up),
                Degraded = rows.Count(x => x.State == EndpointStateKind.Degraded),
                Down = rows.Count(x => x.State == EndpointStateKind.Down),
                Suppressed = rows.Count(x => x.State == EndpointStateKind.Suppressed),
                Unknown = rows.Count(x => x.State == EndpointStateKind.Unknown)
            },
            DownEndpoints = SelectEndpointRows(rows, EndpointStateKind.Down),
            DegradedEndpoints = SelectEndpointRows(rows, EndpointStateKind.Degraded),
            UnknownEndpoints = SelectEndpointRows(rows, EndpointStateKind.Unknown),
            SuppressedEndpoints = SelectEndpointRows(rows, EndpointStateKind.Suppressed),
            StaleAgents = staleAgents,
            RecentStateChanges = recentStateChanges,
            Limitations = [
                "This summary uses current endpoint state and recent transitions only.",
                "Raw CheckResults diagnostics, endpoint diagnostic packs, diagram lookup, AI memory, and write actions are not connected in this slice."
            ]
        };

        return new AiMonitoringContextResult { ShouldInclude = true, Succeeded = true, Summary = summary };
    }

    internal static bool LooksLikeHealthQuestion(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        var text = message.Trim().ToLowerInvariant();
        string[] phrases = ["network looking", "anything down", "any outages", "current status", "what is down", "how are things", "endpoints unknown", "anything unknown", "agents offline", "agent offline", "network health", "currently down"];
        return phrases.Any(text.Contains);
    }

    private async Task<AccessScope> ResolveScopeAsync(AiMonitoringContextRequest request, CancellationToken cancellationToken)
    {
        if (request.Principal is not null)
        {
            var isAdmin = await _userAccessScopeService.IsAdminAsync(request.Principal);
            var visible = isAdmin ? new HashSet<string>(StringComparer.Ordinal) : await _userAccessScopeService.GetVisibleEndpointIdsAsync(request.Principal, cancellationToken);
            return new AccessScope(true, isAdmin, visible);
        }

        if (string.IsNullOrWhiteSpace(request.UserId)) return new AccessScope(false, false, new HashSet<string>(StringComparer.Ordinal));
        var user = await _userManager.FindByIdAsync(request.UserId.Trim());
        if (user is null) return new AccessScope(false, false, new HashSet<string>(StringComparer.Ordinal));
        var isUserAdmin = await _userManager.IsInRoleAsync(user, ApplicationRoles.Admin);
        if (isUserAdmin) return new AccessScope(true, true, new HashSet<string>(StringComparer.Ordinal));

        var direct = await _dbContext.UserEndpointAccesses.AsNoTracking().Where(x => x.UserId == user.Id).Select(x => x.EndpointId).ToArrayAsync(cancellationToken);
        var grouped = await (from membership in _dbContext.EndpointGroupMemberships.AsNoTracking()
            join access in _dbContext.UserGroupAccesses.AsNoTracking() on membership.GroupId equals access.GroupId
            where access.UserId == user.Id
            select membership.EndpointId).ToArrayAsync(cancellationToken);
        return new AccessScope(true, false, direct.Concat(grouped).ToHashSet(StringComparer.Ordinal));
    }

    private static IReadOnlyList<AiNetworkHealthEndpoint> SelectEndpointRows(IEnumerable<SummaryEndpointRow> rows, EndpointStateKind state) => rows
        .Where(x => x.State == state)
        .OrderBy(x => x.Name)
        .ThenBy(x => x.Target)
        .Take(EndpointListLimit)
        .Select(x => new AiNetworkHealthEndpoint { EndpointId = x.EndpointId, Name = x.Name, Target = x.Target, State = x.State.ToString().ToUpperInvariant(), LastChangedUtc = x.LastChangedUtc })
        .ToArray();

    private sealed record SummaryEndpointRow(string EndpointId, string Name, string Target, EndpointStateKind State, DateTimeOffset? LastChangedUtc, string AgentId);
    private sealed record AccessScope(bool Resolved, bool IsAdmin, IReadOnlySet<string> VisibleEndpointIds);
}
