namespace PingMonitor.Web.Models;

public sealed class Endpoint
{
    public string EndpointId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string IconKey { get; set; } = "generic";
    public bool Enabled { get; set; }
    public List<string> Tags { get; set; } = [];
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
