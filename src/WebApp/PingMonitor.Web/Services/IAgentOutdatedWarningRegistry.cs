namespace PingMonitor.Web.Services;

public interface IAgentOutdatedWarningRegistry
{
    Task<bool> HasWarningAsync(string agentId, string bundledVersion, CancellationToken cancellationToken);
}
