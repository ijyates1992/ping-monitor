using PingMonitor.Web.Data;
using PingMonitor.Web.Contracts.Heartbeat;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.EventLogs;

namespace PingMonitor.Web.Services;

internal sealed class AgentHeartbeatService : IHeartbeatService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly IAgentConfigurationService _configurationService;
    private readonly IEventLogService _eventLogService;

    public AgentHeartbeatService(
        PingMonitorDbContext dbContext,
        IAgentConfigurationService configurationService,
        IEventLogService eventLogService)
    {
        _dbContext = dbContext;
        _configurationService = configurationService;
        _eventLogService = eventLogService;
    }

    public async Task<AgentHeartbeatResponse> ProcessHeartbeatAsync(Agent agent, AgentHeartbeatRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var currentConfigVersion = await _configurationService.GetCurrentConfigVersionAsync(agent, cancellationToken);

        var wasOnline = agent.Status == AgentHealthStatus.Online;
        var previousHeartbeat = agent.LastHeartbeatUtc;
        agent.LastHeartbeatUtc = now;
        agent.LastSeenUtc = now;
        agent.AgentVersion = request.AgentVersion.Trim();
        agent.Status = AgentHealthStatus.Online;
        _dbContext.AgentHeartbeatHistories.Add(new AgentHeartbeatHistory
        {
            AgentHeartbeatHistoryId = $"ahh_{Guid.NewGuid():N}",
            AgentId = agent.AgentId,
            HeartbeatAtUtc = now,
            RecordedAtUtc = now
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!wasOnline)
        {
            await _eventLogService.WriteAsync(new EventLogWriteRequest
            {
                OccurredAtUtc = now,
                Category = EventCategory.Agent,
                EventType = EventType.AgentOnline,
                Severity = EventSeverity.Info,
                AgentId = agent.AgentId,
                Message = $"Agent \"{agent.Name ?? agent.InstanceId}\" is now online."
            }, cancellationToken);
        }

        if (!agent.LastHeartbeatEventLoggedAtUtc.HasValue ||
            now - agent.LastHeartbeatEventLoggedAtUtc.Value >= TimeSpan.FromMinutes(5) ||
            (previousHeartbeat.HasValue && now - previousHeartbeat.Value >= TimeSpan.FromMinutes(5)))
        {
            await _eventLogService.WriteAsync(new EventLogWriteRequest
            {
                OccurredAtUtc = now,
                Category = EventCategory.Agent,
                EventType = EventType.AgentHeartbeatReceived,
                Severity = EventSeverity.Info,
                AgentId = agent.AgentId,
                Message = $"Heartbeat received from agent \"{agent.Name ?? agent.InstanceId}\"."
            }, cancellationToken);

            agent.LastHeartbeatEventLoggedAtUtc = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return new AgentHeartbeatResponse(
            Ok: true,
            ServerTimeUtc: now,
            ConfigChanged: !string.Equals(request.ConfigVersion.Trim(), currentConfigVersion, StringComparison.Ordinal));
    }
}
