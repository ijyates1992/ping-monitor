using Microsoft.Extensions.Options;
using PingMonitor.Web.Contracts.Hello;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services;

internal sealed class AgentHelloService : IAgentHelloService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly AgentApiOptions _options;

    public AgentHelloService(PingMonitorDbContext dbContext, IOptions<AgentApiOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public async Task<AgentHelloResponse> ProcessHelloAsync(Agent agent, AgentHelloRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        agent.LastSeenUtc = now;
        agent.LastHeartbeatUtc = now;
        agent.AgentVersion = request.AgentVersion.Trim();
        agent.MachineName = request.MachineName.Trim();
        agent.Platform = request.Platform.Trim();
        agent.Status = AgentHealthStatus.Online;

        await _dbContext.SaveChangesAsync(cancellationToken);

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
