namespace PingMonitor.Web.Models;

public sealed class StateTransition
{
    public string TransitionId { get; set; } = string.Empty;
    public string AssignmentId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string EndpointId { get; set; } = string.Empty;
    public EndpointStateKind PreviousState { get; set; } = EndpointStateKind.Unknown;
    public EndpointStateKind NewState { get; set; } = EndpointStateKind.Unknown;
    public DateTimeOffset TransitionAtUtc { get; set; }
    public string? ReasonCode { get; set; }
    public string? DependencyEndpointId { get; set; }
}
