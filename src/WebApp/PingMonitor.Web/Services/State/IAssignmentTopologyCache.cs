using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.State;

public interface IAssignmentTopologyCache
{
    Task<AssignmentTopologyContext?> GetAssignmentContextAsync(string assignmentId, CancellationToken cancellationToken);
    Task InvalidateAllAsync(CancellationToken cancellationToken);
}

public sealed class AssignmentTopologyContext
{
    public required string AssignmentId { get; init; }
    public required string AgentId { get; init; }
    public required string EndpointId { get; init; }
    public required string EndpointName { get; init; }
    public required bool AssignmentEnabled { get; init; }
    public required int FailureThreshold { get; init; }
    public required int RecoveryThreshold { get; init; }
    public required bool AgentEnabled { get; init; }
    public required bool AgentApiKeyRevoked { get; init; }
    public required AgentHealthStatus AgentStatus { get; init; }
    public required IReadOnlyList<AssignmentParentDependency> ParentDependencies { get; init; }
    public required IReadOnlyList<string> ChildAssignmentIds { get; init; }
}

public sealed class AssignmentParentDependency
{
    public required string ParentEndpointId { get; init; }
    public required string ParentAssignmentId { get; init; }
}
