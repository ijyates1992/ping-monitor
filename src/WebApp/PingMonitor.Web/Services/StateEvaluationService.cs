using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.EventLogs;
using PingMonitor.Web.Services.Metrics;
using PingMonitor.Web.Services.Diagnostics;
using PingMonitor.Web.Services.State;
using PingMonitor.Web.Services.AiEventTasks;
using System.Diagnostics;
using System.Text.Json;

namespace PingMonitor.Web.Services;

internal sealed class StateEvaluationService : IStateEvaluationService
{
    private const int RollingMetricsWindowMinutes = 1440;
    private readonly PingMonitorDbContext _dbContext;
    private readonly ILogger<StateEvaluationService> _logger;
    private readonly IEventLogService _eventLogService;
    private readonly IAssignmentMetrics24hService _assignmentMetrics24hService;
    private readonly IAssignmentTopologyCache _topologyCache;
    private readonly IAssignmentCurrentStateCache _currentStateCache;
    private readonly IRollingAssignmentWindowStore _rollingAssignmentWindowStore;
    private readonly IDbActivityScope _dbActivityScope;
    private readonly IAiEventTriggeredTaskService _aiEventTasks;
    private DegradedEndpointEvaluationSettings? _degradedSettings;

    public StateEvaluationService(
        PingMonitorDbContext dbContext,
        ILogger<StateEvaluationService> logger,
        IEventLogService eventLogService,
        IAssignmentMetrics24hService assignmentMetrics24hService,
        IAssignmentTopologyCache topologyCache,
        IAssignmentCurrentStateCache currentStateCache,
        IRollingAssignmentWindowStore rollingAssignmentWindowStore,
        IDbActivityScope dbActivityScope,
        IAiEventTriggeredTaskService aiEventTasks)
    {
        _dbContext = dbContext;
        _logger = logger;
        _eventLogService = eventLogService;
        _assignmentMetrics24hService = assignmentMetrics24hService;
        _topologyCache = topologyCache;
        _currentStateCache = currentStateCache;
        _rollingAssignmentWindowStore = rollingAssignmentWindowStore;
        _dbActivityScope = dbActivityScope;
        _aiEventTasks = aiEventTasks;
    }

    public async Task EvaluateAssignmentsAsync(IEnumerable<string> assignmentIds, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var pendingAssignmentIds = new Queue<string>(assignmentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal));
        var processedAssignmentIds = new HashSet<string>(StringComparer.Ordinal);
        var stateChangeCount = 0;
        var degradedCacheEvaluationCount = 0;

        while (pendingAssignmentIds.Count > 0)
        {
            var assignmentId = pendingAssignmentIds.Dequeue();
            if (!processedAssignmentIds.Add(assignmentId))
            {
                continue;
            }

            var evaluation = await EvaluateInternalAsync(assignmentId, cancellationToken);
            if (evaluation.StateChanged)
            {
                stateChangeCount += 1;
            }

            if (evaluation.UsedDegradedMetricsCache)
            {
                degradedCacheEvaluationCount += 1;
            }

            if (evaluation.TransitionedChildAssignmentIds.Count == 0)
            {
                continue;
            }

            foreach (var childAssignmentId in evaluation.TransitionedChildAssignmentIds)
            {
                if (!processedAssignmentIds.Contains(childAssignmentId))
                {
                    pendingAssignmentIds.Enqueue(childAssignmentId);
                }
            }
        }

        stopwatch.Stop();
        _logger.LogDebug(
            "State evaluation completed for {EvaluatedAssignmentCount} assignments in {ElapsedMs} ms. StateChanges={StateChangeCount}; DegradedMetricsSource={DegradedMetricsSource}; DegradedCacheEvaluations={DegradedCacheEvaluationCount}.",
            processedAssignmentIds.Count,
            stopwatch.ElapsedMilliseconds,
            stateChangeCount,
            degradedCacheEvaluationCount > 0 ? "RollingCache" : "NotEvaluated",
            degradedCacheEvaluationCount);
    }

    public Task EvaluateAssignmentStateAsync(string assignmentId, CancellationToken cancellationToken)
    {
        return EvaluateAssignmentsAsync([assignmentId], cancellationToken);
    }

    private async Task<AssignmentEvaluationOutcome> EvaluateInternalAsync(string assignmentId, CancellationToken cancellationToken)
    {
        using var scope = _dbActivityScope.BeginScope("StateEvaluation");
        var assignmentContext = await _topologyCache.GetAssignmentContextAsync(assignmentId, cancellationToken);
        if (assignmentContext is null)
        {
            _logger.LogWarning("State evaluation skipped because assignment {AssignmentId} was not found in topology cache.", assignmentId);
            return AssignmentEvaluationOutcome.NoChange;
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
        var baseNextState = DetermineNextState(assignmentContext, mutableState, parentContext);
        var usedDegradedMetricsCache = baseNextState == EndpointStateKind.Up;
        var degradedEvaluation = usedDegradedMetricsCache
            ? await EvaluateDegradedAsync(assignmentContext.AssignmentId, DateTimeOffset.UtcNow, cancellationToken)
            : DegradedEndpointEvaluationResult.NotDegraded;
        var nextState = DegradedEndpointStatePriority.Apply(baseNextState, degradedEvaluation);
        var reasonCode = GetReasonCode(previousState, nextState, parentContext.DependencyDown, degradedEvaluation.IsDegraded);

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
                    Message = StateChangeEventLogMessageBuilder.Build(
                        assignmentContext.EndpointName,
                        previousState,
                        nextState,
                        transitionAtUtc.Value,
                        previousStateChangedAtUtc,
                        degradedEvaluation),
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        assignmentContext.AssignmentId,
                        assignmentContext.AgentId,
                        assignmentContext.EndpointId,
                        previousState = previousState.ToString(),
                        newState = nextState.ToString(),
                        reasonCode,
                        dependencyEndpointId = mutableState.SuppressedByEndpointId,
                        degraded = new
                        {
                            degradedEvaluation.IsDegraded,
                            degradedEvaluation.ReasonSummary,
                            degradedEvaluation.BaselineSampleCount,
                            degradedEvaluation.CurrentSampleCount,
                            degradedEvaluation.BaselinePacketLossPercent,
                            degradedEvaluation.CurrentPacketLossPercent,
                            degradedEvaluation.BaselineAverageRttMs,
                            degradedEvaluation.CurrentAverageRttMs,
                            degradedEvaluation.BaselineJitterMs,
                            degradedEvaluation.CurrentJitterMs,
                            degradedEvaluation.BaselineJitterSampleCount,
                            degradedEvaluation.CurrentJitterSampleCount,
                            degradedEvaluation.PacketLossDegraded,
                            degradedEvaluation.RttDegraded,
                            degradedEvaluation.JitterDegraded
                        }
                    })
                },
                cancellationToken);

            await _aiEventTasks.RecordEndpointStateChangedAsync(
                new EndpointStateChangedAiEvent(assignmentContext.AssignmentId, assignmentContext.AgentId, assignmentContext.EndpointId, assignmentContext.EndpointName, string.Empty, previousState, nextState, transitionAtUtc.Value),
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
            return new AssignmentEvaluationOutcome(
                assignmentContext.ChildAssignmentIds,
                previousState != nextState,
                usedDegradedMetricsCache);
        }

        return new AssignmentEvaluationOutcome(
            Array.Empty<string>(),
            previousState != nextState,
            usedDegradedMetricsCache);
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

    private async Task<DegradedEndpointEvaluationResult> EvaluateDegradedAsync(
        string assignmentId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var settings = await GetDegradedSettingsAsync(cancellationToken);
        if (!settings.Enabled || settings.BaselineLookbackMinutes <= settings.CurrentWindowMinutes)
        {
            return DegradedEndpointEvaluationResult.NotDegraded;
        }

        var metrics = await _rollingAssignmentWindowStore.GetDegradedMetricsAsync(
            assignmentId,
            nowUtc,
            settings.BaselineLookbackMinutes,
            settings.CurrentWindowMinutes,
            settings.MinimumSamples,
            cancellationToken);

        return DegradedEndpointEvaluator.Evaluate(metrics, settings);
    }

    private async Task<DegradedEndpointEvaluationSettings> GetDegradedSettingsAsync(CancellationToken cancellationToken)
    {
        if (_degradedSettings is not null)
        {
            return _degradedSettings;
        }

        var settings = await _dbContext.ApplicationSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ApplicationSettingsId == ApplicationSettings.SingletonId, cancellationToken);

        if (settings is null)
        {
            _degradedSettings = new DegradedEndpointEvaluationSettings();
            return _degradedSettings;
        }

        _degradedSettings = new DegradedEndpointEvaluationSettings
        {
            Enabled = settings.DegradedEvaluationEnabled,
            BaselineLookbackMinutes = Math.Clamp(settings.DegradedBaselineLookbackMinutes, 1, RollingMetricsWindowMinutes),
            CurrentWindowMinutes = Math.Clamp(settings.DegradedCurrentWindowMinutes, 1, RollingMetricsWindowMinutes),
            PacketLossIncreasePercentagePoints = Math.Clamp(settings.DegradedPacketLossIncreasePercentagePoints, 0d, 100d),
            RttIncreasePercent = Math.Max(0d, settings.DegradedRttIncreasePercent),
            JitterIncreasePercent = Math.Max(0d, settings.DegradedJitterIncreasePercent),
            MinimumSamples = Math.Max(1, settings.DegradedMinimumSamples)
        };
        return _degradedSettings;
    }

    private static string? GetReasonCode(EndpointStateKind previousState, EndpointStateKind newState, bool dependencyDown, bool degraded)
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

        if (previousState == EndpointStateKind.Degraded && newState == EndpointStateKind.Up)
        {
            return StateTransitionReasonCodes.DegradedThresholdCleared;
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

        if (newState == EndpointStateKind.Degraded && degraded)
        {
            return StateTransitionReasonCodes.DegradedThresholdReached;
        }

        return null;
    }

    private static bool CrossedDownBoundary(EndpointStateKind previousState, EndpointStateKind newState)
    {
        var previousWasDown = previousState == EndpointStateKind.Down;
        var newIsDown = newState == EndpointStateKind.Down;
        return previousWasDown != newIsDown;
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

    private sealed record AssignmentEvaluationOutcome(
        IReadOnlyCollection<string> TransitionedChildAssignmentIds,
        bool StateChanged,
        bool UsedDegradedMetricsCache)
    {
        public static AssignmentEvaluationOutcome NoChange { get; } = new(Array.Empty<string>(), false, false);
    }
}
