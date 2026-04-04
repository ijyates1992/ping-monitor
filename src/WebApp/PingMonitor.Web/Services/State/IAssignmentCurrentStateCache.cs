using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.State;

public interface IAssignmentCurrentStateCache
{
    Task<CachedAssignmentState> GetStateAsync(string assignmentId, string agentId, string endpointId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, CachedAssignmentState>> GetStatesAsync(
        IReadOnlyCollection<AssignmentStateLookupRequest> requests,
        CancellationToken cancellationToken);
    void Upsert(CachedAssignmentState state);
    void Invalidate(string assignmentId);
}

public sealed class AssignmentStateLookupRequest
{
    public required string AssignmentId { get; init; }
    public required string AgentId { get; init; }
    public required string EndpointId { get; init; }
}

public sealed class CachedAssignmentState
{
    public string AssignmentId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string EndpointId { get; set; } = string.Empty;
    public EndpointStateKind CurrentState { get; set; } = EndpointStateKind.Unknown;
    public int ConsecutiveFailureCount { get; set; }
    public int ConsecutiveSuccessCount { get; set; }
    public DateTimeOffset? LastCheckUtc { get; set; }
    public DateTimeOffset? LastStateChangeUtc { get; set; }
    public string? SuppressedByEndpointId { get; set; }
    public bool ExistsInDatabase { get; set; }
}
