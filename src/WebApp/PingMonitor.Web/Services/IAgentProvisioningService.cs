namespace PingMonitor.Web.Services;

public interface IAgentProvisioningService
{
    Task<AgentProvisioningResult> ProvisionAsync(string agentName, CancellationToken cancellationToken);
}
