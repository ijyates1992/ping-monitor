namespace PingMonitor.Web.Models;

public sealed class NetworkDiagramArea
{
    public string AreaId { get; set; } = Guid.NewGuid().ToString("N");
    public string DiagramId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 600;
    public double Height { get; set; } = 350;
    public string? StyleKey { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public NetworkDiagram? Diagram { get; set; }
}
