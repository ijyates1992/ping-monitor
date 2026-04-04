using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.EventLogs;
using PingMonitor.Web.Services.Metrics;
using PingMonitor.Web.Services.Diagnostics;
using PingMonitor.Web.Services.State;
using System.Text.Json;

namespace PingMonitor.Web.Services;

internal sealed class StateEvaluationService : IStateEvaluationService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly ILogger<StateEvaluationService> _logger;
    private readonly IEventLogService _eventLogService;
    private readonly IAssignmentMetrics24hService _assignmentMetrics24hService;
    private readonly IAssignmentTopologyCache _topologyCache;
    private readonly IAssignmentCurrentStateCache _currentStateCache;
    private readonly IRollingAssignmentWindowStore _rollingAssignmentWindowStore;
    private readonly IDbActivityScope _dbActivityScope;

    public StateEvaluationService(
        PingMonitorDbContext dbContext,
        ILogger<StateEvaluationService> logger,
        IEventLogService eventLogService,
        IAssignmentMetrics24hService assignmentMetrics24hService,
        IAssignmentTopologyCache topologyCache,
        IAssignmentCurrentStateCache currentStateCache,
        IRollingAssignmentWindowStore rollingAssignmentWindowStore,
        IDbActivityScope dbActivityScope)
    {
        _dbContext = dbContext;
        _logger = logger;
        _eventLogService = eventLogService;
        _assignmentMetrics24hService = assignmentMetrics24hService;
        _topologyCache = topologyCache;
        _currentStateCache = currentStateCache;
        _rollingAssignmentWindowStore = rollingAssignmentWindowStore;
        _dbActivityScope = dbActivityScope;
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
        using var scope = _dbActivityScope.BeginScope("StateEvaluation");
        var assignmentContext = await _topologyCache.GetAssignmentContextAsync(assignmentId, cancellationToken);
        if (assignmentContext is null)
        {
            _logger.LogWarning("State evaluation skipped because assignment {AssignmentId} was not found in topology cache.", assignmentId);
            return Array.Empty<string>();
        }

        var state = await _currentStateCache.GetStateAsync(
            assignmentContext.AssignmentId,
            assignmentContext.AgentId,
            assignmentContext.EndpointId,
            cancellationToken);

        var latestResult = await _rollingAssignmentWindowStore.GetLatestResultAsync(assignmentContext.AssignmentId, cancellationToken);
        var mutableState = CloneState(state);
        mutableState.AgentId = assignmentContext.AgentId;
        mutableState.EndpointId = assignmentContext.EndpointId;

        if (latestResult is not null && mutableState.LastCheckUtc != latestResult.CheckedAtUtc)
        {
            mutableState.LastCheckUtc = latestResult.CheckedAtUtc;
            if (latestResult.Success)
            {
                mutableState.ConsecutiveSuccessCount += 1;
                mutableState.ConsecutiveFailureCount = 0;
            }
            else
            {
                mutableState.ConsecutiveFailureCount += 1;
                mutableState.ConsecutiveSuccessCount = 0;
            }
        }

        var parentStateRequests = assignmentContext.ParentDependencies
            .Select(x => new AssignmentStateLookupRequest
            {
                AssignmentId = x.ParentAssignmentId,
                AgentId = assignmentContext.AgentId,
                EndpointId = x.ParentEndpointId
            })
            .ToArray();

        var parentStates = await _currentStateCache.GetStatesAsync(parentStateRequests, cancellationToken);
        var parentContext = GetParentContext(assignmentContext, parentStates);

        var previousState = mutableState.CurrentState;
        var previousStateChangedAtUtc = mutableState.LastStateChangeUtc ?? DateTimeOffset.UtcNow;
        var nextState = DetermineNextState(assignmentContext, mutableState, parentContext);
        var reasonCode = GetReasonCode(previousState, nextState, parentContext.DependencyDown);

        mutableState.CurrentState = nextState;
        mutableState.SuppressedByEndpointId = nextState == EndpointStateKind.Suppressed
            ? parentContext.ParentEndpointId
            : null;

        DateTimeOffset? transitionAtUtc = null;
        if (previousState != nextState)
        {
            transitionAtUtc = DateTimeOffset.UtcNow;
            mutableState.LastStateChangeUtc = transitionAtUtc.Value;

            _dbContext.StateTransitions.Add(new StateTransition
            {
                TransitionId = Guid.NewGuid().ToString(),
                AssignmentId = assignmentContext.AssignmentId,
                AgentId = assignmentContext.AgentId,
                EndpointId = assignmentContext.EndpointId,
                PreviousState = previousState,
                NewState = nextState,
                TransitionAtUtc = transitionAtUtc.Value,
                ReasonCode = reasonCode,
                DependencyEndpointId = nextState == EndpointStateKind.Suppressed ? parentContext.ParentEndpointId : null
            });

            _logger.LogInformation(
                "Assignment {AssignmentId} transitioned from {PreviousState} to {NewState} with reason {ReasonCode}.",
                assignmentContext.AssignmentId,
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
                    AgentId = assignmentContext.AgentId,
                    EndpointId = assignmentContext.EndpointId,
                    AssignmentId = assignmentContext.AssignmentId,
                    Message = BuildStateChangeMessage(
                        assignmentContext.EndpointName,
                        previousState,
                        nextState,
                        transitionAtUtc.Value,
                        previousStateChangedAtUtc),
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        assignmentContext.AssignmentId,
                        assignmentContext.AgentId,
                        assignmentContext.EndpointId,
                        previousState = previousState.ToString(),
                        newState = nextState.ToString(),
                        reasonCode,
                        dependencyEndpointId = mutableState.SuppressedByEndpointId
                    })
                },
                cancellationToken);
        }

        var endpointStateEntity = new EndpointState
        {
            AssignmentId = mutableState.AssignmentId,
            AgentId = mutableState.AgentId,
            EndpointId = mutableState.EndpointId,
            CurrentState = mutableState.CurrentState,
            ConsecutiveFailureCount = mutableState.ConsecutiveFailureCount,
            ConsecutiveSuccessCount = mutableState.ConsecutiveSuccessCount,
            LastCheckUtc = mutableState.LastCheckUtc,
            LastStateChangeUtc = mutableState.LastStateChangeUtc,
            SuppressedByEndpointId = mutableState.SuppressedByEndpointId
        };

        if (mutableState.ExistsInDatabase)
        {
            _dbContext.EndpointStates.Update(endpointStateEntity);
        }
        else
        {
            _dbContext.EndpointStates.Add(endpointStateEntity);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        mutableState.ExistsInDatabase = true;
        _currentStateCache.Upsert(mutableState);

        await _assignmentMetrics24hService.ApplyStateEvaluationAsync(
            assignmentContext.AssignmentId,
            previousState,
            mutableState.CurrentState,
            transitionAtUtc,
            previousStateChangedAtUtc,
            DateTimeOffset.UtcNow,
            cancellationToken);

        if (CrossedDownBoundary(previousState, nextState))
        {
            return assignmentContext.ChildAssignmentIds;
        }

        return Array.Empty<string>();
    }

    private static ParentStateContext GetParentContext(
        AssignmentTopologyContext assignment,
        IReadOnlyDictionary<string, CachedAssignmentState> parentStates)
    {
        foreach (var parent in assignment.ParentDependencies)
        {
            if (!parentStates.TryGetValue(parent.ParentAssignmentId, out var parentState))
            {
                continue;
            }

            if (parentState.CurrentState == EndpointStateKind.Down)
            {
                return new ParentStateContext(parent.ParentEndpointId, true);
            }
        }

        return ParentStateContext.None;
    }

    private static EndpointStateKind DetermineNextState(
        AssignmentTopologyContext assignment,
        CachedAssignmentState state,
        ParentStateContext parentContext)
    {
        if (!assignment.AssignmentEnabled)
        {
            return EndpointStateKind.Unknown;
        }

        if (!assignment.AgentEnabled || assignment.AgentApiKeyRevoked || assignment.AgentStatus != AgentHealthStatus.Online)
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

    private static string BuildStateChangeMessage(
        string endpointName,
        EndpointStateKind previousState,
        EndpointStateKind nextState,
        DateTimeOffset transitionAtUtc,
        DateTimeOffset previousStateChangedAtUtc)
    {
        if (nextState == EndpointStateKind.Down)
        {
            return $"Endpoint \"{endpointName}\" went down.";
        }

        if (previousState == EndpointStateKind.Down && nextState == EndpointStateKind.Up)
        {
            var downtime = transitionAtUtc - previousStateChangedAtUtc;
            return $"Endpoint \"{endpointName}\" recovered after {FormatDuration(downtime)} downtime.";
        }

        return $"Endpoint \"{endpointName}\" state changed from {previousState} to {nextState}.";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var safeDuration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        var hours = (int)safeDuration.TotalHours;
        return $"{hours:D2}:{safeDuration.Minutes:D2}:{safeDuration.Seconds:D2}";
    }

    private static CachedAssignmentState CloneState(CachedAssignmentState state)
    {
        return new CachedAssignmentState
        {
            AssignmentId = state.AssignmentId,
            AgentId = state.AgentId,
            EndpointId = state.EndpointId,
            CurrentState = state.CurrentState,
            ConsecutiveFailureCount = state.ConsecutiveFailureCount,
            ConsecutiveSuccessCount = state.ConsecutiveSuccessCount,
            LastCheckUtc = state.LastCheckUtc,
            LastStateChangeUtc = state.LastStateChangeUtc,
            SuppressedByEndpointId = state.SuppressedByEndpointId,
            ExistsInDatabase = state.ExistsInDatabase
        };
    }

    private readonly record struct ParentStateContext(string? ParentEndpointId, bool DependencyDown)
    {
        public static ParentStateContext None => new(null, false);
    }
}
