using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services;

public interface IAgentOutdatedVersionWarningService
{
    Task TryWriteWarningAsync(Agent agent, string reportedAgentVersion, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken);
}
