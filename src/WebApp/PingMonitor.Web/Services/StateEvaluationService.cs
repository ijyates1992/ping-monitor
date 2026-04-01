using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.EventLogs;
using PingMonitor.Web.Services.Metrics;
using System.Text.Json;

namespace PingMonitor.Web.Services;

internal sealed class StateEvaluationService : IStateEvaluationService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly ILogger<StateEvaluationService> _logger;
    private readonly IEventLogService _eventLogService;
    private readonly IAssignmentMetrics24hService _assignmentMetrics24hService;

    public StateEvaluationService(
        PingMonitorDbContext dbContext,
        ILogger<StateEvaluationService> logger,
        IEventLogService eventLogService,
        IAssignmentMetrics24hService assignmentMetrics24hService)
    {
        _dbContext = dbContext;
        _logger = logger;
        _eventLogService = eventLogService;
        _assignmentMetrics24hService = assignmentMetrics24hService;
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

        var parentContext = await GetParentContextAsync(assignment, endpoint.EndpointId, cancellationToken);
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
            var transitionAtUtc = state.LastStateChangeUtc.Value;
            _dbContext.StateTransitions.Add(new StateTransition
            {
                TransitionId = Guid.NewGuid().ToString(),
                AssignmentId = assignment.AssignmentId,
                AgentId = assignment.AgentId,
                EndpointId = assignment.EndpointId,
                PreviousState = previousState,
                NewState = nextState,
                TransitionAtUtc = transitionAtUtc,
                ReasonCode = reasonCode,
                DependencyEndpointId = nextState == EndpointStateKind.Suppressed ? parentContext.ParentEndpointId : null
            });

            _logger.LogInformation(
                "Assignment {AssignmentId} transitioned from {PreviousState} to {NewState} with reason {ReasonCode}.",
                assignment.AssignmentId,
                previousState,
                nextState,
                reasonCode ?? "(none)");

            var eventType = EventType.EndpointStateChanged;
            if (previousState != EndpointStateKind.Suppressed && nextState == EndpointStateKind.Suppressed)
            {
                eventType = EventType.EndpointSuppressionApplied;
            }
            else if (previousState == EndpointStateKind.Suppressed && nextState != EndpointStateKind.Suppressed)
            {
                eventType = EventType.EndpointSuppressionCleared;
            }

            await _eventLogService.WriteAsync(
                new EventLogWriteRequest
                {
                    OccurredAtUtc = transitionAtUtc,
                    Category = EventCategory.Endpoint,
                    EventType = eventType,
                    Severity = nextState == EndpointStateKind.Down ? EventSeverity.Error : EventSeverity.Info,
                    AgentId = assignment.AgentId,
                    EndpointId = assignment.EndpointId,
                    AssignmentId = assignment.AssignmentId,
                    Message = await BuildStateChangeMessageAsync(
                        assignment.AssignmentId,
                        endpoint.Name,
                        previousState,
                        nextState,
                        transitionAtUtc,
                        cancellationToken),
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        assignment.AssignmentId,
                        assignment.AgentId,
                        assignment.EndpointId,
                        previousState = previousState.ToString(),
                        newState = nextState.ToString(),
                        reasonCode,
                        dependencyEndpointId = state.SuppressedByEndpointId
                    })
                },
                cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _assignmentMetrics24hService.RefreshAssignmentAsync(assignment.AssignmentId, cancellationToken);

        if (CrossedDownBoundary(previousState, nextState))
        {
            return await GetDirectChildAssignmentIdsAsync(assignment, cancellationToken);
        }

        return Array.Empty<string>();
    }

    private async Task<ParentStateContext> GetParentContextAsync(
        MonitorAssignment assignment,
        string endpointId,
        CancellationToken cancellationToken)
    {
        var directParentEndpointIds = await _dbContext.EndpointDependencies.AsNoTracking()
            .Where(x => x.EndpointId == endpointId)
            .Select(x => x.DependsOnEndpointId)
            .ToArrayAsync(cancellationToken);

        if (directParentEndpointIds.Length == 0)
        {
            return ParentStateContext.None;
        }

        var parentAssignments = await _dbContext.EndpointDependencies.AsNoTracking()
            .Where(x => x.EndpointId == endpointId)
            .Join(
                _dbContext.MonitorAssignments.AsNoTracking().Where(x => x.AgentId == assignment.AgentId),
                dependency => dependency.DependsOnEndpointId,
                monitorAssignment => monitorAssignment.EndpointId,
                (dependency, monitorAssignment) => new
                {
                    ParentEndpointId = dependency.DependsOnEndpointId,
                    monitorAssignment.AssignmentId
                })
            .ToDictionaryAsync(x => x.ParentEndpointId, x => x.AssignmentId, cancellationToken);

        if (parentAssignments.Count == 0)
        {
            return new ParentStateContext(null, false);
        }

        var parentStates = await _dbContext.EndpointStates.AsNoTracking()
            .Where(x => x.AgentId == assignment.AgentId)
            .Join(
                _dbContext.MonitorAssignments.AsNoTracking(),
                state => state.AssignmentId,
                monitorAssignment => monitorAssignment.AssignmentId,
                (state, monitorAssignment) => new
                {
                    monitorAssignment.EndpointId,
                    state.CurrentState
                })
            .ToArrayAsync(cancellationToken);

        foreach (var parentEndpointId in directParentEndpointIds)
        {
            if (!parentAssignments.ContainsKey(parentEndpointId))
            {
                continue;
            }

            if (parentStates.Any(x => x.EndpointId == parentEndpointId && x.CurrentState == EndpointStateKind.Down))
            {
                return new ParentStateContext(parentEndpointId, true);
            }
        }

        return new ParentStateContext(null, false);
    }

    private async Task<IReadOnlyCollection<string>> GetDirectChildAssignmentIdsAsync(
        MonitorAssignment assignment,
        CancellationToken cancellationToken)
    {
        var childEndpointIds = await _dbContext.EndpointDependencies.AsNoTracking()
            .Where(x => x.DependsOnEndpointId == assignment.EndpointId)
            .Select(x => x.EndpointId)
            .Distinct()
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

    private async Task<string> BuildStateChangeMessageAsync(
        string assignmentId,
        string endpointName,
        EndpointStateKind previousState,
        EndpointStateKind nextState,
        DateTimeOffset transitionAtUtc,
        CancellationToken cancellationToken)
    {
        if (nextState == EndpointStateKind.Down)
        {
            return $"Endpoint \"{endpointName}\" went down.";
        }

        if (previousState == EndpointStateKind.Down && nextState == EndpointStateKind.Up)
        {
            var downTransitionAtUtc = await _dbContext.StateTransitions.AsNoTracking()
                .Where(x => x.AssignmentId == assignmentId && x.NewState == EndpointStateKind.Down)
                .OrderByDescending(x => x.TransitionAtUtc)
                .Select(x => (DateTimeOffset?)x.TransitionAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (downTransitionAtUtc.HasValue && transitionAtUtc >= downTransitionAtUtc.Value)
            {
                var downtime = transitionAtUtc - downTransitionAtUtc.Value;
                return $"Endpoint \"{endpointName}\" recovered after {FormatDuration(downtime)} downtime.";
            }

            return $"Endpoint \"{endpointName}\" recovered.";
        }

        return $"Endpoint \"{endpointName}\" state changed from {previousState} to {nextState}.";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var safeDuration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        var hours = (int)safeDuration.TotalHours;
        return $"{hours:D2}:{safeDuration.Minutes:D2}:{safeDuration.Seconds:D2}";
    }

    private readonly record struct ParentStateContext(string? ParentEndpointId, bool DependencyDown)
    {
        public static ParentStateContext None => new(null, false);
    }
}
