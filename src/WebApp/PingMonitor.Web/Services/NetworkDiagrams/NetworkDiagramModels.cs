using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.NetworkDiagrams;

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
    public string LinkType { get; init; } = "default";
    public string? MetadataJson { get; init; }
}
