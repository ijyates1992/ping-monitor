namespace PingMonitor.Web.Models;

public sealed class NetworkDiagramLinkVlan
{
    public string LinkVlanId { get; set; } = Guid.NewGuid().ToString("N");
    public string LinkId { get; set; } = string.Empty;
    public string DiagramId { get; set; } = string.Empty;
    public int VlanId { get; set; }
    public string? Name { get; set; }
    public string Mode { get; set; } = "Tagged";
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public NetworkDiagramLink? Link { get; set; }
}
