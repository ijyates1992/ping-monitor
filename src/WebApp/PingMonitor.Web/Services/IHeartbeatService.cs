using PingMonitor.Web.Contracts.Heartbeat;

namespace PingMonitor.Web.Services;

public interface IHeartbeatService
{
    Task<AgentHeartbeatResponse> ProcessHeartbeatAsync(string instanceId, AgentHeartbeatRequest request, CancellationToken cancellationToken);
}
