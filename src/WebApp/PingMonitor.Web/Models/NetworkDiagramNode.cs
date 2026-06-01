namespace PingMonitor.Web.Models;

public sealed class NetworkDiagramNode
{
    public string NodeId { get; set; } = Guid.NewGuid().ToString("N");
    public string DiagramId { get; set; } = string.Empty;
    public NetworkDiagramNodeType NodeType { get; set; }
    public string? EndpointId { get; set; }
    public string DisplayLabel { get; set; } = string.Empty;
    public string IconKey { get; set; } = "generic";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 178;
    public double Height { get; set; } = 78;
    public string? Notes { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public NetworkDiagram? Diagram { get; set; }
}
