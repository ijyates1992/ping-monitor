using PingMonitor.Web.Data;
using PingMonitor.Web.Contracts.Heartbeat;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services;

internal sealed class AgentHeartbeatService : IHeartbeatService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly IAgentConfigurationService _configurationService;

    public AgentHeartbeatService(
        PingMonitorDbContext dbContext,
        IAgentConfigurationService configurationService)
    {
        _dbContext = dbContext;
        _configurationService = configurationService;
    }

    public async Task<AgentHeartbeatResponse> ProcessHeartbeatAsync(Agent agent, AgentHeartbeatRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var currentConfigVersion = await _configurationService.GetCurrentConfigVersionAsync(agent, cancellationToken);

        agent.LastHeartbeatUtc = now;
        agent.LastSeenUtc = now;
        agent.AgentVersion = request.AgentVersion.Trim();

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AgentHeartbeatResponse(
            Ok: true,
            ServerTimeUtc: now,
            ConfigChanged: !string.Equals(request.ConfigVersion.Trim(), currentConfigVersion, StringComparison.Ordinal));
    }
}
