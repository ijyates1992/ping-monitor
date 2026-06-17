using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Models.Identity;
using PingMonitor.Web.Services.Identity;

namespace PingMonitor.Web.Services.AiTools;

internal sealed class NetworkHealthSummaryAiTool : IAiTool
{
    private const int MaxListedItemsPerState = 10;
    private readonly PingMonitorDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public NetworkHealthSummaryAiTool(PingMonitorDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    public AiToolDefinition Definition { get; } = new()
    {
        Name = "get_network_health_summary",
        Description = "Returns a compact read-only summary of the current Ping Monitor endpoint states and relevant agent health visible to the current user.",
        MaxResultCharacters = 12000,
        Parameters = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["window"] = new JsonObject { ["type"] = "string", ["description"] = "Optional future-compatible window. This slice normalizes unsupported values to 24h." } },
            ["required"] = new JsonArray(),
            ["additionalProperties"] = false
        }
    };

    public async Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Error("invalid_arguments", "Arguments must be a JSON object.");
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!string.Equals(prop.Name, "window", StringComparison.Ordinal)) return Error("invalid_arguments", "Unsupported argument.");
            }
        }
        catch (JsonException)
        {
            return Error("invalid_arguments", "Arguments must be valid JSON.");
        }

        var user = await ResolveUserAsync(call, cancellationToken);
        if (user is null) return Error("unauthorized", "No application user was available for tool execution.");
        var isAdmin = await _userManager.IsInRoleAsync(user, ApplicationRoles.Admin);
        var visibleEndpointIds = isAdmin ? null : await GetVisibleEndpointIdsAsync(user.Id, cancellationToken);

        var assignments = _dbContext.MonitorAssignments.AsNoTracking();
        if (visibleEndpointIds is not null)
        {
            assignments = visibleEndpointIds.Count == 0 ? assignments.Where(static _ => false) : assignments.Where(x => visibleEndpointIds.Contains(x.EndpointId));
        }

        var rows = await (from assignment in assignments
            join endpoint in _dbContext.Endpoints.AsNoTracking() on assignment.EndpointId equals endpoint.EndpointId
            join agent in _dbContext.Agents.AsNoTracking() on assignment.AgentId equals agent.AgentId
            join state in _dbContext.EndpointStates.AsNoTracking() on assignment.AssignmentId equals state.AssignmentId into stateJoin
            from state in stateJoin.DefaultIfEmpty()
            select new SummaryRow(endpoint.EndpointId, endpoint.Name, assignment.AssignmentId, agent.AgentId, agent.InstanceId, agent.Name, state != null ? state.CurrentState : EndpointStateKind.Unknown, state != null ? state.LastCheckUtc : null, state != null ? state.LastStateChangeUtc : null))
            .ToArrayAsync(cancellationToken);

        var visibleAgentIds = rows.Select(x => x.AgentId).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var unhealthyAgents = await _dbContext.Agents.AsNoTracking()
            .Where(x => x.Status == AgentHealthStatus.Offline || x.Status == AgentHealthStatus.Stale)
            .OrderBy(x => x.InstanceId)
            .Select(x => new { x.AgentId, x.InstanceId, x.Name, x.Status, x.LastHeartbeatUtc, x.LastSeenUtc })
            .ToArrayAsync(cancellationToken);
        var agents = unhealthyAgents
            .Where(x => visibleAgentIds.Contains(x.AgentId))
            .Take(MaxListedItemsPerState)
            .ToArray();

        var result = new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            dataSource = "current_endpoint_state",
            visibleEndpointCount = rows.Select(x => x.EndpointId).Distinct(StringComparer.Ordinal).Count(),
            visibleAssignmentCount = rows.Length,
            stateCounts = new
            {
                UP = rows.Count(x => x.State == EndpointStateKind.Up),
                DEGRADED = rows.Count(x => x.State == EndpointStateKind.Degraded),
                DOWN = rows.Count(x => x.State == EndpointStateKind.Down),
                SUPPRESSED = rows.Count(x => x.State == EndpointStateKind.Suppressed),
                UNKNOWN = rows.Count(x => x.State == EndpointStateKind.Unknown)
            },
            downEndpoints = SelectRows(rows, EndpointStateKind.Down),
            degradedEndpoints = SelectRows(rows, EndpointStateKind.Degraded),
            unknownEndpoints = SelectRows(rows, EndpointStateKind.Unknown),
            suppressedEndpoints = SelectRows(rows, EndpointStateKind.Suppressed),
            offlineOrStaleAgents = agents.Select(x => new { agentId = x.AgentId, instanceId = x.InstanceId, name = x.Name ?? x.InstanceId, status = x.Status.ToString().ToUpperInvariant(), lastHeartbeatUtc = x.LastHeartbeatUtc, lastSeenUtc = x.LastSeenUtc }).ToArray(),
            limitations = new[] { "raw CheckResults diagnostics are not connected yet", "endpoint diagnostics are not connected yet", "diagram lookup is not connected yet", "write actions are not available" }
        };

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new AiToolExecutionResult { Succeeded = true, ContentJson = json.Length <= Definition.MaxResultCharacters ? json : json[..Definition.MaxResultCharacters] };
    }

    private async Task<ApplicationUser?> ResolveUserAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        if (call.Principal is not null) return await _userManager.GetUserAsync(call.Principal);
        return string.IsNullOrWhiteSpace(call.ApplicationUserId) ? null : await _userManager.Users.SingleOrDefaultAsync(x => x.Id == call.ApplicationUserId, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> GetVisibleEndpointIdsAsync(string userId, CancellationToken cancellationToken)
    {
        var direct = await _dbContext.UserEndpointAccesses.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.EndpointId).ToArrayAsync(cancellationToken);
        var grouped = await (from membership in _dbContext.EndpointGroupMemberships.AsNoTracking()
            join access in _dbContext.UserGroupAccesses.AsNoTracking() on membership.GroupId equals access.GroupId
            where access.UserId == userId
            select membership.EndpointId).ToArrayAsync(cancellationToken);
        return direct.Concat(grouped).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static object[] SelectRows(IEnumerable<SummaryRow> rows, EndpointStateKind state) => rows.Where(x => x.State == state).OrderBy(x => x.EndpointName).ThenBy(x => x.AgentInstanceId).Take(MaxListedItemsPerState).Select(x => new { endpointId = x.EndpointId, name = x.EndpointName, assignmentId = x.AssignmentId, agentInstanceId = x.AgentInstanceId, agentName = x.AgentName ?? x.AgentInstanceId, state = x.State.ToString().ToUpperInvariant(), lastCheckUtc = x.LastCheckUtc, lastStateChangeUtc = x.LastStateChangeUtc }).ToArray();
    private static AiToolExecutionResult Error(string code, string message) => new() { Succeeded = false, ErrorMessage = message, ContentJson = JsonSerializer.Serialize(new { error = code, message }) };
    private sealed record SummaryRow(string EndpointId, string EndpointName, string AssignmentId, string AgentId, string AgentInstanceId, string? AgentName, EndpointStateKind State, DateTimeOffset? LastCheckUtc, DateTimeOffset? LastStateChangeUtc);
}
