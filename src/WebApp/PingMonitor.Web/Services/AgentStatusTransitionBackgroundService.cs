using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.EventLogs;

namespace PingMonitor.Web.Services;

internal sealed class AgentStatusTransitionBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan OfflineThreshold = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<AgentStatusTransitionBackgroundService> _logger;

    public AgentStatusTransitionBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<AgentStatusTransitionBackgroundService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
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
        }
    }
}
