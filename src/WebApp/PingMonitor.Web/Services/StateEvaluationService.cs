using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using EndpointModel = PingMonitor.Web.Models.Endpoint;

namespace PingMonitor.Web.Services;

internal sealed class StateEvaluationService : IStateEvaluationService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly ILogger<StateEvaluationService> _logger;

    public StateEvaluationService(
        PingMonitorDbContext dbContext,
        ILogger<StateEvaluationService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task EvaluateAssignmentsAsync(IEnumerable<string> assignmentIds, CancellationToken cancellationToken)
    {
        var pendingAssignmentIds = new Queue<string>(assignmentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal));
        var processedAssignmentIds = new HashSet<string>(StringComparer.Ordinal);

        while (pendingAssignmentIds.Count > 0)
        {
            var assignmentId = pendingAssignmentIds.Dequeue();
            if (!processedAssignmentIds.Add(assignmentId))
            {
                continue;
            }

            var transitionedAcrossDownBoundary = await EvaluateInternalAsync(assignmentId, cancellationToken);
            if (transitionedAcrossDownBoundary.Count == 0)
            {
                continue;
            }

            foreach (var childAssignmentId in transitionedAcrossDownBoundary)
            {
                if (!processedAssignmentIds.Contains(childAssignmentId))
                {
                    pendingAssignmentIds.Enqueue(childAssignmentId);
                }
            }
        }
    }

    public Task EvaluateAssignmentStateAsync(string assignmentId, CancellationToken cancellationToken)
    {
        return EvaluateAssignmentsAsync([assignmentId], cancellationToken);
    }

    private async Task<IReadOnlyCollection<string>> EvaluateInternalAsync(string assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await _dbContext.MonitorAssignments
            .SingleOrDefaultAsync(x => x.AssignmentId == assignmentId, cancellationToken);

        if (assignment is null)
        {
            _logger.LogWarning("State evaluation skipped because assignment {AssignmentId} was not found.", assignmentId);
            return Array.Empty<string>();
        }

        var endpoint = await _dbContext.Endpoints
            .SingleAsync(x => x.EndpointId == assignment.EndpointId, cancellationToken);
        var agent = await _dbContext.Agents
            .SingleAsync(x => x.AgentId == assignment.AgentId, cancellationToken);

        var state = await _dbContext.EndpointStates
            .SingleOrDefaultAsync(x => x.AssignmentId == assignment.AssignmentId, cancellationToken);

        if (state is null)
        {
            state = new EndpointState
            {
                AssignmentId = assignment.AssignmentId,
                AgentId = assignment.AgentId,
                EndpointId = assignment.EndpointId,
                CurrentState = EndpointStateKind.Unknown
            };

            _dbContext.EndpointStates.Add(state);
        }

        state.AgentId = assignment.AgentId;
        state.EndpointId = assignment.EndpointId;

        var latestResult = await _dbContext.CheckResults
            .Where(x => x.AssignmentId == assignment.AssignmentId)
            .OrderByDescending(x => x.CheckedAtUtc)
            .ThenByDescending(x => x.ReceivedAtUtc)
            .ThenByDescending(x => x.CheckResultId)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestResult is not null && state.LastCheckUtc != latestResult.CheckedAtUtc)
        {
            state.LastCheckUtc = latestResult.CheckedAtUtc;
            if (latestResult.Success)
            {
                state.ConsecutiveSuccessCount += 1;
                state.ConsecutiveFailureCount = 0;
            }
            else
            {
                state.ConsecutiveFailureCount += 1;
                state.ConsecutiveSuccessCount = 0;
            }
        }

        var parentContext = await GetParentContextAsync(assignment, endpoint, cancellationToken);
        var previousState = state.CurrentState;
        var nextState = DetermineNextState(assignment, state, agent, parentContext);
        var reasonCode = GetReasonCode(previousState, nextState, parentContext.DependencyDown);

        state.CurrentState = nextState;
        state.SuppressedByEndpointId = nextState == EndpointStateKind.Suppressed
            ? parentContext.ParentEndpointId
            : null;

        if (previousState != nextState)
        {
            state.LastStateChangeUtc = DateTimeOffset.UtcNow;
            _dbContext.StateTransitions.Add(new StateTransition
            {
                TransitionId = Guid.NewGuid().ToString(),
                AssignmentId = assignment.AssignmentId,
                AgentId = assignment.AgentId,
                EndpointId = assignment.EndpointId,
                PreviousState = previousState,
                NewState = nextState,
                TransitionAtUtc = state.LastStateChangeUtc.Value,
                ReasonCode = reasonCode,
                DependencyEndpointId = nextState == EndpointStateKind.Suppressed ? parentContext.ParentEndpointId : null
            });

            _logger.LogInformation(
                "Assignment {AssignmentId} transitioned from {PreviousState} to {NewState} with reason {ReasonCode}.",
                assignment.AssignmentId,
                previousState,
                nextState,
                reasonCode ?? "(none)");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (CrossedDownBoundary(previousState, nextState))
        {
            return await GetDirectChildAssignmentIdsAsync(assignment, cancellationToken);
        }

        return Array.Empty<string>();
    }

    private async Task<ParentStateContext> GetParentContextAsync(
        MonitorAssignment assignment,
        EndpointModel endpoint,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint.DependsOnEndpointId))
        {
            return ParentStateContext.None;
        }

        var parentAssignment = await _dbContext.MonitorAssignments
            .SingleOrDefaultAsync(
                x => x.AgentId == assignment.AgentId && x.EndpointId == endpoint.DependsOnEndpointId,
                cancellationToken);

        if (parentAssignment is null)
        {
            return new ParentStateContext(endpoint.DependsOnEndpointId, false);
        }

        var parentState = await _dbContext.EndpointStates
            .SingleOrDefaultAsync(x => x.AssignmentId == parentAssignment.AssignmentId, cancellationToken);

        return new ParentStateContext(
            endpoint.DependsOnEndpointId,
            parentState?.CurrentState == EndpointStateKind.Down);
    }

    private async Task<IReadOnlyCollection<string>> GetDirectChildAssignmentIdsAsync(
        MonitorAssignment assignment,
        CancellationToken cancellationToken)
    {
        var childEndpointIds = await _dbContext.Endpoints
            .Where(x => x.DependsOnEndpointId == assignment.EndpointId)
            .Select(x => x.EndpointId)
            .ToArrayAsync(cancellationToken);

        if (childEndpointIds.Length == 0)
        {
            return Array.Empty<string>();
        }

        return await _dbContext.MonitorAssignments
            .Where(x => x.AgentId == assignment.AgentId && childEndpointIds.Contains(x.EndpointId))
            .Select(x => x.AssignmentId)
            .ToArrayAsync(cancellationToken);
    }

    private static EndpointStateKind DetermineNextState(
        MonitorAssignment assignment,
        EndpointState state,
        Agent agent,
        ParentStateContext parentContext)
    {
        if (!assignment.Enabled)
        {
            return EndpointStateKind.Unknown;
        }

        if (!agent.Enabled || agent.ApiKeyRevoked || agent.Status != AgentHealthStatus.Online)
        {
            return EndpointStateKind.Unknown;
        }

        var failureReached = state.ConsecutiveFailureCount >= assignment.FailureThreshold;
        var recoveryReached = state.ConsecutiveSuccessCount >= assignment.RecoveryThreshold;

        if (recoveryReached)
        {
            return EndpointStateKind.Up;
        }

        if (failureReached && parentContext.DependencyDown)
        {
            return EndpointStateKind.Suppressed;
        }

        if (failureReached)
        {
            return EndpointStateKind.Down;
        }

        // DEGRADED is intentionally dormant in v1 until degradation policy exists.
        return state.CurrentState;
    }

    private static string? GetReasonCode(EndpointStateKind previousState, EndpointStateKind newState, bool dependencyDown)
    {
        if (previousState == newState)
        {
            return null;
        }

        if (newState == EndpointStateKind.Unknown)
        {
            return StateTransitionReasonCodes.AgentContextLost;
        }

        if (newState == EndpointStateKind.Suppressed)
        {
            return StateTransitionReasonCodes.DependencyDown;
        }

        if (newState == EndpointStateKind.Up)
        {
            return previousState == EndpointStateKind.Suppressed
                ? StateTransitionReasonCodes.DependencyCleared
                : StateTransitionReasonCodes.RecoveryThresholdReached;
        }

        if (newState == EndpointStateKind.Down)
        {
            return previousState == EndpointStateKind.Suppressed && !dependencyDown
                ? StateTransitionReasonCodes.DependencyCleared
                : StateTransitionReasonCodes.FailureThresholdReached;
        }

        return null;
    }

    private static bool CrossedDownBoundary(EndpointStateKind previousState, EndpointStateKind newState)
    {
        var previousWasDown = previousState == EndpointStateKind.Down;
        var newIsDown = newState == EndpointStateKind.Down;
        return previousWasDown != newIsDown;
    }

    private readonly record struct ParentStateContext(string? ParentEndpointId, bool DependencyDown)
    {
        public static ParentStateContext None => new(null, false);
    }
}
