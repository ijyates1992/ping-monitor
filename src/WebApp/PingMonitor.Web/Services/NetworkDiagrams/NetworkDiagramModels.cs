using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.NetworkDiagrams;

public static class NetworkDiagramLinkTypes
{
    public const string Copper = "Copper";
    public const string Fibre = "Fibre";
    public const string Wireless = "Wireless";
    public const string Lacp = "LACP";
    public const string Vpn = "VPN";
    public const string Logical = "Logical";
    public const string Other = "Other";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        Copper,
        Fibre,
        Wireless,
        Lacp,
        Vpn,
        Logical,
        Other
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "default", StringComparison.OrdinalIgnoreCase))
        {
            return Copper;
        }

        var trimmed = value.Trim();
        return Allowed.FirstOrDefault(allowed => string.Equals(allowed, trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
    }

    public static bool IsAllowed(string? value) => Allowed.Contains(Normalize(value));
}

public sealed class NetworkDiagramSaveRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public double CanvasWidth { get; init; }
    public double CanvasHeight { get; init; }
    public double ViewportPanX { get; init; }
    public double ViewportPanY { get; init; }
    public double ViewportZoom { get; init; }
    public IReadOnlyList<NetworkDiagramNodeSaveRequest> Nodes { get; init; } = [];
    public IReadOnlyList<NetworkDiagramLinkSaveRequest> Links { get; init; } = [];
}

public sealed class NetworkDiagramNodeSaveRequest
{
    public string NodeId { get; init; } = string.Empty;
    public NetworkDiagramNodeType NodeType { get; init; }
    public string? EndpointId { get; init; }
    public string DisplayLabel { get; init; } = string.Empty;
    public string IconKey { get; init; } = "generic";
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public string? Notes { get; init; }
    public string? MetadataJson { get; init; }
}

public sealed class NetworkDiagramLinkSaveRequest
{
    public string LinkId { get; init; } = string.Empty;
    public string SourceNodeId { get; init; } = string.Empty;
    public string TargetNodeId { get; init; } = string.Empty;
    public string? Label { get; init; }
    public string? SourcePortLabel { get; init; }
    public string? TargetPortLabel { get; init; }
    public string? Notes { get; init; }
    public string? LinkType { get; init; }
    public string? MetadataJson { get; init; }
}

public sealed class NetworkDiagramDto
{
    public string DiagramId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public double CanvasWidth { get; init; }
    public double CanvasHeight { get; init; }
    public double ViewportPanX { get; init; }
    public double ViewportPanY { get; init; }
    public double ViewportZoom { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public IReadOnlyList<NetworkDiagramNodeDto> Nodes { get; init; } = [];
    public IReadOnlyList<NetworkDiagramLinkDto> Links { get; init; } = [];
}

public sealed class NetworkDiagramNodeDto
{
    public string NodeId { get; init; } = string.Empty;
    public string NodeType { get; init; } = string.Empty;
    public string? EndpointId { get; init; }
    public string DisplayLabel { get; init; } = string.Empty;
    public string IconKey { get; init; } = "generic";
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public string? Notes { get; init; }
    public string? MetadataJson { get; init; }
}

public sealed class NetworkDiagramLinkDto
{
    public string LinkId { get; init; } = string.Empty;
    public string SourceNodeId { get; init; } = string.Empty;
    public string TargetNodeId { get; init; } = string.Empty;
    public string? Label { get; init; }
    public string? SourcePortLabel { get; init; }
    public string? TargetPortLabel { get; init; }
    public string? Notes { get; init; }
    public string LinkType { get; init; } = NetworkDiagramLinkTypes.Copper;
    public string? MetadataJson { get; init; }
}
