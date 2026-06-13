using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.NetworkDiagrams;

public interface INetworkDiagramService
{
    Task<IReadOnlyList<NetworkDiagramListSummary>> ListAsync(CancellationToken cancellationToken);
    Task<NetworkDiagram?> GetDiagramAsync(string diagramId, CancellationToken cancellationToken);
    Task<NetworkDiagram> CreateAsync(string name, string? description, string? userId, CancellationToken cancellationToken);
    Task<NetworkDiagramDto?> LoadAsync(string diagramId, CancellationToken cancellationToken);
    Task<NetworkDiagramDto> SaveAsync(string diagramId, NetworkDiagramSaveRequest request, string? userId, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string diagramId, CancellationToken cancellationToken);
}

public sealed class NetworkDiagramListSummary
{
    public string DiagramId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int NodeCount { get; init; }
    public int LinkCount { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed class NetworkDiagramValidationException : InvalidOperationException
{
    public NetworkDiagramValidationException(string message) : base(message) { }
}

internal sealed class NetworkDiagramService : INetworkDiagramService
{
    private readonly PingMonitorDbContext _dbContext;

    public NetworkDiagramService(PingMonitorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<NetworkDiagramListSummary>> ListAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.NetworkDiagrams
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new NetworkDiagramListSummary
            {
                DiagramId = x.DiagramId,
                Name = x.Name,
                Description = x.Description,
                UpdatedAtUtc = x.UpdatedAtUtc,
                NodeCount = x.Nodes.Count,
                LinkCount = x.Links.Count
            })
            .ToListAsync(cancellationToken);
    }

    public Task<NetworkDiagram?> GetDiagramAsync(string diagramId, CancellationToken cancellationToken)
    {
        return _dbContext.NetworkDiagrams.AsNoTracking().FirstOrDefaultAsync(x => x.DiagramId == diagramId, cancellationToken);
    }

    public async Task<NetworkDiagram> CreateAsync(string name, string? description, string? userId, CancellationToken cancellationToken)
    {
        var trimmedName = TrimRequired(name, 255, "Diagram name");
        var now = DateTimeOffset.UtcNow;
        var diagram = new NetworkDiagram
        {
            DiagramId = Guid.NewGuid().ToString("N"),
            Name = trimmedName,
            Description = TrimOptional(description, 2048),
            CanvasWidth = NetworkDiagramPaper.SmallCanvasWidth,
            CanvasHeight = NetworkDiagramPaper.SmallCanvasHeight,
            ViewportZoom = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };

        _dbContext.NetworkDiagrams.Add(diagram);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return diagram;
    }

    public async Task<NetworkDiagramDto?> LoadAsync(string diagramId, CancellationToken cancellationToken)
    {
        var diagram = await _dbContext.NetworkDiagrams
            .AsNoTracking()
            .Include(x => x.Areas)
            .Include(x => x.Nodes)
            .Include(x => x.Links)
                .ThenInclude(x => x.Vlans)
            .FirstOrDefaultAsync(x => x.DiagramId == diagramId, cancellationToken);

        return diagram is null ? null : ToDto(diagram);
    }

    public async Task<NetworkDiagramDto> SaveAsync(string diagramId, NetworkDiagramSaveRequest request, string? userId, CancellationToken cancellationToken)
    {
        var diagram = await _dbContext.NetworkDiagrams
            .Include(x => x.Areas)
            .Include(x => x.Nodes)
            .Include(x => x.Links)
                .ThenInclude(x => x.Vlans)
            .FirstOrDefaultAsync(x => x.DiagramId == diagramId, cancellationToken)
            ?? throw new NetworkDiagramValidationException("Diagram does not exist.");

        await ValidateSavePayloadAsync(request, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        diagram.Name = TrimRequired(request.Name, 255, "Diagram name");
        diagram.Description = TrimOptional(request.Description, 2048);
        diagram.CanvasWidth = ClampFinite(request.CanvasWidth, 1000, 20000, "Canvas width");
        diagram.CanvasHeight = ClampFinite(request.CanvasHeight, 1000, 20000, "Canvas height");
        diagram.ViewportPanX = ClampFinite(request.ViewportPanX, -100000, 100000, "Viewport pan X");
        diagram.ViewportPanY = ClampFinite(request.ViewportPanY, -100000, 100000, "Viewport pan Y");
        diagram.ViewportZoom = ClampFinite(request.ViewportZoom, 0.1, 5, "Viewport zoom");
        diagram.UpdatedAtUtc = now;
        diagram.UpdatedByUserId = userId;

        _dbContext.NetworkDiagramLinks.RemoveRange(diagram.Links);
        _dbContext.NetworkDiagramNodes.RemoveRange(diagram.Nodes);
        _dbContext.NetworkDiagramAreas.RemoveRange(diagram.Areas);
        diagram.Areas.Clear();
        diagram.Nodes.Clear();
        diagram.Links.Clear();

        foreach (var areaRequest in request.Areas.OrderBy(x => x.SortOrder))
        {
            diagram.Areas.Add(new NetworkDiagramArea
            {
                AreaId = TrimRequired(areaRequest.AreaId, 64, "Area ID"),
                DiagramId = diagram.DiagramId,
                Label = TrimRequired(areaRequest.Label, 255, "Area label"),
                Notes = TrimOptional(areaRequest.Notes, 2048),
                X = ClampFinite(areaRequest.X, -1000, 21000, "Area X"),
                Y = ClampFinite(areaRequest.Y, -1000, 21000, "Area Y"),
                Width = ClampFinite(areaRequest.Width, 80, 20000, "Area width"),
                Height = ClampFinite(areaRequest.Height, 60, 20000, "Area height"),
                StyleKey = NormalizeAreaStyle(areaRequest.StyleKey),
                SortOrder = areaRequest.SortOrder,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        foreach (var nodeRequest in request.Nodes)
        {
            diagram.Nodes.Add(new NetworkDiagramNode
            {
                NodeId = TrimRequired(nodeRequest.NodeId, 64, "Node ID"),
                DiagramId = diagram.DiagramId,
                NodeType = nodeRequest.NodeType,
                EndpointId = nodeRequest.NodeType == NetworkDiagramNodeType.MonitoredEndpoint ? TrimOptional(nodeRequest.EndpointId, 64) : null,
                DisplayLabel = TrimRequired(nodeRequest.DisplayLabel, 255, "Node label"),
                IconKey = TrimRequired(nodeRequest.IconKey, 64, "Node icon"),
                X = ClampFinite(nodeRequest.X, -1000, 21000, "Node X"),
                Y = ClampFinite(nodeRequest.Y, -1000, 21000, "Node Y"),
                Width = ClampFinite(nodeRequest.Width <= 0 ? 178 : nodeRequest.Width, 40, 2000, "Node width"),
                Height = ClampFinite(nodeRequest.Height <= 0 ? 78 : nodeRequest.Height, 30, 2000, "Node height"),
                Notes = TrimOptional(nodeRequest.Notes, 4096),
                MetadataJson = TrimOptional(nodeRequest.MetadataJson, 65535),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        foreach (var linkRequest in request.Links)
        {
            var link = new NetworkDiagramLink
            {
                LinkId = TrimRequired(linkRequest.LinkId, 64, "Link ID"),
                DiagramId = diagram.DiagramId,
                SourceNodeId = TrimRequired(linkRequest.SourceNodeId, 64, "Link source node ID"),
                TargetNodeId = TrimRequired(linkRequest.TargetNodeId, 64, "Link target node ID"),
                Label = TrimOptional(linkRequest.Label, 255),
                SourcePortLabel = TrimOptional(linkRequest.SourcePortLabel, 128),
                TargetPortLabel = TrimOptional(linkRequest.TargetPortLabel, 128),
                Notes = TrimOptional(linkRequest.Notes, 4096),
                MediaType = ResolveMediaType(linkRequest.MediaType, linkRequest.LinkType),
                FibreSubtype = ResolveMediaSubtype(linkRequest.MediaType, linkRequest.MediaSubtype ?? linkRequest.FibreSubtype),
                LinkType = ResolveLinkType(linkRequest.LinkType),
                LinkSpeedValue = NormalizeLinkSpeedValue(linkRequest.LinkSpeedValue),
                LinkSpeedUnit = ResolveLinkSpeedUnit(linkRequest.LinkSpeedValue, linkRequest.LinkSpeedUnit),
                LacpMemberCount = ResolveLacpMemberCount(linkRequest.LinkType, linkRequest.LacpMemberCount),
                LacpMemberPortsJson = NormalizeLacpMemberPortsJson(linkRequest.LinkType, linkRequest.LacpMemberPortsJson),
                MetadataJson = TrimOptional(linkRequest.MetadataJson, 65535),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            foreach (var vlan in NormalizeVlans(linkRequest.Vlans))
            {
                link.Vlans.Add(new NetworkDiagramLinkVlan
                {
                    LinkVlanId = Guid.NewGuid().ToString("N"),
                    LinkId = link.LinkId,
                    DiagramId = diagram.DiagramId,
                    VlanId = vlan.VlanId,
                    Name = vlan.Name,
                    Mode = vlan.Mode,
                    Notes = vlan.Notes,
                    SortOrder = vlan.SortOrder,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }

            diagram.Links.Add(link);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(diagram);
    }

    public async Task<bool> DeleteAsync(string diagramId, CancellationToken cancellationToken)
    {
        var diagram = await _dbContext.NetworkDiagrams.FirstOrDefaultAsync(x => x.DiagramId == diagramId, cancellationToken);
        if (diagram is null)
        {
            return false;
        }

        _dbContext.NetworkDiagrams.Remove(diagram);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task ValidateSavePayloadAsync(NetworkDiagramSaveRequest request, CancellationToken cancellationToken)
    {
        _ = TrimRequired(request.Name, 255, "Diagram name");
        _ = ClampFinite(request.CanvasWidth, 1000, 20000, "Canvas width");
        _ = ClampFinite(request.CanvasHeight, 1000, 20000, "Canvas height");
        _ = ClampFinite(request.ViewportZoom, 0.1, 5, "Viewport zoom");

        var areaIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var area in request.Areas)
        {
            var areaId = TrimRequired(area.AreaId, 64, "Area ID");
            if (!areaIds.Add(areaId))
            {
                throw new NetworkDiagramValidationException($"Duplicate area ID '{areaId}'.");
            }

            _ = TrimRequired(area.Label, 255, "Area label");
            _ = TrimOptional(area.Notes, 2048);
            _ = ClampFinite(area.X, -1000, 21000, "Area X");
            _ = ClampFinite(area.Y, -1000, 21000, "Area Y");
            _ = ClampFinite(area.Width, 80, 20000, "Area width");
            _ = ClampFinite(area.Height, 60, 20000, "Area height");
            _ = NormalizeAreaStyle(area.StyleKey);
        }

        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in request.Nodes)
        {
            var nodeId = TrimRequired(node.NodeId, 64, "Node ID");
            if (!nodeIds.Add(nodeId))
            {
                throw new NetworkDiagramValidationException($"Duplicate node ID '{nodeId}'.");
            }

            if (!Enum.IsDefined(node.NodeType))
            {
                throw new NetworkDiagramValidationException($"Unsupported node type '{node.NodeType}'.");
            }

            if (node.NodeType == NetworkDiagramNodeType.MonitoredEndpoint)
            {
                var endpointId = TrimRequired(node.EndpointId ?? string.Empty, 64, "Endpoint ID");
                var endpointExists = await _dbContext.Endpoints.AnyAsync(x => x.EndpointId == endpointId, cancellationToken);
                if (!endpointExists)
                {
                    throw new NetworkDiagramValidationException($"Monitored endpoint node references missing endpoint '{endpointId}'.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(node.EndpointId))
            {
                throw new NetworkDiagramValidationException("Only monitored endpoint nodes may reference an endpoint.");
            }
        }

        var linkIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var link in request.Links)
        {
            var linkId = TrimRequired(link.LinkId, 64, "Link ID");
            if (!linkIds.Add(linkId))
            {
                throw new NetworkDiagramValidationException($"Duplicate link ID '{linkId}'.");
            }

            var sourceNodeId = TrimRequired(link.SourceNodeId, 64, "Link source node ID");
            var targetNodeId = TrimRequired(link.TargetNodeId, 64, "Link target node ID");
            if (string.Equals(sourceNodeId, targetNodeId, StringComparison.Ordinal))
            {
                throw new NetworkDiagramValidationException("Diagram links cannot connect a node to itself.");
            }

            if (!nodeIds.Contains(sourceNodeId) || !nodeIds.Contains(targetNodeId))
            {
                throw new NetworkDiagramValidationException("Diagram links must reference nodes in the same diagram payload.");
            }

            ValidateLinkMetadata(link);
            _ = NormalizeVlans(link.Vlans);

            _ = TrimOptional(link.Label, 255);
            _ = TrimOptional(link.SourcePortLabel, 128);
            _ = TrimOptional(link.TargetPortLabel, 128);
            _ = TrimOptional(link.Notes, 4096);
        }
    }

    private static NetworkDiagramDto ToDto(NetworkDiagram diagram)
    {
        return new NetworkDiagramDto
        {
            DiagramId = diagram.DiagramId,
            Name = diagram.Name,
            Description = diagram.Description,
            CanvasWidth = diagram.CanvasWidth,
            CanvasHeight = diagram.CanvasHeight,
            ViewportPanX = diagram.ViewportPanX,
            ViewportPanY = diagram.ViewportPanY,
            ViewportZoom = diagram.ViewportZoom,
            UpdatedAtUtc = diagram.UpdatedAtUtc,
            Areas = diagram.Areas.OrderBy(x => x.SortOrder).ThenBy(x => x.CreatedAtUtc).Select(x => new NetworkDiagramAreaDto
            {
                AreaId = x.AreaId,
                Label = x.Label,
                Notes = x.Notes,
                X = x.X,
                Y = x.Y,
                Width = x.Width,
                Height = x.Height,
                StyleKey = NormalizeAreaStyle(x.StyleKey),
                SortOrder = x.SortOrder
            }).ToArray(),
            Nodes = diagram.Nodes.OrderBy(x => x.CreatedAtUtc).Select(x => new NetworkDiagramNodeDto
            {
                NodeId = x.NodeId,
                NodeType = x.NodeType.ToString(),
                EndpointId = x.EndpointId,
                DisplayLabel = x.DisplayLabel,
                IconKey = x.IconKey,
                X = x.X,
                Y = x.Y,
                Width = x.Width,
                Height = x.Height,
                Notes = x.Notes,
                MetadataJson = x.MetadataJson
            }).ToArray(),
            Links = diagram.Links.OrderBy(x => x.CreatedAtUtc).Select(x => new NetworkDiagramLinkDto
            {
                LinkId = x.LinkId,
                SourceNodeId = x.SourceNodeId,
                TargetNodeId = x.TargetNodeId,
                Label = x.Label,
                SourcePortLabel = x.SourcePortLabel,
                TargetPortLabel = x.TargetPortLabel,
                Notes = x.Notes,
                MediaType = ResolveMediaType(x.MediaType, x.LinkType),
                MediaSubtype = ResolveMediaSubtype(x.MediaType, x.FibreSubtype),
                FibreSubtype = string.Equals(ResolveMediaType(x.MediaType, x.LinkType), NetworkDiagramLinkMediaTypes.Fibre, StringComparison.Ordinal)
                    ? NetworkDiagramMediaSubtypes.Normalize(x.FibreSubtype, NetworkDiagramLinkMediaTypes.Fibre)
                    : null,
                LinkType = ResolveLinkType(x.LinkType),
                LinkSpeedValue = x.LinkSpeedValue is > 0 ? NormalizeLinkSpeedValue(x.LinkSpeedValue) : null,
                LinkSpeedUnit = NetworkDiagramLinkSpeedUnits.Normalize(x.LinkSpeedUnit),
                LacpMemberCount = ResolveLacpMemberCount(x.LinkType, x.LacpMemberCount),
                LacpMemberPortsJson = NormalizeLacpMemberPortsJson(x.LinkType, x.LacpMemberPortsJson),
                MetadataJson = x.MetadataJson,
                Vlans = x.Vlans.OrderBy(v => v.SortOrder).ThenBy(v => v.VlanId).Select(v => new NetworkDiagramLinkVlanDto
                {
                    VlanId = v.VlanId,
                    Name = v.Name,
                    Mode = NetworkDiagramVlanModes.Normalize(v.Mode),
                    Notes = v.Notes,
                    SortOrder = v.SortOrder
                }).ToArray()
            }).ToArray()
        };
    }


    private static string? NormalizeAreaStyle(string? styleKey)
    {
        if (string.IsNullOrWhiteSpace(styleKey))
        {
            return null;
        }

        var normalized = styleKey.Trim().ToLowerInvariant();
        return normalized is "neutral" or "blue" or "green" or "amber" or "red" or "purple"
            ? normalized
            : throw new NetworkDiagramValidationException($"Unsupported area style '{styleKey}'.");
    }

    private static void ValidateLinkMetadata(NetworkDiagramLinkSaveRequest link)
    {
        var mediaType = ResolveMediaType(link.MediaType, link.LinkType);
        if (!NetworkDiagramLinkMediaTypes.IsAllowed(mediaType))
        {
            throw new NetworkDiagramValidationException($"Unsupported media type '{mediaType}'.");
        }

        var linkType = ResolveLinkType(link.LinkType);
        if (!NetworkDiagramLinkTypes.IsAllowed(linkType))
        {
            throw new NetworkDiagramValidationException($"Unsupported link type '{linkType}'.");
        }

        var mediaSubtype = NetworkDiagramMediaSubtypes.Normalize(link.MediaSubtype ?? link.FibreSubtype, mediaType);
        if (!NetworkDiagramMediaSubtypes.IsAllowed(mediaSubtype, mediaType))
        {
            throw new NetworkDiagramValidationException($"Unsupported media subtype '{mediaSubtype}' for media type '{mediaType}'.");
        }

        var speedValue = NormalizeLinkSpeedValue(link.LinkSpeedValue);
        var speedUnit = NetworkDiagramLinkSpeedUnits.Normalize(link.LinkSpeedUnit);
        if (!NetworkDiagramLinkSpeedUnits.IsAllowed(speedUnit))
        {
            throw new NetworkDiagramValidationException($"Unsupported link speed unit '{speedUnit}'.");
        }

        if (speedValue is not null && speedUnit is null)
        {
            throw new NetworkDiagramValidationException("Link speed unit is required when link speed value is set.");
        }

        if (speedUnit is not null && speedValue is null)
        {
            throw new NetworkDiagramValidationException("Link speed value is required when link speed unit is set.");
        }

        var memberCount = ResolveLacpMemberCount(link.LinkType, link.LacpMemberCount);
        if (!string.Equals(linkType, NetworkDiagramLinkTypes.Lacp, StringComparison.Ordinal) && (link.LacpMemberCount is not null || !string.IsNullOrWhiteSpace(link.LacpMemberPortsJson)))
        {
            throw new NetworkDiagramValidationException("LACP member metadata can only be set when link type is LACP.");
        }

        _ = memberCount;
        _ = NormalizeLacpMemberPortsJson(link.LinkType, link.LacpMemberPortsJson);
    }


    private static IReadOnlyList<NetworkDiagramLinkVlanDto> NormalizeVlans(IReadOnlyList<NetworkDiagramLinkVlanSaveRequest>? vlans)
    {
        if (vlans is null || vlans.Count == 0)
        {
            return [];
        }

        var result = new List<NetworkDiagramLinkVlanDto>();
        var seenVlanIds = new HashSet<int>();
        var sortOrder = 0;
        foreach (var vlan in vlans)
        {
            if (vlan.VlanId is null && string.IsNullOrWhiteSpace(vlan.Name) && string.IsNullOrWhiteSpace(vlan.Mode) && string.IsNullOrWhiteSpace(vlan.Notes))
            {
                continue;
            }

            if (vlan.VlanId is null or < 1 or > 4094)
            {
                throw new NetworkDiagramValidationException("VLAN ID must be between 1 and 4094.");
            }

            if (!seenVlanIds.Add(vlan.VlanId.Value))
            {
                throw new NetworkDiagramValidationException($"This link already contains VLAN {vlan.VlanId.Value}.");
            }

            var mode = NetworkDiagramVlanModes.Normalize(vlan.Mode);
            if (!NetworkDiagramVlanModes.IsAllowed(mode))
            {
                throw new NetworkDiagramValidationException("Select a VLAN mode.");
            }

            result.Add(new NetworkDiagramLinkVlanDto
            {
                VlanId = vlan.VlanId.Value,
                Name = TrimOptional(vlan.Name, 128),
                Mode = mode,
                Notes = TrimOptional(vlan.Notes, 512),
                SortOrder = sortOrder++
            });
        }

        return result.OrderBy(x => x.SortOrder).ToArray();
    }

    private static string ResolveMediaType(string? mediaType, string? legacyLinkType)
    {
        var normalized = NetworkDiagramLinkMediaTypes.Normalize(mediaType);
        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            return normalized;
        }

        return NetworkDiagramLinkTypes.Normalize(legacyLinkType) switch
        {
            NetworkDiagramLinkTypes.Lacp => NetworkDiagramLinkMediaTypes.Other,
            NetworkDiagramLinkTypes.Logical => NetworkDiagramLinkMediaTypes.Virtual,
            _ => NetworkDiagramLinkMediaTypes.Allowed.Contains(NetworkDiagramLinkMediaTypes.Normalize(legacyLinkType))
                ? NetworkDiagramLinkMediaTypes.Normalize(legacyLinkType)
                : NetworkDiagramLinkMediaTypes.Copper
        };
    }

    private static string ResolveLinkType(string? linkType)
    {
        var normalized = NetworkDiagramLinkTypes.Normalize(linkType);
        if (NetworkDiagramLinkTypes.Allowed.Contains(normalized))
        {
            return normalized;
        }

        var legacyMedia = NetworkDiagramLinkMediaTypes.Normalize(linkType);
        return NetworkDiagramLinkMediaTypes.Allowed.Contains(legacyMedia) ? NetworkDiagramLinkTypes.Standard : normalized;
    }

    private static string? ResolveMediaSubtype(string? mediaType, string? mediaSubtype)
    {
        var resolvedMediaType = NetworkDiagramLinkMediaTypes.Normalize(mediaType);
        var normalized = NetworkDiagramMediaSubtypes.Normalize(mediaSubtype, resolvedMediaType);
        return NetworkDiagramMediaSubtypes.IsAllowed(normalized, resolvedMediaType) ? normalized : null;
    }

    private static decimal? NormalizeLinkSpeedValue(decimal? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value <= 0 || value > 1000000)
        {
            throw new NetworkDiagramValidationException("Link speed value must be greater than 0 and no more than 1,000,000.");
        }

        return Math.Round(value.Value, 3);
    }

    private static string? ResolveLinkSpeedUnit(decimal? speedValue, string? speedUnit)
    {
        var normalized = NetworkDiagramLinkSpeedUnits.Normalize(speedUnit);
        return speedValue is null ? null : normalized;
    }

    private static int? ResolveLacpMemberCount(string? linkType, int? count)
    {
        if (!string.Equals(ResolveLinkType(linkType), NetworkDiagramLinkTypes.Lacp, StringComparison.Ordinal))
        {
            return null;
        }

        var resolved = count ?? 2;
        if (resolved < 1 || resolved > 16)
        {
            throw new NetworkDiagramValidationException("LACP member count must be between 1 and 16.");
        }

        return resolved;
    }

    private static string? NormalizeLacpMemberPortsJson(string? linkType, string? json)
    {
        if (!string.Equals(ResolveLinkType(linkType), NetworkDiagramLinkTypes.Lacp, StringComparison.Ordinal))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        if (json.Length > 65535)
        {
            throw new NetworkDiagramValidationException("LACP member port metadata is too large.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > 16)
            {
                throw new NetworkDiagramValidationException("LACP member port metadata must be an array with at most 16 members.");
            }

            foreach (var member in document.RootElement.EnumerateArray())
            {
                if (member.ValueKind != JsonValueKind.Object)
                {
                    throw new NetworkDiagramValidationException("Each LACP member port entry must be an object.");
                }

                ValidateOptionalMemberPort(member, "sourcePort");
                ValidateOptionalMemberPort(member, "targetPort");
            }
        }
        catch (JsonException ex)
        {
            throw new NetworkDiagramValidationException($"LACP member port metadata must be valid JSON: {ex.Message}");
        }

        return json;
    }

    private static void ValidateOptionalMemberPort(JsonElement member, string propertyName)
    {
        if (!member.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.String || value.GetString() is { Length: > 128 })
        {
            throw new NetworkDiagramValidationException("LACP member port labels must be strings of 128 characters or fewer.");
        }
    }

    private static string TrimRequired(string value, int maxLength, string fieldName)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new NetworkDiagramValidationException($"{fieldName} is required.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new NetworkDiagramValidationException($"{fieldName} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? TrimOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new NetworkDiagramValidationException($"Value must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static double ClampFinite(double value, double min, double max, string fieldName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new NetworkDiagramValidationException($"{fieldName} must be a finite number.");
        }

        return Math.Min(Math.Max(value, min), max);
    }
}
