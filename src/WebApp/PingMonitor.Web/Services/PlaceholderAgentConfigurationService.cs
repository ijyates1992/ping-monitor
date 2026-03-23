using Microsoft.Extensions.Options;
using PingMonitor.Web.Contracts.Config;
using PingMonitor.Web.Models;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services;

internal sealed class PlaceholderAgentConfigurationService : IAgentConfigurationService
{
    private readonly AgentApiOptions _options;

    public PlaceholderAgentConfigurationService(IOptions<AgentApiOptions> options)
    {
        _options = options.Value;
    }

    public Task<AgentConfigResponse> GetConfigurationAsync(Agent agent, CancellationToken cancellationToken)
    {
        var response = new AgentConfigResponse(
            ConfigVersion: _options.ConfigVersion,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Assignments: []);

        return Task.FromResult(response);
    }

    public Task<string> GetCurrentConfigVersionAsync(Agent agent, CancellationToken cancellationToken)
    {
        return Task.FromResult(_options.ConfigVersion);
    }
}
