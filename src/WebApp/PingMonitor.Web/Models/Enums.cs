namespace PingMonitor.Web.Models;

public enum EndpointStateKind
{
    Unknown,
    Up,
    Degraded,
    Down,
    Suppressed
}

public enum AlertStatus
{
    Open,
    Closed
}

public enum AgentHealthStatus
{
    Online,
    Stale,
    Offline
}

public enum CheckType
{
    Icmp
}
