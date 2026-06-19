using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Models.Identity;

namespace PingMonitor.Web.Services.AiTools;

internal sealed class SearchStateTransitionsAiTool : IAiTool
{
    private const int HardMaxResults = 1000;
    private const int HardMaxLookbackDays = 365;
    private const int HardMaxEndpointDetails = 1000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly EndpointStateKind[] AllowedStates =
    [
        EndpointStateKind.Up,
        EndpointStateKind.Down,
        EndpointStateKind.Degraded,
        EndpointStateKind.Suppressed,
        EndpointStateKind.Unknown
    ];

    private readonly PingMonitorDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public SearchStateTransitionsAiTool(PingMonitorDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    public AiToolDefinition Definition { get; } = new()
    {
        Name = "search_state_transitions",
        Description = "Search a bounded UTC timeline of monitoring state changes across visible endpoints. Use for historic incident analysis, what changed, what went DOWN/UP, what happened around a time, and whether other endpoints changed nearby. This returns state transitions, not raw CheckResults or outage-duration totals.",
        MaxResultCharacters = 30000,
        Parameters = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["fromUtc"] = new JsonObject { ["type"] = "string", ["format"] = "date-time", ["description"] = "Inclusive UTC start timestamp ending in Z." },
                ["toUtc"] = new JsonObject { ["type"] = "string", ["format"] = "date-time", ["description"] = "Exclusive UTC end timestamp ending in Z." },
                ["endpointIds"] = StringArray(),
                ["endpointGroupIds"] = StringArray(),
                ["agentIds"] = StringArray(),
                ["fromStates"] = StateArray(),
                ["toStates"] = StateArray(),
                ["includeEndpointDetails"] = new JsonObject { ["type"] = "boolean", ["default"] = true },
                ["includeDependencyHints"] = new JsonObject { ["type"] = "boolean", ["default"] = true },
                ["maxResults"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = HardMaxResults },
                ["sort"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("timestamp_asc", "timestamp_desc"), ["default"] = "timestamp_asc" }
            },
            ["required"] = new JsonArray("fromUtc", "toUtc"),
            ["additionalProperties"] = false
        }
    };

    public async Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        if (!TryParseArguments(call.ArgumentsJson, out var args, out var error)) return error!;

        var configuredLookbackDays = Math.Clamp(call.Limits.MaxStateTransitionLookbackDays, 1, HardMaxLookbackDays);
        if (args.ToUtc - args.FromUtc > TimeSpan.FromDays(configuredLookbackDays))
        {
            return Error("range_too_wide", $"The requested UTC range exceeds the configured maximum state transition lookback window of {configuredLookbackDays} days.");
        }

        var user = await AiToolUserVisibility.ResolveUserAsync(call, _userManager, cancellationToken);
        if (user is null) return Error("unauthorized", "No application user was available for tool execution.");
        var visibleEndpointIds = await AiToolUserVisibility.GetVisibleEndpointIdsOrNullForAdminAsync(_dbContext, _userManager, user, cancellationToken);

        IReadOnlyCollection<string>? scopedEndpointIds = visibleEndpointIds;
        if (args.EndpointGroupIds.Length > 0)
        {
            var groupEndpointIds = await ApplyStringFilter(
                    _dbContext.EndpointGroupMemberships.AsNoTracking(),
                    x => x.GroupId,
                    args.EndpointGroupIds)
                .Select(x => x.EndpointId)
                .Distinct()
                .ToArrayAsync(cancellationToken);
            scopedEndpointIds = Intersect(scopedEndpointIds, groupEndpointIds);
        }

        if (args.EndpointIds.Length > 0) scopedEndpointIds = Intersect(scopedEndpointIds, args.EndpointIds);

        var query = _dbContext.StateTransitions.AsNoTracking()
            .Where(x => x.TransitionAtUtc >= args.FromUtc && x.TransitionAtUtc < args.ToUtc);
        if (scopedEndpointIds is not null) query = ApplyStringFilter(query, x => x.EndpointId, scopedEndpointIds);
        if (args.AgentIds.Length > 0) query = ApplyStringFilter(query, x => x.AgentId, args.AgentIds);
        if (args.FromStates.Length > 0) query = ApplyStateFilter(query, x => x.PreviousState, args.FromStates);
        if (args.ToStates.Length > 0) query = ApplyStateFilter(query, x => x.NewState, args.ToStates);

        var totalCount = await query.CountAsync(cancellationToken);
        var configuredLimit = Math.Clamp(call.Limits.MaxStateTransitionSearchResults, 1, HardMaxResults);
        var requestedLimit = args.MaxResults ?? configuredLimit;
        var resultLimit = Math.Min(requestedLimit, configuredLimit);
        var ordered = args.Sort == "timestamp_desc"
            ? query.OrderByDescending(x => x.TransitionAtUtc).ThenByDescending(x => x.TransitionId)
            : query.OrderBy(x => x.TransitionAtUtc).ThenBy(x => x.TransitionId);
        var transitions = await ordered.Take(resultLimit).ToArrayAsync(cancellationToken);

        var detailLimit = Math.Clamp(call.Limits.MaxStateTransitionEndpointDetails, 1, HardMaxEndpointDetails);
        var detailEndpointIds = transitions.Select(x => x.EndpointId).Distinct(StringComparer.Ordinal).Take(detailLimit).ToArray();
        var endpoints = args.IncludeEndpointDetails
            ? await ApplyStringFilter(_dbContext.Endpoints.AsNoTracking(), x => x.EndpointId, detailEndpointIds)
                .Select(x => new { x.EndpointId, x.Name, x.Target })
                .ToDictionaryAsync(x => x.EndpointId, StringComparer.Ordinal, cancellationToken)
            : [];
        var agentIds = transitions.Select(x => x.AgentId).Distinct(StringComparer.Ordinal).ToArray();
        var agents = args.IncludeEndpointDetails
            ? await ApplyStringFilter(_dbContext.Agents.AsNoTracking(), x => x.AgentId, agentIds)
                .Select(x => new { x.AgentId, x.InstanceId, x.Name })
                .ToDictionaryAsync(x => x.AgentId, StringComparer.Ordinal, cancellationToken)
            : [];

        var memberships = args.IncludeEndpointDetails
            ? await ApplyStringFilter(_dbContext.EndpointGroupMemberships.AsNoTracking(), x => x.EndpointId, detailEndpointIds)
                .Select(x => new { x.EndpointId, x.GroupId })
                .ToArrayAsync(cancellationToken)
            : [];
        var groupIds = memberships.Select(x => x.GroupId).Distinct(StringComparer.Ordinal).ToArray();
        var groups = args.IncludeEndpointDetails
            ? await ApplyStringFilter(_dbContext.Groups.AsNoTracking(), x => x.GroupId, groupIds)
                .Select(x => new { x.GroupId, x.Name })
                .ToDictionaryAsync(x => x.GroupId, StringComparer.Ordinal, cancellationToken)
            : [];
        var groupNamesByEndpoint = memberships
            .GroupBy(x => x.EndpointId, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => x.Where(m => groups.ContainsKey(m.GroupId)).Select(m => groups[m.GroupId].Name).OrderBy(n => n).ToArray(),
                StringComparer.Ordinal);

        var dependencyIds = args.IncludeDependencyHints
            ? transitions.Where(x => x.DependencyEndpointId is not null).Select(x => x.DependencyEndpointId!).Distinct(StringComparer.Ordinal).ToArray()
            : [];
        if (visibleEndpointIds is not null) dependencyIds = dependencyIds.Intersect(visibleEndpointIds, StringComparer.Ordinal).ToArray();
        var dependencyNames = args.IncludeDependencyHints
            ? await ApplyStringFilter(_dbContext.Endpoints.AsNoTracking(), x => x.EndpointId, dependencyIds)
                .Select(x => new { x.EndpointId, x.Name })
                .ToDictionaryAsync(x => x.EndpointId, x => x.Name, StringComparer.Ordinal, cancellationToken)
            : [];

        var rows = transitions.Select(x =>
        {
            endpoints.TryGetValue(x.EndpointId, out var endpoint);
            agents.TryGetValue(x.AgentId, out var agent);
            var dependencyName = x.DependencyEndpointId is not null && dependencyNames.TryGetValue(x.DependencyEndpointId, out var visibleName) ? visibleName : null;
            return new
            {
                timestampUtc = x.TransitionAtUtc,
                endpointId = x.EndpointId,
                endpointName = endpoint?.Name,
                target = endpoint?.Target,
                agentId = x.AgentId,
                agentName = agent is null ? null : agent.Name ?? agent.InstanceId,
                fromState = FormatState(x.PreviousState),
                toState = FormatState(x.NewState),
                durationInPreviousStateSeconds = (long?)null,
                dependencyRelated = x.NewState == EndpointStateKind.Suppressed || x.PreviousState == EndpointStateKind.Suppressed || x.DependencyEndpointId is not null,
                directDownDependencyNames = dependencyName is null ? Array.Empty<string>() : new[] { dependencyName },
                groupNames = groupNamesByEndpoint.TryGetValue(x.EndpointId, out var names) ? names : Array.Empty<string>()
            };
        }).ToArray();

        var truncated = totalCount > transitions.Length;
        var result = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            dataSource = "state_transitions",
            fromUtc = args.FromUtc,
            toUtc = args.ToUtc,
            permissionFiltered = true,
            truncated,
            reason = truncated ? "Result exceeded configured AI state transition limit." : null,
            returnedCount = transitions.Length,
            totalCount,
            transitions = rows,
            summary = new
            {
                endpointsAffected = transitions.Select(x => x.EndpointId).Distinct(StringComparer.Ordinal).Count(),
                downTransitions = transitions.Count(x => x.NewState == EndpointStateKind.Down),
                upTransitions = transitions.Count(x => x.NewState == EndpointStateKind.Up),
                degradedTransitions = transitions.Count(x => x.NewState == EndpointStateKind.Degraded),
                suppressedTransitions = transitions.Count(x => x.NewState == EndpointStateKind.Suppressed),
                unknownTransitions = transitions.Count(x => x.NewState == EndpointStateKind.Unknown)
            },
            byEndpoint = rows.GroupBy(x => new { x.endpointId, x.endpointName }).Select(x => new
            {
                x.Key.endpointId,
                x.Key.endpointName,
                transitionCount = x.Count(),
                downTransitions = x.Count(y => y.toState == "DOWN"),
                upTransitions = x.Count(y => y.toState == "UP"),
                firstTransitionUtc = x.Min(y => y.timestampUtc),
                lastTransitionUtc = x.Max(y => y.timestampUtc)
            }).ToArray(),
            byState = AllowedStates.ToDictionary(x => FormatState(x), x => transitions.Count(t => t.NewState == x)),
            limitations = new[]
            {
                "This is bounded state transition history, not raw ping/check history.",
                "SUPPRESSED is dependency-related and should not be treated as an independent DOWN outage.",
                "UNKNOWN may indicate agent/check visibility issues rather than endpoint failure.",
                "Temporal correlation does not prove cause.",
                "durationInPreviousStateSeconds is unavailable from the bounded transition query."
            }
        };
        return JsonResult(result, Math.Min(Definition.MaxResultCharacters, call.Limits.MaxSingleToolResultCharacters));
    }

    private static JsonObject StringArray() => new() { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["maxItems"] = 1000 };
    private static JsonObject StateArray() => new() { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("UP", "DOWN", "DEGRADED", "SUPPRESSED", "UNKNOWN") }, ["maxItems"] = 5 };
    private static string FormatState(EndpointStateKind state) => state.ToString().ToUpperInvariant();

    private static IReadOnlyCollection<string> Intersect(IReadOnlyCollection<string>? current, IReadOnlyCollection<string> requested) =>
        current is null ? requested.Distinct(StringComparer.Ordinal).ToArray() : current.Intersect(requested, StringComparer.Ordinal).ToArray();

    private static IQueryable<T> ApplyStringFilter<T>(IQueryable<T> query, Expression<Func<T, string>> selector, IReadOnlyCollection<string> values)
    {
        if (values.Count == 0) return query.Where(_ => false);
        var parameter = selector.Parameters[0];
        Expression? predicate = null;
        foreach (var value in values.Distinct(StringComparer.Ordinal))
        {
            var equals = Expression.Equal(selector.Body, Expression.Constant(value));
            predicate = predicate is null ? equals : Expression.OrElse(predicate, equals);
        }
        return query.Where(Expression.Lambda<Func<T, bool>>(predicate!, parameter));
    }

    private static IQueryable<StateTransition> ApplyStateFilter(
        IQueryable<StateTransition> query,
        Expression<Func<StateTransition, EndpointStateKind>> selector,
        IReadOnlyCollection<EndpointStateKind> values)
    {
        var parameter = selector.Parameters[0];
        Expression? predicate = null;
        foreach (var value in values.Distinct())
        {
            var equals = Expression.Equal(selector.Body, Expression.Constant(value));
            predicate = predicate is null ? equals : Expression.OrElse(predicate, equals);
        }
        return query.Where(Expression.Lambda<Func<StateTransition, bool>>(predicate!, parameter));
    }

    private static bool TryParseArguments(string json, out Arguments args, out AiToolExecutionResult? error)
    {
        args = default!;
        error = null;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = Error("invalid_arguments", "Arguments must be a JSON object.");
                return false;
            }

            var supported = new HashSet<string>(["fromUtc", "toUtc", "endpointIds", "endpointGroupIds", "agentIds", "fromStates", "toStates", "includeEndpointDetails", "includeDependencyHints", "maxResults", "sort"], StringComparer.Ordinal);
            if (root.EnumerateObject().Any(x => !supported.Contains(x.Name)))
            {
                error = Error("invalid_arguments", "Unsupported argument.");
                return false;
            }

            if (!TryReadUtc(root, "fromUtc", out var fromUtc) || !TryReadUtc(root, "toUtc", out var toUtc) || fromUtc >= toUtc)
            {
                error = Error("invalid_arguments", "fromUtc and toUtc are required explicit UTC timestamps ending in Z, and fromUtc must be earlier than toUtc.");
                return false;
            }

            if (!TryReadStrings(root, "endpointIds", 1000, out var endpointIds)
                || !TryReadStrings(root, "endpointGroupIds", 1000, out var groupIds)
                || !TryReadStrings(root, "agentIds", 1000, out var agentIds)
                || !TryReadStates(root, "fromStates", out var fromStates)
                || !TryReadStates(root, "toStates", out var toStates))
            {
                error = Error("invalid_arguments", "Filter arrays contain invalid values or exceed their bounds.");
                return false;
            }

            var sort = root.TryGetProperty("sort", out var sortElement) && sortElement.ValueKind == JsonValueKind.String ? sortElement.GetString() : "timestamp_asc";
            if (sort is not ("timestamp_asc" or "timestamp_desc"))
            {
                error = Error("invalid_arguments", "sort must be timestamp_asc or timestamp_desc.");
                return false;
            }

            int? maxResults = null;
            if (root.TryGetProperty("maxResults", out var maxElement))
            {
                if (!maxElement.TryGetInt32(out var parsedMax) || parsedMax is < 1 or > HardMaxResults)
                {
                    error = Error("invalid_arguments", $"maxResults must be between 1 and {HardMaxResults}.");
                    return false;
                }
                maxResults = parsedMax;
            }

            args = new Arguments(
                fromUtc,
                toUtc,
                endpointIds,
                groupIds,
                agentIds,
                fromStates,
                toStates,
                ReadBoolean(root, "includeEndpointDetails", true),
                ReadBoolean(root, "includeDependencyHints", true),
                maxResults,
                sort!);
            return true;
        }
        catch (JsonException)
        {
            error = Error("invalid_arguments", "Arguments must be valid JSON.");
            return false;
        }
    }

    private static bool TryReadUtc(JsonElement root, string name, out DateTimeOffset value)
    {
        value = default;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String) return false;
        var text = element.GetString();
        return text is not null
            && text.EndsWith('Z')
            && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out value)
            && value.Offset == TimeSpan.Zero;
    }

    private static bool TryReadStrings(JsonElement root, string name, int maxItems, out string[] values)
    {
        values = [];
        if (!root.TryGetProperty(name, out var element)) return true;
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > maxItems) return false;
        var parsed = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString())) return false;
            parsed.Add(item.GetString()!.Trim());
        }
        values = parsed.Distinct(StringComparer.Ordinal).ToArray();
        return true;
    }

    private static bool TryReadStates(JsonElement root, string name, out EndpointStateKind[] states)
    {
        states = [];
        if (!TryReadStrings(root, name, 5, out var values)) return false;
        var parsed = new List<EndpointStateKind>();
        foreach (var value in values)
        {
            if (!Enum.TryParse<EndpointStateKind>(value, true, out var state) || !AllowedStates.Contains(state)) return false;
            parsed.Add(state);
        }
        states = parsed.Distinct().ToArray();
        return true;
    }

    private static bool ReadBoolean(JsonElement root, string name, bool defaultValue) =>
        !root.TryGetProperty(name, out var element) ? defaultValue : element.ValueKind == JsonValueKind.True ? true : element.ValueKind == JsonValueKind.False ? false : defaultValue;

    private static AiToolExecutionResult JsonResult(object result, int maxCharacters)
    {
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return json.Length <= maxCharacters
            ? new AiToolExecutionResult { Succeeded = true, ContentJson = json }
            : Error("result_too_large", "The bounded state transition result exceeded the configured single-tool context limit. Narrow the time range or filters.");
    }

    private static AiToolExecutionResult Error(string code, string message) => new()
    {
        Succeeded = false,
        ErrorMessage = message,
        ContentJson = JsonSerializer.Serialize(new { error = code, message }, JsonOptions)
    };

    private sealed record Arguments(
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc,
        string[] EndpointIds,
        string[] EndpointGroupIds,
        string[] AgentIds,
        EndpointStateKind[] FromStates,
        EndpointStateKind[] ToStates,
        bool IncludeEndpointDetails,
        bool IncludeDependencyHints,
        int? MaxResults,
        string Sort);
}
