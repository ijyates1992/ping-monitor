using PingMonitor.Web.Contracts.Heartbeat;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services;

internal sealed class PlaceholderHeartbeatService : IHeartbeatService
{
    public Task<AgentHeartbeatResponse> ProcessHeartbeatAsync(Agent agent, AgentHeartbeatRequest request, CancellationToken cancellationToken)
    {
        var response = new AgentHeartbeatResponse(
            Ok: true,
            ServerTimeUtc: DateTimeOffset.UtcNow,
            ConfigChanged: false);

        return Task.FromResult(response);
    }
}
