using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Models.Identity;

namespace PingMonitor.Web.Services.AiTools;

internal abstract class DependencyAiToolBase : IAiTool
{
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    protected static readonly string[] Limitations =
    [
        "Only direct parent DOWN state suppresses a child endpoint.",
        "SUPPRESSED parent endpoints do not cascade suppression.",
        "UNKNOWN is not DOWN.",
        "This is saved dependency configuration, not inferred topology, Network Diagram data, or SNMP data.",
        "Dependency tools are read-only and do not change monitoring behaviour."
    ];

    protected readonly PingMonitorDbContext DbContext;
    protected readonly UserManager<ApplicationUser> UserManager;

    protected DependencyAiToolBase(PingMonitorDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        DbContext = dbContext;
        UserManager = userManager;
    }

    public abstract AiToolDefinition Definition { get; }
    public abstract Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken);

    protected async Task<(bool Ok, IReadOnlyDictionary<string, EndpointInfo> Endpoints, Edge[] Edges, AiToolExecutionResult? Error)> LoadVisibleGraphAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        var user = await AiToolUserVisibility.ResolveUserAsync(call, UserManager, cancellationToken);
        if (user is null) return (false, new Dictionary<string, EndpointInfo>(), [], Error("unauthorized", "No application user was available for tool execution."));
        var visibleIds = await AiToolUserVisibility.GetVisibleEndpointIdsOrNullForAdminAsync(DbContext, UserManager, user, cancellationToken);
        var endpointQuery = DbContext.Endpoints.AsNoTracking();
        if (visibleIds is not null) endpointQuery = endpointQuery.Where(x => visibleIds.Contains(x.EndpointId));
        var endpoints = await endpointQuery.Select(x => new EndpointInfo(x.EndpointId, x.Name, x.Target, x.Enabled, "UNKNOWN")).ToArrayAsync(cancellationToken);
        var endpointIds = endpoints.Select(x => x.EndpointId).ToArray();
        var states = await (from assignment in DbContext.MonitorAssignments.AsNoTracking()
            join state in DbContext.EndpointStates.AsNoTracking() on assignment.AssignmentId equals state.AssignmentId into sj
            from state in sj.DefaultIfEmpty()
            where endpointIds.Contains(assignment.EndpointId)
            select new { assignment.EndpointId, State = state != null ? state.CurrentState : EndpointStateKind.Unknown }).ToArrayAsync(cancellationToken);
        var stateByEndpoint = states.GroupBy(x => x.EndpointId).ToDictionary(x => x.Key, x => x.First().State.ToString().ToUpperInvariant(), StringComparer.Ordinal);
        var endpointMap = endpoints.Select(x => x with { CurrentState = stateByEndpoint.GetValueOrDefault(x.EndpointId, "UNKNOWN") }).ToDictionary(x => x.EndpointId, StringComparer.Ordinal);
        var edges = await DbContext.EndpointDependencies.AsNoTracking()
            .Where(x => endpointIds.Contains(x.EndpointId) && endpointIds.Contains(x.DependsOnEndpointId))
            .Select(x => new Edge(x.EndpointId, x.DependsOnEndpointId))
            .ToArrayAsync(cancellationToken);
        return (true, endpointMap, edges, null);
    }

    protected static (int Depth, bool Clamped) ReadDepth(JsonElement root, AiToolCall call, int defaultDepth)
    {
        var max = Math.Clamp(call.Limits.MaxDependencyTraversalDepth, 1, 5);
        var requested = root.TryGetProperty("depth", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetInt32(out var value) ? value : defaultDepth;
        return (Math.Clamp(requested, 1, max), requested < 1 || requested > max);
    }

    protected static List<DependencyItem> Traverse(string startId, IReadOnlyDictionary<string, EndpointInfo> endpoints, Edge[] edges, bool upstream, int maxDepth, int maxItems, List<string> warnings, out bool truncated)
    {
        truncated = false;
        var results = new List<DependencyItem>();
        var queue = new Queue<(string Id, int Depth, string[] Path)>();
        queue.Enqueue((startId, 0, [startId]));
        var seenAtDepth = new Dictionary<string, int>(StringComparer.Ordinal) { [startId] = 0 };
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.Depth >= maxDepth) continue;
            var nextIds = upstream ? edges.Where(x => x.ChildId == current.Id).Select(x => x.ParentId) : edges.Where(x => x.ParentId == current.Id).Select(x => x.ChildId);
            foreach (var nextId in nextIds.Order(StringComparer.Ordinal))
            {
                if (!endpoints.TryGetValue(nextId, out var endpoint)) continue;
                var nextDepth = current.Depth + 1;
                if (current.Path.Contains(nextId, StringComparer.Ordinal))
                {
                    warnings.Add($"Cycle encountered while traversing dependency graph at endpointId '{nextId}'. Traversal for that path was stopped.");
                    continue;
                }
                if (results.Count >= maxItems) { truncated = true; return results; }
                var path = current.Path.Append(nextId).ToArray();
                results.Add(new DependencyItem(endpoint.EndpointId, endpoint.Name, endpoint.Target, endpoint.CurrentState, nextDepth, upstream && nextDepth == 1, !upstream && nextDepth == 1, path));
                if (!seenAtDepth.TryGetValue(nextId, out var previousDepth) || nextDepth < previousDepth)
                {
                    seenAtDepth[nextId] = nextDepth;
                    queue.Enqueue((nextId, nextDepth, path));
                }
            }
        }
        return results;
    }

    protected static bool TryReadObject(string json, out JsonElement root, out AiToolExecutionResult? error) { try { using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); if (doc.RootElement.ValueKind != JsonValueKind.Object) { root = default; error = Error("invalid_arguments", "Arguments must be a JSON object."); return false; } root = doc.RootElement.Clone(); error = null; return true; } catch (JsonException) { root = default; error = Error("invalid_arguments", "Arguments must be valid JSON."); return false; } }
    protected static AiToolExecutionResult JsonResult(object result, int max) { var json = JsonSerializer.Serialize(result, JsonOptions); return new AiToolExecutionResult { Succeeded = true, ContentJson = json.Length <= max ? json : JsonSerializer.Serialize(new { truncated = true, reason = "Result exceeded configured AI tool limit.", originalCharacterCount = json.Length, maxCharacters = max }, JsonOptions) }; }
    protected static AiToolExecutionResult Error(string code, string message) => new() { Succeeded = false, ErrorMessage = message, ContentJson = JsonSerializer.Serialize(new { error = code, message }, JsonOptions) };

    protected sealed record EndpointInfo(string EndpointId, string Name, string Target, bool Enabled, string CurrentState);
    protected sealed record Edge(string ChildId, string ParentId);
    protected sealed record DependencyItem(string EndpointId, string Name, string Target, string CurrentState, int RelationshipDepth, bool SuppressionRelevant, bool DirectlySuppressibleIfSourceDown, string[] PathEndpointIds);
}

internal sealed class GetEndpointDependenciesAiTool(PingMonitorDbContext dbContext, UserManager<ApplicationUser> userManager) : DependencyAiToolBase(dbContext, userManager)
{
    public override AiToolDefinition Definition { get; } = new() { Name = "get_endpoint_dependencies", Description = "Get saved upstream and/or downstream dependency relationships for a visible endpoint. Use this for questions about what an endpoint depends on, what depends on an endpoint, dependency trees, and suppression relationships. This is saved dependency configuration, not diagram topology.", MaxResultCharacters = 18000, Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["endpointId"] = new JsonObject { ["type"] = "string" }, ["direction"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("upstream", "downstream", "both") }, ["depth"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 5 }, ["includeStates"] = new JsonObject { ["type"] = "boolean" } }, ["required"] = new JsonArray("endpointId"), ["additionalProperties"] = false } };
    public override async Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        if (!TryReadObject(call.ArgumentsJson, out var root, out var error)) return error!;
        foreach (var prop in root.EnumerateObject()) if (prop.Name is not ("endpointId" or "direction" or "depth" or "includeStates")) return Error("invalid_arguments", "Unsupported argument.");
        var endpointId = root.TryGetProperty("endpointId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()?.Trim() : null;
        var direction = root.TryGetProperty("direction", out var dir) && dir.ValueKind == JsonValueKind.String ? dir.GetString()?.Trim().ToLowerInvariant() : "both";
        if (string.IsNullOrWhiteSpace(endpointId)) return Error("invalid_arguments", "endpointId is required.");
        if (direction is not ("upstream" or "downstream" or "both")) return Error("invalid_arguments", "direction must be upstream, downstream, or both.");
        var (depth, depthClamped) = ReadDepth(root, call, 1);
        var graph = await LoadVisibleGraphAsync(call, cancellationToken); if (!graph.Ok) return graph.Error!;
        if (!graph.Endpoints.TryGetValue(endpointId, out var endpoint)) return Error("not_found", "No visible endpoint matched the requested endpointId.");
        var warnings = new List<string>(); if (depthClamped) warnings.Add("Requested depth was clamped to the configured AI dependency traversal limit.");
        var maxItems = Math.Max(1, call.Limits.MaxDependencyEndpointsReturned);
        var upTruncated = false;
        var downTruncated = false;
        var upstream = direction is "upstream" or "both" ? Traverse(endpointId, graph.Endpoints, graph.Edges, true, depth, maxItems, warnings, out upTruncated) : [];
        var remaining = Math.Max(0, maxItems - upstream.Count);
        var downstream = direction is "downstream" or "both" ? Traverse(endpointId, graph.Endpoints, graph.Edges, false, depth, Math.Max(1, remaining), warnings, out downTruncated) : [];
        var truncated = upTruncated || downTruncated || (direction is "downstream" or "both") && remaining == 0;
        return JsonResult(new { generatedAtUtc = DateTimeOffset.UtcNow, dataSource = "endpoint_dependencies", permissionFiltered = true, endpoint, direction, depth, upstreamDependencies = upstream, downstreamDependents = downstream, paths = upstream.Concat(downstream).Take(call.Limits.MaxDependencyPathsReturned).Select(x => x.PathEndpointIds).ToArray(), truncation = truncated ? new { truncated = true, reason = "Dependency result exceeded configured AI tool limit.", returnedCount = upstream.Count + downstream.Count } : null, warnings, limitations = Limitations }, call.Limits.MaxSingleToolResultCharacters);
    }
}

internal sealed class GetDependencyImpactAiTool(PingMonitorDbContext dbContext, UserManager<ApplicationUser> userManager) : DependencyAiToolBase(dbContext, userManager)
{
    public override AiToolDefinition Definition { get; } = new() { Name = "get_dependency_impact", Description = "Get the visible endpoints that may be affected if a selected endpoint goes DOWN, based on saved dependency configuration. Use this for impact questions such as \"what would be affected if this switch/router goes down?\". Only direct parent DOWN state suppresses child endpoints.", MaxResultCharacters = 18000, Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["endpointId"] = new JsonObject { ["type"] = "string" }, ["depth"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 5 }, ["includeCurrentStates"] = new JsonObject { ["type"] = "boolean" } }, ["required"] = new JsonArray("endpointId"), ["additionalProperties"] = false } };
    public override async Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        if (!TryReadObject(call.ArgumentsJson, out var root, out var error)) return error!;
        foreach (var prop in root.EnumerateObject()) if (prop.Name is not ("endpointId" or "depth" or "includeCurrentStates")) return Error("invalid_arguments", "Unsupported argument.");
        var endpointId = root.TryGetProperty("endpointId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(endpointId)) return Error("invalid_arguments", "endpointId is required.");
        var (depth, depthClamped) = ReadDepth(root, call, 3);
        var graph = await LoadVisibleGraphAsync(call, cancellationToken); if (!graph.Ok) return graph.Error!;
        if (!graph.Endpoints.TryGetValue(endpointId, out var source)) return Error("not_found", "No visible endpoint matched the requested endpointId.");
        var warnings = new List<string>(); if (depthClamped) warnings.Add("Requested depth was clamped to the configured AI dependency traversal limit.");
        var affected = Traverse(endpointId, graph.Endpoints, graph.Edges, false, depth, Math.Max(1, call.Limits.MaxDependencyEndpointsReturned), warnings, out var truncated);
        var byState = affected.GroupBy(x => x.CurrentState).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        return JsonResult(new { generatedAtUtc = DateTimeOffset.UtcNow, dataSource = "endpoint_dependency_impact", permissionFiltered = true, sourceEndpoint = source, depth, directDependents = affected.Where(x => x.RelationshipDepth == 1).ToArray(), recursiveDependents = affected.Where(x => x.RelationshipDepth > 1).ToArray(), stateCounts = byState, truncation = truncated ? new { truncated = true, reason = "Dependency result exceeded configured AI tool limit.", returnedCount = affected.Count } : null, warnings, limitations = Limitations }, call.Limits.MaxSingleToolResultCharacters);
    }
}

internal sealed class GetDependencySummaryAiTool(PingMonitorDbContext dbContext, UserManager<ApplicationUser> userManager) : DependencyAiToolBase(dbContext, userManager)
{
    public override AiToolDefinition Definition { get; } = new() { Name = "get_dependency_summary", Description = "Get a bounded summary of saved endpoint dependency configuration visible to the current user, including top depended-on endpoints and dependency counts.", MaxResultCharacters = 14000, Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["scope"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("visible") }, ["includeOrphans"] = new JsonObject { ["type"] = "boolean" } }, ["required"] = new JsonArray(), ["additionalProperties"] = false } };
    public override async Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        if (!TryReadObject(call.ArgumentsJson, out var root, out var error)) return error!;
        foreach (var prop in root.EnumerateObject()) if (prop.Name is not ("scope" or "includeOrphans")) return Error("invalid_arguments", "Unsupported argument.");
        var includeOrphans = root.TryGetProperty("includeOrphans", out var o) && o.ValueKind == JsonValueKind.True;
        var graph = await LoadVisibleGraphAsync(call, cancellationToken); if (!graph.Ok) return graph.Error!;
        var childIds = graph.Edges.Select(x => x.ChildId).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var parentIds = graph.Edges.Select(x => x.ParentId).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var top = graph.Edges.GroupBy(x => x.ParentId).Select(x => new { endpoint = graph.Endpoints[x.Key], dependentCount = x.Count() }).OrderByDescending(x => x.dependentCount).ThenBy(x => x.endpoint.Name).Take(call.Limits.MaxTopDependedOnEndpoints).ToArray();
        var orphans = includeOrphans ? graph.Endpoints.Values.Where(x => !childIds.Contains(x.EndpointId) && !parentIds.Contains(x.EndpointId)).OrderBy(x => x.Name).Take(call.Limits.MaxDependencyEndpointsReturned).ToArray() : [];
        return JsonResult(new { generatedAtUtc = DateTimeOffset.UtcNow, dataSource = "endpoint_dependency_summary", permissionFiltered = true, scope = "visible", visibleEndpointCount = graph.Endpoints.Count, dependencyEdgeCount = graph.Edges.Length, endpointsWithUpstreamDependencies = childIds.Count, endpointsWithDownstreamDependents = parentIds.Count, topDependedOnEndpoints = top, endpointsWithNoDependencies = orphans, limitations = Limitations }, call.Limits.MaxSingleToolResultCharacters);
    }
}

internal sealed class ExplainEndpointSuppressionAiTool(PingMonitorDbContext dbContext, UserManager<ApplicationUser> userManager) : DependencyAiToolBase(dbContext, userManager)
{
    public override AiToolDefinition Definition { get; } = new() { Name = "explain_endpoint_suppression", Description = "Explain whether a visible endpoint's current SUPPRESSED state is caused by direct parent dependencies that are currently DOWN. Use this for \"why is this suppressed?\" questions.", MaxResultCharacters = 12000, Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["endpointId"] = new JsonObject { ["type"] = "string" } }, ["required"] = new JsonArray("endpointId"), ["additionalProperties"] = false } };
    public override async Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        if (!TryReadObject(call.ArgumentsJson, out var root, out var error)) return error!;
        foreach (var prop in root.EnumerateObject()) if (prop.Name != "endpointId") return Error("invalid_arguments", "Unsupported argument.");
        var endpointId = root.TryGetProperty("endpointId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(endpointId)) return Error("invalid_arguments", "endpointId is required.");
        var graph = await LoadVisibleGraphAsync(call, cancellationToken); if (!graph.Ok) return graph.Error!;
        if (!graph.Endpoints.TryGetValue(endpointId, out var endpoint)) return Error("not_found", "No visible endpoint matched the requested endpointId.");
        var parents = graph.Edges.Where(x => x.ChildId == endpointId).Select(x => graph.Endpoints[x.ParentId]).OrderBy(x => x.Name).ToArray();
        var downParents = parents.Where(x => x.CurrentState == "DOWN").ToArray();
        return JsonResult(new { generatedAtUtc = DateTimeOffset.UtcNow, dataSource = "endpoint_suppression_explanation", permissionFiltered = true, endpoint, directParents = parents, directDownParents = downParents, suppressionExplained = endpoint.CurrentState == "SUPPRESSED" && downParents.Length > 0, note = endpoint.CurrentState != "SUPPRESSED" ? "Endpoint is not currently SUPPRESSED." : downParents.Length > 0 ? "Current SUPPRESSED state is explained by at least one direct parent in DOWN state." : "No visible direct parent is currently DOWN; UNKNOWN and SUPPRESSED parents are not direct DOWN suppression causes.", limitations = Limitations }, call.Limits.MaxSingleToolResultCharacters);
    }
}
