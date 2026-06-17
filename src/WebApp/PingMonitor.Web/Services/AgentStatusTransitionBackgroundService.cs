using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.EventLogs;
using PingMonitor.Web.Services.State;
using PingMonitor.Web.Services.StartupGate;
using PingMonitor.Web.Services.AiEventTasks;
using System.Text.Json;

namespace PingMonitor.Web.Services;

internal sealed class AgentStatusTransitionBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan OfflineThreshold = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IStartupGateRuntimeState _startupGateRuntimeState;
    private readonly ILogger<AgentStatusTransitionBackgroundService> _logger;

    public AgentStatusTransitionBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        IStartupGateRuntimeState startupGateRuntimeState,
        ILogger<AgentStatusTransitionBackgroundService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _startupGateRuntimeState = startupGateRuntimeState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var wasBlockedByStartupGate = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_startupGateRuntimeState.IsOperationalMode)
                {
                    if (!wasBlockedByStartupGate)
                    {
                        _logger.LogInformation("Agent status transition evaluation is paused because Startup Gate is active.");
                        wasBlockedByStartupGate = true;
                    }

                    await Task.Delay(PollInterval, stoppingToken);
                    continue;
                }

                if (wasBlockedByStartupGate)
                {
                    _logger.LogInformation("Startup Gate is cleared. Agent status transition evaluation is resuming normal operation.");
                    wasBlockedByStartupGate = false;
                }

                await EvaluateTransitionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Agent status transition evaluation failed.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task EvaluateTransitionsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PingMonitorDbContext>();
        var eventLogService = scope.ServiceProvider.GetRequiredService<IEventLogService>();
        var aiEventTasks = scope.ServiceProvider.GetRequiredService<IAiEventTriggeredTaskService>();
        var now = DateTimeOffset.UtcNow;

        var agents = await dbContext.Agents
            .Where(x => x.Enabled && !x.ApiKeyRevoked)
            .ToArrayAsync(cancellationToken);

        foreach (var agent in agents)
        {
            if (!agent.LastHeartbeatUtc.HasValue)
            {
                continue;
            }

            var previousStatus = agent.Status;
            var elapsed = now - agent.LastHeartbeatUtc.Value;
            var nextStatus = elapsed >= OfflineThreshold
                ? AgentHealthStatus.Offline
                : elapsed >= StaleThreshold
                    ? AgentHealthStatus.Stale
                    : AgentHealthStatus.Online;

            if (nextStatus == previousStatus)
            {
                continue;
            }

            if (nextStatus == AgentHealthStatus.Online)
            {
                continue;
            }

            agent.Status = nextStatus;
            await dbContext.SaveChangesAsync(cancellationToken);

            await eventLogService.WriteAsync(
                new EventLogWriteRequest
                {
                    OccurredAtUtc = now,
                    Category = EventCategory.Agent,
                    EventType = nextStatus == AgentHealthStatus.Stale ? EventType.AgentBecameStale : EventType.AgentBecameOffline,
                    Severity = EventSeverity.Error,
                    AgentId = agent.AgentId,
                    Message = nextStatus == AgentHealthStatus.Stale
                        ? $"Agent \"{agent.Name ?? agent.InstanceId}\" became stale."
                        : $"Agent \"{agent.Name ?? agent.InstanceId}\" became offline."
                },
                cancellationToken);

            await aiEventTasks.RecordAgentStateChangedAsync(
                new AgentStateChangedAiEvent(agent.AgentId, agent.Name ?? agent.InstanceId, agent.InstanceId, previousStatus, nextStatus, now),
                cancellationToken);

            if (nextStatus == AgentHealthStatus.Offline)
            {
                await TransitionAssignedEndpointsToUnknownAsync(dbContext, eventLogService, agent, now, cancellationToken);
            }
        }
    }

    internal static async Task TransitionAssignedEndpointsToUnknownAsync(
        PingMonitorDbContext dbContext,
        IEventLogService eventLogService,
        Agent agent,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!ShouldTransitionAssignmentsToUnknown(agent, now))
        {
            return;
        }

        var assignments = await dbContext.MonitorAssignments
            .Where(x => x.AgentId == agent.AgentId && x.Enabled)
            .Select(x => new { x.AssignmentId, x.EndpointId })
            .ToListAsync(cancellationToken);

        if (assignments.Count == 0)
        {
            return;
        }

        var states = await dbContext.EndpointStates
            .Where(x => x.AgentId == agent.AgentId)
            .ToDictionaryAsync(x => x.AssignmentId, x => x, cancellationToken);

        foreach (var assignment in assignments)
        {
            if (states.TryGetValue(assignment.AssignmentId, out var state)
                && state.CurrentState == EndpointStateKind.Unknown)
            {
                continue;
            }

            var previousState = states.TryGetValue(assignment.AssignmentId, out var existingState)
                ? existingState.CurrentState
                : EndpointStateKind.Unknown;

            var nextState = existingState ?? new EndpointState
            {
                AssignmentId = assignment.AssignmentId,
                AgentId = agent.AgentId,
                EndpointId = assignment.EndpointId
            };

            nextState.CurrentState = EndpointStateKind.Unknown;
            nextState.SuppressedByEndpointId = null;
            nextState.LastStateChangeUtc = now;

            if (existingState is null)
            {
                dbContext.EndpointStates.Add(nextState);
            }

            dbContext.StateTransitions.Add(new StateTransition
            {
                TransitionId = Guid.NewGuid().ToString(),
                AssignmentId = assignment.AssignmentId,
                AgentId = agent.AgentId,
                EndpointId = assignment.EndpointId,
                PreviousState = previousState,
                NewState = EndpointStateKind.Unknown,
                TransitionAtUtc = now,
                ReasonCode = StateTransitionReasonCodes.AgentOfflineTimeout
            });

            await eventLogService.WriteAsync(new EventLogWriteRequest
            {
                OccurredAtUtc = now,
                Category = EventCategory.Endpoint,
                EventType = EventType.EndpointStateChanged,
                Severity = EventSeverity.Info,
                AgentId = agent.AgentId,
                EndpointId = assignment.EndpointId,
                AssignmentId = assignment.AssignmentId,
                Message = "Endpoint state changed to Unknown because its assigned agent is offline beyond the configured timeout.",
                DetailsJson = JsonSerializer.Serialize(new
                {
                    assignment.AssignmentId,
                    assignment.EndpointId,
                    previousState = previousState.ToString(),
                    newState = EndpointStateKind.Unknown.ToString(),
                    reasonCode = StateTransitionReasonCodes.AgentOfflineTimeout,
                    agent.EndpointUnknownAfterAgentOfflineSeconds
                })
            }, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    internal static bool ShouldTransitionAssignmentsToUnknown(Agent agent, DateTimeOffset now)
    {
        var staleAfter = TimeSpan.FromSeconds(agent.EndpointUnknownAfterAgentOfflineSeconds <= 0
            ? Agent.DefaultEndpointUnknownAfterAgentOfflineSeconds
            : agent.EndpointUnknownAfterAgentOfflineSeconds);
        var lastSeenUtc = agent.LastSeenUtc ?? agent.LastHeartbeatUtc;
        return lastSeenUtc.HasValue && now - lastSeenUtc.Value >= staleAfter;
    }
}
