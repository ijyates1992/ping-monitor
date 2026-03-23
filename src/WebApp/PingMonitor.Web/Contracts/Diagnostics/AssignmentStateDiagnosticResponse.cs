using PingMonitor.Web.Models;

namespace PingMonitor.Web.Contracts.Diagnostics;

public sealed record AssignmentStateDiagnosticResponse(
    string AssignmentId,
    string AgentId,
    string EndpointId,
    EndpointStateKind CurrentState,
    int ConsecutiveFailureCount,
    int ConsecutiveSuccessCount,
    DateTimeOffset? LastCheckUtc,
    DateTimeOffset? LastStateChangeUtc,
    string? SuppressedByEndpointId,
    IReadOnlyList<StateTransitionDiagnosticItem> Transitions);

public sealed record StateTransitionDiagnosticItem(
    string TransitionId,
    EndpointStateKind PreviousState,
    EndpointStateKind NewState,
    DateTimeOffset TransitionAtUtc,
    string? ReasonCode,
    string? DependencyEndpointId);
