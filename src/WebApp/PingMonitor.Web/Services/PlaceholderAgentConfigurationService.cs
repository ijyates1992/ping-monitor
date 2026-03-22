using PingMonitor.Web.Contracts.Config;
using PingMonitor.Web.Contracts.Hello;

namespace PingMonitor.Web.Services;

internal sealed class PlaceholderAgentConfigurationService : IAgentConfigurationService
{
    public Task<AgentHelloResponse> BuildHelloResponseAsync(string instanceId, AgentHelloRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var response = new AgentHelloResponse(
            AgentId: instanceId,
            ServerTimeUtc: now,
            ConfigRefreshSeconds: 300,
            HeartbeatIntervalSeconds: 60,
            ResultBatchIntervalSeconds: 10,
            MaxResultBatchSize: 500,
            ConfigVersion: "cfg_placeholder_v1");

        return Task.FromResult(response);
    }

    public Task<AgentConfigResponse> GetConfigurationAsync(string instanceId, CancellationToken cancellationToken)
    {
        var response = new AgentConfigResponse(
            ConfigVersion: "cfg_placeholder_v1",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Assignments: []);

        return Task.FromResult(response);
    }
}
