using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Models.Identity;
using PingMonitor.Web.Services.NetworkDiagrams;

namespace PingMonitor.Web.Services.AiTools;

internal abstract class NetworkDiagramAiToolBase : IAiTool
{
    protected const int MaxDiagrams = 50;
    protected const int MaxNodeMatches = 10;
    protected const int MaxConnections = 50;
    protected const int MaxFullNodes = 120;
    protected const int MaxFullLinks = 160;
    protected const int MaxFullAreas = 60;
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    protected readonly PingMonitorDbContext DbContext;
    protected readonly UserManager<ApplicationUser> UserManager;
    private readonly IApplicationSettingsService _settingsService;

    protected NetworkDiagramAiToolBase(PingMonitorDbContext dbContext, UserManager<ApplicationUser> userManager, IApplicationSettingsService settingsService)
    {
        DbContext = dbContext;
        UserManager = userManager;
        _settingsService = settingsService;
    }

    public abstract AiToolDefinition Definition { get; }
    public abstract Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken);

    protected async Task<(bool Ok, IReadOnlyList<string>? VisibleEndpointIds, AiToolExecutionResult? Error)> ValidateAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        var settings = await _settingsService.GetCurrentAsync(cancellationToken);
        if (!settings.NetworkDiagramsEnabled) return (false, null, Error("network_diagrams_disabled", "Network Diagram lookup is disabled."));
        var user = await AiToolUserVisibility.ResolveUserAsync(call, UserManager, cancellationToken);
        if (user is null) return (false, null, Error("unauthorized", "No application user was available for tool execution."));
        return (true, await AiToolUserVisibility.GetVisibleEndpointIdsOrNullForAdminAsync(DbContext, UserManager, user, cancellationToken), null);
    }

    protected async Task<NetworkDiagramSnapshot?> LoadVisibleDiagramAsync(string diagramId, IReadOnlyList<string>? visibleEndpointIds, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(diagramId) || diagramId.Length > 128) return null;
        var diagram = await DbContext.NetworkDiagrams.AsNoTracking()
            .Include(x => x.Areas).Include(x => x.Nodes).Include(x => x.Links).ThenInclude(x => x.Vlans)
            .SingleOrDefaultAsync(x => x.DiagramId == diagramId, cancellationToken);
        return diagram is null ? null : BuildSnapshot(diagram, visibleEndpointIds);
    }

    protected async Task<NetworkDiagramSnapshot[]> LoadVisibleDiagramsAsync(IReadOnlyList<string>? visibleEndpointIds, string? diagramId, CancellationToken cancellationToken)
    {
        var query = DbContext.NetworkDiagrams.AsNoTracking().Include(x => x.Areas).Include(x => x.Nodes).Include(x => x.Links).ThenInclude(x => x.Vlans).OrderBy(x => x.Name).AsQueryable();
        if (!string.IsNullOrWhiteSpace(diagramId)) query = query.Where(x => x.DiagramId == diagramId.Trim());
        var diagrams = await query.Take(MaxDiagrams).ToArrayAsync(cancellationToken);
        return diagrams.Select(x => BuildSnapshot(x, visibleEndpointIds)).ToArray();
    }

    private static NetworkDiagramSnapshot BuildSnapshot(NetworkDiagram diagram, IReadOnlyList<string>? visibleEndpointIds)
    {
        var visibleNodes = diagram.Nodes.Where(n => visibleEndpointIds is null || n.NodeType != NetworkDiagramNodeType.MonitoredEndpoint || (!string.IsNullOrWhiteSpace(n.EndpointId) && visibleEndpointIds.Contains(n.EndpointId, StringComparer.Ordinal))).OrderBy(n => n.CreatedAtUtc).ToArray();
        var visibleNodeIds = visibleNodes.Select(n => n.NodeId).ToHashSet(StringComparer.Ordinal);
        var links = diagram.Links.Where(l => visibleNodeIds.Contains(l.SourceNodeId) && visibleNodeIds.Contains(l.TargetNodeId)).OrderBy(l => l.CreatedAtUtc).ToArray();
        return new NetworkDiagramSnapshot(diagram, visibleNodes, links, diagram.Areas.OrderBy(a => a.SortOrder).ThenBy(a => a.CreatedAtUtc).ToArray());
    }

    protected async Task<IReadOnlyDictionary<string, EndpointBrief>> LoadEndpointBriefsAsync(IEnumerable<string?> endpointIds, IReadOnlyList<string>? visibleEndpointIds, CancellationToken cancellationToken)
    {
        var ids = endpointIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Cast<string>().ToArray();
        if (visibleEndpointIds is not null) ids = ids.Where(id => visibleEndpointIds.Contains(id, StringComparer.Ordinal)).ToArray();
        if (ids.Length == 0) return new Dictionary<string, EndpointBrief>(StringComparer.Ordinal);
        var rows = await DbContext.Endpoints.AsNoTracking().Where(x => ids.Contains(x.EndpointId)).Select(x => new EndpointBrief(x.EndpointId, x.Name, x.Target)).ToArrayAsync(cancellationToken);
        return rows.ToDictionary(x => x.EndpointId, StringComparer.Ordinal);
    }

    protected static object NodePayload(NetworkDiagramNode n, IReadOnlyDictionary<string, EndpointBrief> endpoints, IReadOnlyList<NetworkDiagramArea> areas) => new
    {
        nodeId = n.NodeId, nodeType = n.NodeType.ToString(), displayLabel = Truncate(n.DisplayLabel, 160), endpointId = n.EndpointId,
        endpointName = n.EndpointId is not null && endpoints.TryGetValue(n.EndpointId, out var ep) ? ep.Name : null,
        target = n.EndpointId is not null && endpoints.TryGetValue(n.EndpointId, out ep) ? ep.Target : null,
        iconKey = Truncate(n.IconKey, 64), notes = Truncate(n.Notes, 400), areaLabel = AreaLabel(n, areas), metadataJson = SafeMetadata(n.MetadataJson)
    };

    protected static object LinkPayload(NetworkDiagramLink l, IReadOnlyDictionary<string, NetworkDiagramNode> nodes) => new
    {
        linkId = l.LinkId,
        sourceNode = nodes.TryGetValue(l.SourceNodeId, out var s) ? new { nodeId = s.NodeId, label = Truncate(s.DisplayLabel, 160) } : null,
        targetNode = nodes.TryGetValue(l.TargetNodeId, out var t) ? new { nodeId = t.NodeId, label = Truncate(t.DisplayLabel, 160) } : null,
        sourcePortLabel = Truncate(l.SourcePortLabel, 80), targetPortLabel = Truncate(l.TargetPortLabel, 80), linkLabel = Truncate(l.Label, 160), notes = Truncate(l.Notes, 400),
        mediaType = l.MediaType, mediaSubtype = l.FibreSubtype, fibreSubtype = l.FibreSubtype, linkType = l.LinkType, speed = Speed(l.LinkSpeedValue, l.LinkSpeedUnit),
        lacp = l.LacpMemberCount.HasValue || !string.IsNullOrWhiteSpace(l.LacpMemberPortsJson) ? new { memberCount = l.LacpMemberCount, memberPortsJson = SafeMetadata(l.LacpMemberPortsJson) } : null,
        vlans = l.Vlans.OrderBy(v => v.SortOrder).ThenBy(v => v.VlanId).Select(v => new { vlanId = v.VlanId, name = Truncate(v.Name, 120), mode = v.Mode, notes = Truncate(v.Notes, 200) }).ToArray(),
        metadataJson = SafeMetadata(l.MetadataJson), source = "saved_network_diagram", isLiveLinkState = false
    };

    protected static string? Speed(decimal? value, string? unit) => value is > 0 && !string.IsNullOrWhiteSpace(unit) ? $"{value:0.##} {unit}" : null;
    protected static string? Truncate(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : (value.Length <= max ? value : value[..max]);
    protected static string? SafeMetadata(string? json) => Truncate(json, 600);
    protected static string? AreaLabel(NetworkDiagramNode n, IReadOnlyList<NetworkDiagramArea> areas) => areas.LastOrDefault(a => n.X >= a.X && n.Y >= a.Y && n.X <= a.X + a.Width && n.Y <= a.Y + a.Height)?.Label;
    protected static bool Contains(string? value, string query) => !string.IsNullOrWhiteSpace(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);
    protected static AiToolExecutionResult JsonResult(object result, int max) { var json = JsonSerializer.Serialize(result, JsonOptions); return new AiToolExecutionResult { Succeeded = true, ContentJson = json.Length <= max ? json : json[..max] }; }
    protected static AiToolExecutionResult Error(string code, string message) => new() { Succeeded = false, ErrorMessage = message, ContentJson = JsonSerializer.Serialize(new { error = code, message }, JsonOptions) };
    protected sealed record NetworkDiagramSnapshot(NetworkDiagram Diagram, IReadOnlyList<NetworkDiagramNode> Nodes, IReadOnlyList<NetworkDiagramLink> Links, IReadOnlyList<NetworkDiagramArea> Areas);
    protected sealed record EndpointBrief(string EndpointId, string Name, string Target);
}

internal sealed class ListNetworkDiagramsAiTool : NetworkDiagramAiToolBase
{
    public ListNetworkDiagramsAiTool(PingMonitorDbContext dbContext, UserManager<ApplicationUser> userManager, IApplicationSettingsService settingsService) : base(dbContext, userManager, settingsService) { }
    public override AiToolDefinition Definition { get; } = new() { Name = "list_network_diagrams", Description = "List saved Network Diagrams visible to the current user. Use this when the user asks what diagrams exist or when you need to choose a diagram before searching.", MaxResultCharacters = 8000, Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject(), ["required"] = new JsonArray(), ["additionalProperties"] = false } };
    public override async Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        var v = await ValidateAsync(call, cancellationToken); if (!v.Ok) return v.Error!;
        var rows = await DbContext.NetworkDiagrams.AsNoTracking().OrderBy(x => x.Name).Take(MaxDiagrams + 1).Select(x => new { x.DiagramId, x.Name, x.Description, x.UpdatedAtUtc, nodeCount = x.Nodes.Count, linkCount = x.Links.Count, areaCount = x.Areas.Count }).ToArrayAsync(cancellationToken);
        return JsonResult(new { diagrams = rows.Take(MaxDiagrams).Select(x => new { diagramId = x.DiagramId, name = Truncate(x.Name, 160), description = Truncate(x.Description, 300), x.nodeCount, x.linkCount, x.areaCount, updatedAtUtc = x.UpdatedAtUtc }), featureEnabled = true, permissionFiltered = true, truncated = rows.Length > MaxDiagrams, returnedCount = Math.Min(rows.Length, MaxDiagrams), totalCount = rows.Length > MaxDiagrams ? (int?)null : rows.Length }, Definition.MaxResultCharacters);
    }
}

internal sealed class SearchDiagramNodesAiTool : NetworkDiagramAiToolBase
{
    public SearchDiagramNodesAiTool(PingMonitorDbContext dbContext, UserManager<ApplicationUser> userManager, IApplicationSettingsService settingsService) : base(dbContext, userManager, settingsService) { }
    public override AiToolDefinition Definition { get; } = new() { Name = "search_diagram_nodes", Description = "Search saved Network Diagram nodes visible to the current user by device label, endpoint name, endpoint target, notes, or metadata. Use this to resolve diagram/device names before looking up links or details.", MaxResultCharacters = 10000, Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["query"] = new JsonObject { ["type"] = "string", ["maxLength"] = 120 }, ["diagramId"] = new JsonObject { ["type"] = new JsonArray("string", "null") } }, ["required"] = new JsonArray("query"), ["additionalProperties"] = false } };
    public override async Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        if (!TryArgs(call.ArgumentsJson, out var query, out var diagramId, out var err)) return err!;
        var v = await ValidateAsync(call, cancellationToken); if (!v.Ok) return v.Error!;
        var diagrams = await LoadVisibleDiagramsAsync(v.VisibleEndpointIds, diagramId, cancellationToken);
        var endpoints = await LoadEndpointBriefsAsync(diagrams.SelectMany(d => d.Nodes).Select(n => n.EndpointId), v.VisibleEndpointIds, cancellationToken);
        var matches = diagrams.SelectMany(d => d.Nodes.Select(n => new { d, n, ep = n.EndpointId is not null && endpoints.TryGetValue(n.EndpointId, out var e) ? e : null })).Where(x => Contains(x.n.DisplayLabel, query) || Contains(x.n.Notes, query) || Contains(x.n.MetadataJson, query) || Contains(AreaLabel(x.n, x.d.Areas), query) || Contains(x.ep?.Name, query) || Contains(x.ep?.Target, query)).Take(MaxNodeMatches + 1).ToArray();
        return JsonResult(new { matches = matches.Take(MaxNodeMatches).Select(x => new { diagramId = x.d.Diagram.DiagramId, diagramName = x.d.Diagram.Name, nodeId = x.n.NodeId, nodeType = x.n.NodeType.ToString(), displayLabel = Truncate(x.n.DisplayLabel, 160), endpointId = x.ep?.EndpointId, endpointName = x.ep?.Name, target = x.ep?.Target, areaLabel = AreaLabel(x.n, x.d.Areas), notes = Truncate(x.n.Notes, 300), linkedNodeCount = x.d.Links.Count(l => l.SourceNodeId == x.n.NodeId || l.TargetNodeId == x.n.NodeId) }), ambiguous = matches.Length > 1, permissionFiltered = true, truncated = matches.Length > MaxNodeMatches }, Definition.MaxResultCharacters);
    }
    private static bool TryArgs(string json, out string query, out string? diagramId, out AiToolExecutionResult? error) { query = string.Empty; diagramId = null; error = null; try { using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); if (doc.RootElement.ValueKind != JsonValueKind.Object) { error = Error("invalid_arguments", "Arguments must be a JSON object."); return false; } foreach (var p in doc.RootElement.EnumerateObject()) if (p.Name is not ("query" or "diagramId")) { error = Error("invalid_arguments", "Unsupported argument."); return false; } query = doc.RootElement.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String ? (q.GetString() ?? string.Empty).Trim() : string.Empty; diagramId = doc.RootElement.TryGetProperty("diagramId", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()?.Trim() : null; if (query.Length is 0 or > 120) { error = Error("invalid_arguments", "query is required and must be 120 characters or fewer."); return false; } return true; } catch (JsonException) { error = Error("invalid_arguments", "Arguments must be valid JSON."); return false; } }
}

internal sealed class GetNetworkDiagramAiTool : NetworkDiagramAiToolBase
{
    public GetNetworkDiagramAiTool(PingMonitorDbContext dbContext, UserManager<ApplicationUser> userManager, IApplicationSettingsService settingsService) : base(dbContext, userManager, settingsService) { }
    public override AiToolDefinition Definition { get; } = new() { Name = "get_network_diagram", Description = "Get a read-only filtered snapshot of one saved Network Diagram, including visible nodes, links, areas, port labels, media, speed, VLAN, LAG/LACP, and notes. This is saved documentation only, not live switch/interface state.", MaxResultCharacters = 24000, Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["diagramId"] = new JsonObject { ["type"] = "string" } }, ["required"] = new JsonArray("diagramId"), ["additionalProperties"] = false } };
    public override async Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        var diagramId = ReadId(call.ArgumentsJson); if (diagramId.Error is not null) return diagramId.Error;
        var v = await ValidateAsync(call, cancellationToken); if (!v.Ok) return v.Error!;
        var snapshot = await LoadVisibleDiagramAsync(diagramId.Id!, v.VisibleEndpointIds, cancellationToken); if (snapshot is null) return Error("not_found", "No visible Network Diagram matched the requested diagramId.");
        var endpoints = await LoadEndpointBriefsAsync(snapshot.Nodes.Select(n => n.EndpointId), v.VisibleEndpointIds, cancellationToken); var nodeById = snapshot.Nodes.ToDictionary(n => n.NodeId, StringComparer.Ordinal);
        var truncated = snapshot.Nodes.Count > MaxFullNodes || snapshot.Links.Count > MaxFullLinks || snapshot.Areas.Count > MaxFullAreas;
        return JsonResult(new { diagramId = snapshot.Diagram.DiagramId, name = snapshot.Diagram.Name, description = Truncate(snapshot.Diagram.Description, 500), generatedAtUtc = DateTimeOffset.UtcNow, source = "saved_network_diagram", isLiveLinkState = false, permissionFiltered = true, truncated, reason = truncated ? "Result exceeded AI tool size limit." : null, totalNodeCount = snapshot.Nodes.Count, totalLinkCount = snapshot.Links.Count, totalAreaCount = snapshot.Areas.Count, areas = snapshot.Areas.Take(MaxFullAreas).Select(a => new { areaId = a.AreaId, label = Truncate(a.Label, 160), notes = Truncate(a.Notes, 300), styleKey = a.StyleKey, containedNodes = snapshot.Nodes.Where(n => n.X >= a.X && n.Y >= a.Y && n.X <= a.X + a.Width && n.Y <= a.Y + a.Height).Select(n => n.NodeId).Take(40).ToArray() }), nodes = snapshot.Nodes.Take(MaxFullNodes).Select(n => NodePayload(n, endpoints, snapshot.Areas)), links = snapshot.Links.Take(MaxFullLinks).Select(l => LinkPayload(l, nodeById)), limitations = new[] { "This is saved diagram documentation.", "This is not live switch interface state.", "Diagram links do not create or modify monitoring dependencies." } }, Definition.MaxResultCharacters);
    }
    internal static (string? Id, AiToolExecutionResult? Error) ReadId(string json) { try { using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); if (doc.RootElement.ValueKind != JsonValueKind.Object) return (null, Error("invalid_arguments", "Arguments must be a JSON object.")); foreach (var p in doc.RootElement.EnumerateObject()) if (p.Name != "diagramId") return (null, Error("invalid_arguments", "Unsupported argument.")); var id = doc.RootElement.TryGetProperty("diagramId", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()?.Trim() : null; return string.IsNullOrWhiteSpace(id) || id.Length > 128 ? (null, Error("invalid_arguments", "diagramId is required.")) : (id, null); } catch (JsonException) { return (null, Error("invalid_arguments", "Arguments must be valid JSON.")); } }
}

internal sealed class FindDiagramConnectionsAiTool : NetworkDiagramAiToolBase
{
    public FindDiagramConnectionsAiTool(PingMonitorDbContext dbContext, UserManager<ApplicationUser> userManager, IApplicationSettingsService settingsService) : base(dbContext, userManager, settingsService) { }
    public override AiToolDefinition Definition { get; } = new() { Name = "find_diagram_connections", Description = "Find saved Network Diagram links/connections for a visible device, endpoint, or node. Use this for questions about which port a device is connected to, what VLANs are on a link, link media/speed, or what is connected to a switch/device. Results are saved diagram documentation only, not live link state.", MaxResultCharacters = 18000, Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["query"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["maxLength"] = 120 }, ["nodeId"] = new JsonObject { ["type"] = new JsonArray("string", "null") }, ["diagramId"] = new JsonObject { ["type"] = new JsonArray("string", "null") } }, ["required"] = new JsonArray(), ["additionalProperties"] = false } };
    public override async Task<AiToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        if (!TryArgs(call.ArgumentsJson, out var query, out var nodeId, out var diagramId, out var err)) return err!;
        var v = await ValidateAsync(call, cancellationToken); if (!v.Ok) return v.Error!;
        var diagrams = await LoadVisibleDiagramsAsync(v.VisibleEndpointIds, diagramId, cancellationToken);
        var endpoints = await LoadEndpointBriefsAsync(diagrams.SelectMany(d => d.Nodes).Select(n => n.EndpointId), v.VisibleEndpointIds, cancellationToken);
        var targetIds = diagrams.SelectMany(d => d.Nodes.Select(n => new { d, n, ep = n.EndpointId is not null && endpoints.TryGetValue(n.EndpointId, out var e) ? e : null })).Where(x => (!string.IsNullOrWhiteSpace(nodeId) && x.n.NodeId == nodeId) || (!string.IsNullOrWhiteSpace(query) && (Contains(x.n.DisplayLabel, query!) || Contains(x.n.Notes, query!) || Contains(x.n.MetadataJson, query!) || Contains(AreaLabel(x.n, x.d.Areas), query!) || Contains(x.ep?.Name, query!) || Contains(x.ep?.Target, query!)))).Select(x => (x.d, x.n.NodeId)).ToArray();
        var matches = new List<object>();
        foreach (var d in diagrams)
        {
            var ids = targetIds.Where(x => ReferenceEquals(x.d, d)).Select(x => x.NodeId).ToHashSet(StringComparer.Ordinal); if (ids.Count == 0) continue;
            var nodeById = d.Nodes.ToDictionary(n => n.NodeId, StringComparer.Ordinal);
            foreach (var link in d.Links.Where(l => ids.Contains(l.SourceNodeId) || ids.Contains(l.TargetNodeId))) { matches.Add(new { diagramId = d.Diagram.DiagramId, diagramName = d.Diagram.Name, sourceNode = nodeById.TryGetValue(link.SourceNodeId, out var s) ? new { nodeId = s.NodeId, label = Truncate(s.DisplayLabel, 160) } : null, targetNode = nodeById.TryGetValue(link.TargetNodeId, out var t) ? new { nodeId = t.NodeId, label = Truncate(t.DisplayLabel, 160) } : null, sourcePortLabel = Truncate(link.SourcePortLabel, 80), targetPortLabel = Truncate(link.TargetPortLabel, 80), linkLabel = Truncate(link.Label, 160), mediaType = link.MediaType, mediaSubtype = link.FibreSubtype, speed = Speed(link.LinkSpeedValue, link.LinkSpeedUnit), vlans = link.Vlans.OrderBy(vl => vl.SortOrder).ThenBy(vl => vl.VlanId).Select(vl => new { vlanId = vl.VlanId, name = Truncate(vl.Name, 120), mode = vl.Mode, notes = Truncate(vl.Notes, 200) }), lacp = link.LacpMemberCount.HasValue || !string.IsNullOrWhiteSpace(link.LacpMemberPortsJson) ? new { memberCount = link.LacpMemberCount, memberPortsJson = SafeMetadata(link.LacpMemberPortsJson) } : null, notes = Truncate(link.Notes, 400), source = "saved_network_diagram", isLiveLinkState = false }); if (matches.Count > MaxConnections) break; }
        }
        return JsonResult(new { query, matches = matches.Take(MaxConnections), permissionFiltered = true, truncated = matches.Count > MaxConnections, source = "saved_network_diagram", isLiveLinkState = false }, Definition.MaxResultCharacters);
    }
    private static bool TryArgs(string json, out string? query, out string? nodeId, out string? diagramId, out AiToolExecutionResult? error) { query = nodeId = diagramId = null; error = null; try { using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); if (doc.RootElement.ValueKind != JsonValueKind.Object) { error = Error("invalid_arguments", "Arguments must be a JSON object."); return false; } foreach (var p in doc.RootElement.EnumerateObject()) if (p.Name is not ("query" or "nodeId" or "diagramId")) { error = Error("invalid_arguments", "Unsupported argument."); return false; } query = doc.RootElement.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String ? q.GetString()?.Trim() : null; nodeId = doc.RootElement.TryGetProperty("nodeId", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()?.Trim() : null; diagramId = doc.RootElement.TryGetProperty("diagramId", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()?.Trim() : null; if ((string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(nodeId)) || query?.Length > 120 || nodeId?.Length > 128) { error = Error("invalid_arguments", "query or nodeId is required and must be bounded."); return false; } return true; } catch (JsonException) { error = Error("invalid_arguments", "Arguments must be valid JSON."); return false; } }
}
