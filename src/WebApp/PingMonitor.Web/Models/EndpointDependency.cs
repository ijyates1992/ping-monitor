namespace PingMonitor.Web.Models;

public sealed class EndpointDependency
{
    public string EndpointDependencyId { get; set; } = string.Empty;
    public string EndpointId { get; set; } = string.Empty;
    public string DependsOnEndpointId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
