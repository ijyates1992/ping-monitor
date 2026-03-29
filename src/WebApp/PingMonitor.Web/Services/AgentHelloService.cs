using Microsoft.Extensions.Options;
using PingMonitor.Web.Contracts.Hello;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Options;
using PingMonitor.Web.Services.EventLogs;

namespace PingMonitor.Web.Services;

internal sealed class AgentHelloService : IAgentHelloService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly AgentApiOptions _options;
    private readonly IEventLogService _eventLogService;

    public AgentHelloService(PingMonitorDbContext dbContext, IOptions<AgentApiOptions> options, IEventLogService eventLogService)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _eventLogService = eventLogService;
    }

    public async Task<AgentHelloResponse> ProcessHelloAsync(Agent agent, AgentHelloRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        agent.LastSeenUtc = now;
        agent.LastHeartbeatUtc = now;
        agent.AgentVersion = request.AgentVersion.Trim();
        agent.MachineName = request.MachineName.Trim();
        agent.Platform = request.Platform.Trim();
        var wasOnline = agent.Status == AgentHealthStatus.Online;
        agent.Status = AgentHealthStatus.Online;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            OccurredAtUtc = now,
            Category = EventCategory.Agent,
            EventType = EventType.AgentAuthenticated,
            Severity = EventSeverity.Info,
            AgentId = agent.AgentId,
            Message = $"Agent \"{agent.Name ?? agent.InstanceId}\" authenticated successfully (hello)."
        }, cancellationToken);

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

        return new AgentHelloResponse(
            AgentId: agent.AgentId,
            ServerTimeUtc: now,
            ConfigRefreshSeconds: _options.ConfigRefreshSeconds,
            HeartbeatIntervalSeconds: _options.HeartbeatIntervalSeconds,
            ResultBatchIntervalSeconds: _options.ResultBatchIntervalSeconds,
            MaxResultBatchSize: _options.MaxResultBatchSize,
            ConfigVersion: _options.ConfigVersion);
    }
}
