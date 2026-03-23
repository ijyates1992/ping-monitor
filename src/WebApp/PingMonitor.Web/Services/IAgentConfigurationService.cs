using PingMonitor.Web.Contracts.Config;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services;

public interface IAgentConfigurationService
{
    Task<AgentConfigResponse> GetConfigurationAsync(Agent agent, CancellationToken cancellationToken);
    Task<string> GetCurrentConfigVersionAsync(Agent agent, CancellationToken cancellationToken);
}
