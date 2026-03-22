namespace PingMonitor.Web.Models;

public enum EndpointStateKind
{
    Unknown,
    Up,
    Degraded,
    Down,
    Suppressed
}
