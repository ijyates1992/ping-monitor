namespace PingMonitor.Web.Models;

public sealed class EndpointState
{
    public string AssignmentId { get; set; } = string.Empty;
    public EndpointStateKind CurrentState { get; set; } = EndpointStateKind.Unknown;
    public int ConsecutiveFailureCount { get; set; }
    public int ConsecutiveSuccessCount { get; set; }
    public DateTimeOffset? LastCheckUtc { get; set; }
    public DateTimeOffset? LastStateChangeUtc { get; set; }
    public string? SuppressedByEndpointId { get; set; }

    public string AgentId { get; set; } = string.Empty;
    public string EndpointId { get; set; } = string.Empty;
}
