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
                EventType = EventType.AgentBecameOnline,
                Severity = EventSeverity.Info,
                AgentId = agent.AgentId,
                Message = $"Agent \"{agent.Name ?? agent.InstanceId}\" became online."
            }, cancellationToken);
        }

        return new AgentHeartbeatResponse(
            Ok: true,
            ServerTimeUtc: now,
            ConfigChanged: !string.Equals(request.ConfigVersion.Trim(), currentConfigVersion, StringComparison.Ordinal));
    }
}
