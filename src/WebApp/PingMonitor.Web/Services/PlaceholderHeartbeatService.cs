using PingMonitor.Web.Contracts.Heartbeat;

namespace PingMonitor.Web.Services;

internal sealed class PlaceholderHeartbeatService : IHeartbeatService
{
    public Task<AgentHeartbeatResponse> ProcessHeartbeatAsync(string instanceId, AgentHeartbeatRequest request, CancellationToken cancellationToken)
    {
        var response = new AgentHeartbeatResponse(
            Ok: true,
            ServerTimeUtc: DateTimeOffset.UtcNow,
            ConfigChanged: false);

        return Task.FromResult(response);
    }
}
