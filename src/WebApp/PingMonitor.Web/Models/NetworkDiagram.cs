namespace PingMonitor.Web.Models;

public sealed class NetworkDiagram
{
    public string DiagramId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public double CanvasWidth { get; set; } = 4000;
    public double CanvasHeight { get; set; } = 2500;
    public double ViewportPanX { get; set; }
    public double ViewportPanY { get; set; }
    public double ViewportZoom { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string? CreatedByUserId { get; set; }
    public string? UpdatedByUserId { get; set; }
    public List<NetworkDiagramNode> Nodes { get; set; } = [];
    public List<NetworkDiagramLink> Links { get; set; } = [];
}
