namespace PingMonitor.Web.Services;

public interface IAgentPackageBuilder
{
    Task<byte[]> BuildAsync(string serverUrl, string instanceId, string apiKey, CancellationToken cancellationToken);
}
