namespace PingMonitor.Web.Models;

public sealed class Endpoint
{
    public string EndpointId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? DependsOnEndpointId { get; set; }
    public List<string> Tags { get; set; } = [];
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
