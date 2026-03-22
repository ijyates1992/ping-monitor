using PingMonitor.Web.Contracts.Heartbeat;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services;

public interface IHeartbeatService
{
    Task<AgentHeartbeatResponse> ProcessHeartbeatAsync(Agent agent, AgentHeartbeatRequest request, CancellationToken cancellationToken);
}
