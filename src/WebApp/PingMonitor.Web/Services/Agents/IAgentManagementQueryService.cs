namespace PingMonitor.Web.Services.Agents;

public interface IAgentManagementQueryService
{
    Task<IReadOnlyList<AgentManagementRow>> ListAsync(CancellationToken cancellationToken);
}
