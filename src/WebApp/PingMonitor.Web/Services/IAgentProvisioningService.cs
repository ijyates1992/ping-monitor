namespace PingMonitor.Web.Services;

public interface IAgentProvisioningService
{
    Task<AgentProvisioningResult> ProvisionAsync(string agentName, CancellationToken cancellationToken);
    Task<bool> SetEnabledAsync(string agentId, bool enabled, CancellationToken cancellationToken);
    Task<AgentProvisioningResult> RotatePackageAsync(string agentId, CancellationToken cancellationToken);
}
