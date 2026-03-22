using PingMonitor.Web.Contracts.Config;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services;

internal sealed class PlaceholderAgentConfigurationService : IAgentConfigurationService
{
    public Task<AgentConfigResponse> GetConfigurationAsync(Agent agent, CancellationToken cancellationToken)
    {
        var response = new AgentConfigResponse(
            ConfigVersion: "cfg_placeholder_v1",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Assignments: []);

        return Task.FromResult(response);
    }
}
