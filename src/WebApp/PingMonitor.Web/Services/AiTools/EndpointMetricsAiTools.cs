using System.Linq.Expressions;
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

internal sealed class SearchEndpointsAiTool : IAiTool
{
    private const int MaxMatches = 5;
    private readonly PingMonitorDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public SearchEndpointsAiTool(PingMonitorDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    public AiToolDefinition Definition { get; } = new()
    {
        Name = "search_endpoints",
        Description = "Search Ping Monitor endpoints visible to the current user by endpoint name, target IP, or hostname. Use this before requesting endpoint-specific metrics when the user names an endpoint.",
        MaxResultCharacters = 6000,
        Parameters = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["query"] = new JsonObject { ["type"] = "string", ["description"] = "Endpoint name, target IP address, or hostname to search for." } },
            ["required"] = new JsonArray("query"),
            ["additionalProperties"] = false
        }
    };

    public async Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        if (!TryReadObject(call.ArgumentsJson, out var root, out var error)) return error!;
        var query = root.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String ? (q.GetString() ?? string.Empty).Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(query)) return Error("invalid_arguments", "query is required.");
        foreach (var prop in root.EnumerateObject()) if (prop.Name != "query") return Error("invalid_arguments", "Unsupported argument.");

        var user = await AiToolUserVisibility.ResolveUserAsync(call, _userManager, cancellationToken);
        if (user is null) return Error("unauthorized", "No application user was available for tool execution.");
        var visibleEndpointIds = await AiToolUserVisibility.GetVisibleEndpointIdsOrNullForAdminAsync(_dbContext, _userManager, user, cancellationToken);

        var endpoints = _dbContext.Endpoints.AsNoTracking();
        if (visibleEndpointIds is not null) endpoints = ApplyEndpointFilter(endpoints, visibleEndpointIds);
        var lower = query.ToLowerInvariant();
        var candidates = await endpoints
            .Where(x => x.Name.ToLower().Contains(lower) || x.Target.ToLower().Contains(lower))
            .Select(x => new { x.EndpointId, x.Name, x.Target, x.Enabled })
            .Take(call.Limits.MaxEndpointSearchResults * 5)
            .ToArrayAsync(cancellationToken);

        var ranked = candidates.Select(x => new { Endpoint = x, Score = Score(x.Name, x.Target, query) })
            .OrderByDescending(x => x.Score).ThenBy(x => x.Endpoint.Name).Take(call.Limits.MaxEndpointSearchResults).ToArray();
        var matches = ranked.Select(x => x.Endpoint).ToArray();
        var endpointIds = matches.Select(x => x.EndpointId).ToArray();
        var assignmentQuery = ApplyEndpointFilter(_dbContext.MonitorAssignments.AsNoTracking(), endpointIds);
        var states = await (from assignment in assignmentQuery
            join state in _dbContext.EndpointStates.AsNoTracking() on assignment.AssignmentId equals state.AssignmentId into sj
            from state in sj.DefaultIfEmpty()
            select new { assignment.EndpointId, State = state != null ? state.CurrentState : EndpointStateKind.Unknown }).ToArrayAsync(cancellationToken);
        var stateByEndpoint = states.GroupBy(x => x.EndpointId).ToDictionary(x => x.Key, x => x.First().State, StringComparer.Ordinal);

        var result = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            dataSource = "endpoint_search",
            permissionFiltered = true,
            matches = matches.Select(x => new { endpointId = x.EndpointId, name = x.Name, target = x.Target, enabled = x.Enabled, currentState = stateByEndpoint.TryGetValue(x.EndpointId, out var endpointState) ? endpointState.ToString().ToUpperInvariant() : "UNKNOWN" }).ToArray(),
            ambiguous = matches.Length > 1 && !(ranked.Length > 0 && ranked[0].Score >= 100 && (ranked.Length == 1 || ranked[1].Score < 100)),
            message = matches.Length == 0 ? "No visible endpoint matched the query." : null
        };
        return JsonResult(result, call.Limits.MaxSingleToolResultCharacters);
    }

    private static IQueryable<Models.Endpoint> ApplyEndpointFilter(IQueryable<Models.Endpoint> endpoints, IReadOnlyCollection<string> endpointIds)
    {
        if (endpointIds.Count == 0)
        {
            return endpoints.Where(static _ => false);
        }

        var parameter = Expression.Parameter(typeof(Models.Endpoint), "endpoint");
        var endpointId = Expression.Property(parameter, nameof(Models.Endpoint.EndpointId));
        Expression? predicate = null;
        foreach (var id in endpointIds)
        {
            var equals = Expression.Equal(endpointId, Expression.Constant(id));
            predicate = predicate is null ? equals : Expression.OrElse(predicate, equals);
        }

        return endpoints.Where(Expression.Lambda<Func<Models.Endpoint, bool>>(predicate!, parameter));
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

    private static int Score(string name, string target, string query) => string.Equals(name, query, StringComparison.OrdinalIgnoreCase) || string.Equals(target, query, StringComparison.OrdinalIgnoreCase) ? 100 : (name.StartsWith(query, StringComparison.OrdinalIgnoreCase) || target.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 80 : 50);
    private static bool TryReadObject(string json, out JsonElement root, out AiToolExecutionResult? error) { try { using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); if (doc.RootElement.ValueKind != JsonValueKind.Object) { root = default; error = Error("invalid_arguments", "Arguments must be a JSON object."); return false; } root = doc.RootElement.Clone(); error = null; return true; } catch (JsonException) { root = default; error = Error("invalid_arguments", "Arguments must be valid JSON."); return false; } }
    private static AiToolExecutionResult JsonResult(object result, int max) { var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)); return new AiToolExecutionResult { Succeeded = true, ContentJson = json.Length <= max ? json : JsonSerializer.Serialize(new { truncated = true, reason = "Result exceeded configured AI tool limit.", originalCharacterCount = json.Length, maxCharacters = max }, new JsonSerializerOptions(JsonSerializerDefaults.Web)) }; }
    private static AiToolExecutionResult Error(string code, string message) => new() { Succeeded = false, ErrorMessage = message, ContentJson = JsonSerializer.Serialize(new { error = code, message }) };
}

internal sealed class EndpointMetricsSummaryAiTool : IAiTool
{
    private const int MaxTailSamples = 120;
    private const int MaxTransitions = 20;
    private const int MaxFailureClusters = 20;
    private readonly PingMonitorDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public EndpointMetricsSummaryAiTool(PingMonitorDbContext dbContext, UserManager<ApplicationUser> userManager) { _dbContext = dbContext; _userManager = userManager; }

    public AiToolDefinition Definition { get; } = new()
    {
        Name = "get_endpoint_metrics_summary",
        Description = "Get a bounded read-only metrics summary for a visible endpoint, including current state, uptime/downtime/unknown/suppressed time, check counts, packet loss, RTT statistics, jitter estimate, recent transitions, and a small recent check sample tail. Use this for endpoint-specific health, uptime, RTT, latency, jitter, packet loss, flapping, or reliability questions.",
        MaxResultCharacters = 16000,
        Parameters = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["endpointId"] = new JsonObject { ["type"] = "string" }, ["window"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("1h", "6h", "24h", "7d") } },
            ["required"] = new JsonArray("endpointId"),
            ["additionalProperties"] = false
        }
    };

    public async Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        if (!TryReadObject(call.ArgumentsJson, out var root, out var error)) return error!;
        var endpointId = root.TryGetProperty("endpointId", out var id) && id.ValueKind == JsonValueKind.String ? (id.GetString() ?? string.Empty).Trim() : string.Empty;
        var requestedWindow = root.TryGetProperty("window", out var w) && w.ValueKind == JsonValueKind.String ? (w.GetString() ?? call.Limits.DefaultEndpointMetricsWindow).Trim() : call.Limits.DefaultEndpointMetricsWindow;
        foreach (var prop in root.EnumerateObject()) if (prop.Name is not ("endpointId" or "window")) return Error("invalid_arguments", "Unsupported argument.");
        if (string.IsNullOrWhiteSpace(endpointId)) return Error("invalid_arguments", "endpointId is required.");

        var user = await AiToolUserVisibility.ResolveUserAsync(call, _userManager, cancellationToken);
        if (user is null) return Error("unauthorized", "No application user was available for tool execution.");
        var visibleEndpointIds = await AiToolUserVisibility.GetVisibleEndpointIdsOrNullForAdminAsync(_dbContext, _userManager, user, cancellationToken);
        if (visibleEndpointIds is not null && !visibleEndpointIds.Contains(endpointId, StringComparer.Ordinal)) return Error("not_found", "No visible endpoint matched the requested endpointId.");

        var endpoint = await _dbContext.Endpoints.AsNoTracking().SingleOrDefaultAsync(x => x.EndpointId == endpointId, cancellationToken);
        if (endpoint is null) return Error("not_found", "No visible endpoint matched the requested endpointId.");
        var assignment = await (from a in _dbContext.MonitorAssignments.AsNoTracking() join ag in _dbContext.Agents.AsNoTracking() on a.AgentId equals ag.AgentId where a.EndpointId == endpointId orderby a.Enabled descending, a.CreatedAtUtc select new { a.AssignmentId, a.AgentId, a.PingIntervalSeconds, AgentName = ag.Name, ag.InstanceId, ag.Status }).FirstOrDefaultAsync(cancellationToken);
        if (assignment is null) return Error("no_assignment", "The visible endpoint has no monitor assignment.");
        var state = await _dbContext.EndpointStates.AsNoTracking().SingleOrDefaultAsync(x => x.AssignmentId == assignment.AssignmentId, cancellationToken);
        var (appliedWindow, duration, clamped) = NormalizeWindow(requestedWindow);
        var toUtc = DateTimeOffset.UtcNow; var fromUtc = toUtc - duration;

        var intervals = await _dbContext.AssignmentStateIntervals.AsNoTracking().Where(x => x.AssignmentId == assignment.AssignmentId && x.StartedAtUtc < toUtc && (x.EndedAtUtc == null || x.EndedAtUtc > fromUtc)).ToArrayAsync(cancellationToken);
        var up = SumIntervals(intervals, EndpointStateKind.Up, fromUtc, toUtc) + SumIntervals(intervals, EndpointStateKind.Degraded, fromUtc, toUtc);
        var down = SumIntervals(intervals, EndpointStateKind.Down, fromUtc, toUtc);
        var unknown = SumIntervals(intervals, EndpointStateKind.Unknown, fromUtc, toUtc);
        var suppressed = SumIntervals(intervals, EndpointStateKind.Suppressed, fromUtc, toUtc);
        var transitions = await _dbContext.StateTransitions.AsNoTracking().Where(x => x.AssignmentId == assignment.AssignmentId && x.TransitionAtUtc >= fromUtc && x.TransitionAtUtc <= toUtc).OrderByDescending(x => x.TransitionAtUtc).Take(call.Limits.MaxEndpointTransitionItems).ToArrayAsync(cancellationToken);
        var checks = await _dbContext.CheckResults.AsNoTracking().Where(x => x.AssignmentId == assignment.AssignmentId && x.CheckedAtUtc >= fromUtc && x.CheckedAtUtc <= toUtc).OrderBy(x => x.CheckedAtUtc).Select(x => new { x.CheckedAtUtc, x.Success, x.RoundTripMs, x.ErrorCode, x.ErrorMessage }).ToArrayAsync(cancellationToken);
        var rtts = checks.Where(x => x.Success && x.RoundTripMs.HasValue).Select(x => (double)x.RoundTripMs!.Value).OrderBy(x => x).ToArray();
        var jitterDeltas = checks.Where(x => x.Success && x.RoundTripMs.HasValue).OrderBy(x => x.CheckedAtUtc).Select(x => (double)x.RoundTripMs!.Value).PairwiseDeltas().ToArray();
        var successful = checks.Count(x => x.Success); var failed = checks.Length - successful; var expected = assignment.PingIntervalSeconds > 0 ? (int)Math.Ceiling(duration.TotalSeconds / assignment.PingIntervalSeconds) : (int?)null;
        var failures = BuildFailureClusters(checks.Where(x => !x.Success).Select(x => x.CheckedAtUtc).ToArray());
        var tail = checks.TakeLast(call.Limits.MaxEndpointMetricsSampleTailPoints).Select(x => new { checkedAtUtc = x.CheckedAtUtc, success = x.Success, rttMs = x.RoundTripMs, errorCode = Truncate(x.ErrorCode, 40), errorMessage = Truncate(x.ErrorMessage, 120) }).ToArray();
        var denominator = up + down;
        var result = new
        {
            generatedAtUtc = toUtc,
            dataSource = "endpoint_metrics_summary",
            permissionFiltered = true,
            endpoint = new { endpointId = endpoint.EndpointId, name = endpoint.Name, target = endpoint.Target, enabled = endpoint.Enabled },
            assignment = new { assignmentId = assignment.AssignmentId, agentId = assignment.AgentId, agentName = assignment.AgentName ?? assignment.InstanceId, agentStatus = assignment.Status.ToString() },
            currentState = new { state = (state?.CurrentState ?? EndpointStateKind.Unknown).ToString().ToUpperInvariant(), lastChangedUtc = state?.LastStateChangeUtc, lastCheckUtc = state?.LastCheckUtc, unknownReason = state is null ? "No current endpoint state row is available." : null },
            window = new { requestedWindow, appliedWindow, clamped, fromUtc, toUtc },
            uptime = new { uptimeSeconds = up, downtimeSeconds = down, unknownSeconds = unknown, suppressedSeconds = suppressed, uptimePercentExcludingUnknownSuppressed = denominator > 0 ? Math.Round(up * 100d / denominator, 2) : (double?)null, downTransitions = transitions.Count(x => x.NewState == EndpointStateKind.Down), recoveryTransitions = transitions.Count(x => x.PreviousState == EndpointStateKind.Down && x.NewState != EndpointStateKind.Down), intervalDataComplete = intervals.Length > 0 },
            checks = new { expectedSamples = expected, receivedSamples = checks.Length, successfulSamples = successful, failedSamples = failed, missingSamplesEstimate = expected.HasValue ? Math.Max(0, expected.Value - checks.Length) : (int?)null, packetLossPercentOfReceived = checks.Length > 0 ? Math.Round(failed * 100d / checks.Length, 2) : (double?)null },
            rtt = rtts.Length > 0 ? new { available = true, sampleCount = rtts.Length, minMs = (double?)Round(rtts.First()), avgMs = (double?)Round(rtts.Average()), medianMs = (double?)Round(Percentile(rtts, 50)), p95Ms = (double?)Round(Percentile(rtts, 95)), maxMs = (double?)Round(rtts.Last()), reason = (string?)null } : new { available = false, sampleCount = 0, minMs = (double?)null, avgMs = (double?)null, medianMs = (double?)null, p95Ms = (double?)null, maxMs = (double?)null, reason = (string?)"No successful RTT samples in the selected window." },
            jitter = jitterDeltas.Length > 0 ? new { available = true, sampleCount = jitterDeltas.Length, avgDeltaMs = (double?)Round(jitterDeltas.Average()), p95DeltaMs = (double?)Round(Percentile(jitterDeltas.OrderBy(x => x).ToArray(), 95)), maxDeltaMs = (double?)Round(jitterDeltas.Max()), method = "absolute difference between consecutive successful RTT samples", reason = (string?)null } : new { available = false, sampleCount = jitterDeltas.Length, avgDeltaMs = (double?)null, p95DeltaMs = (double?)null, maxDeltaMs = (double?)null, method = "absolute difference between consecutive successful RTT samples", reason = (string?)"Not enough successful RTT samples in the selected window." },
            recentTransitions = transitions.Select(x => new { transitionAtUtc = x.TransitionAtUtc, previousState = x.PreviousState.ToString().ToUpperInvariant(), newState = x.NewState.ToString().ToUpperInvariant(), reasonCode = Truncate(x.ReasonCode, 80) }).ToArray(),
            recentFailureClusters = failures.Take(call.Limits.MaxEndpointFailureClusters).Select(x => new { fromUtc = x.Start, toUtc = x.End, failedSamples = x.Count }).ToArray(),
            recentSampleTail = tail,
            limitations = new[] { "This result is bounded.", "UNKNOWN is not DOWN.", "SUPPRESSED is not downtime.", "Recent sample tail may not represent the whole window.", "Full unrestricted raw CheckResults export is not available." }
        };
        return JsonResult(result, call.Limits.MaxSingleToolResultCharacters);
    }

    private static (string Applied, TimeSpan Duration, bool Clamped) NormalizeWindow(string requested) => requested switch { "1h" => ("1h", TimeSpan.FromHours(1), false), "6h" => ("6h", TimeSpan.FromHours(6), false), "24h" or "" => ("24h", TimeSpan.FromHours(24), requested is not "24h" and not ""), "7d" => ("7d", TimeSpan.FromDays(7), false), _ => ("24h", TimeSpan.FromHours(24), true) };
    private static long SumIntervals(IEnumerable<AssignmentStateInterval> intervals, EndpointStateKind kind, DateTimeOffset from, DateTimeOffset to) => intervals.Where(x => x.State == kind).Sum(x => (long)Math.Max(0, ((x.EndedAtUtc ?? to) < to ? (x.EndedAtUtc ?? to) : to).Subtract(x.StartedAtUtc > from ? x.StartedAtUtc : from).TotalSeconds));
    private static double Percentile(double[] sorted, double percentile) { if (sorted.Length == 0) return 0; var rank = (percentile / 100d) * (sorted.Length - 1); var lo = (int)Math.Floor(rank); var hi = (int)Math.Ceiling(rank); return lo == hi ? sorted[lo] : sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo); }
    private static double Round(double value) => Math.Round(value, 2);
    private static string? Truncate(string? value, int max) => string.IsNullOrEmpty(value) ? value : value.Length <= max ? value : value[..max];
    private static List<(DateTimeOffset Start, DateTimeOffset End, int Count)> BuildFailureClusters(DateTimeOffset[] failedAt) { var clusters = new List<(DateTimeOffset, DateTimeOffset, int)>(); foreach (var t in failedAt.OrderBy(x => x)) { if (clusters.Count == 0 || (t - clusters[^1].Item2).TotalMinutes > 5) clusters.Add((t, t, 1)); else { var c = clusters[^1]; clusters[^1] = (c.Item1, t, c.Item3 + 1); } } return clusters.OrderByDescending(x => x.Item2).ToList(); }
    private static bool TryReadObject(string json, out JsonElement root, out AiToolExecutionResult? error) { try { using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); if (doc.RootElement.ValueKind != JsonValueKind.Object) { root = default; error = Error("invalid_arguments", "Arguments must be a JSON object."); return false; } root = doc.RootElement.Clone(); error = null; return true; } catch (JsonException) { root = default; error = Error("invalid_arguments", "Arguments must be valid JSON."); return false; } }
    private static AiToolExecutionResult JsonResult(object result, int max) { var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)); return new AiToolExecutionResult { Succeeded = true, ContentJson = json.Length <= max ? json : JsonSerializer.Serialize(new { truncated = true, reason = "Result exceeded configured AI tool limit.", originalCharacterCount = json.Length, maxCharacters = max }, new JsonSerializerOptions(JsonSerializerDefaults.Web)) }; }
    private static AiToolExecutionResult Error(string code, string message) => new() { Succeeded = false, ErrorMessage = message, ContentJson = JsonSerializer.Serialize(new { error = code, message }) };
}

internal static class AiToolUserVisibility
{
    public static async Task<ApplicationUser?> ResolveUserAsync(AiToolCall call, UserManager<ApplicationUser> userManager, CancellationToken cancellationToken)
    {
        if (call.Principal is not null) return await userManager.GetUserAsync(call.Principal);
        return string.IsNullOrWhiteSpace(call.ApplicationUserId) ? null : await userManager.Users.SingleOrDefaultAsync(x => x.Id == call.ApplicationUserId, cancellationToken);
    }

    public static async Task<IReadOnlyList<string>?> GetVisibleEndpointIdsOrNullForAdminAsync(PingMonitorDbContext dbContext, UserManager<ApplicationUser> userManager, ApplicationUser user, CancellationToken cancellationToken)
    {
        if (await userManager.IsInRoleAsync(user, ApplicationRoles.Admin)) return null;
        var direct = await dbContext.UserEndpointAccesses.AsNoTracking().Where(x => x.UserId == user.Id).Select(x => x.EndpointId).ToArrayAsync(cancellationToken);
        var grouped = await (from membership in dbContext.EndpointGroupMemberships.AsNoTracking() join access in dbContext.UserGroupAccesses.AsNoTracking() on membership.GroupId equals access.GroupId where access.UserId == user.Id select membership.EndpointId).ToArrayAsync(cancellationToken);
        return direct.Concat(grouped).Distinct(StringComparer.Ordinal).ToArray();
    }
}

file static class RttExtensions
{
    public static IEnumerable<double> PairwiseDeltas(this IEnumerable<double> values)
    {
        double? previous = null;
        foreach (var value in values)
        {
            if (previous.HasValue) yield return Math.Abs(value - previous.Value);
            previous = value;
        }
    }
}
