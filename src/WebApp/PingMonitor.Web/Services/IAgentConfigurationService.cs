using PingMonitor.Web.Contracts.Config;
using PingMonitor.Web.Contracts.Hello;

namespace PingMonitor.Web.Services;

public interface IAgentConfigurationService
{
    Task<AgentHelloResponse> BuildHelloResponseAsync(string instanceId, AgentHelloRequest request, CancellationToken cancellationToken);
    Task<AgentConfigResponse> GetConfigurationAsync(string instanceId, CancellationToken cancellationToken);
}
