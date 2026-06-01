namespace PingMonitor.Web.Models;

public sealed class NetworkDiagramLink
{
    public string LinkId { get; set; } = Guid.NewGuid().ToString("N");
    public string DiagramId { get; set; } = string.Empty;
    public string SourceNodeId { get; set; } = string.Empty;
    public string TargetNodeId { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? SourcePortLabel { get; set; }
    public string? TargetPortLabel { get; set; }
    public string? Notes { get; set; }
    public string LinkType { get; set; } = "default";
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public NetworkDiagram? Diagram { get; set; }
}
