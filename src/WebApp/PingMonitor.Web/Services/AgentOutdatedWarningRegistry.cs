using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services;

internal sealed class AgentOutdatedWarningRegistry : IAgentOutdatedWarningRegistry
{
    private readonly PingMonitorDbContext _dbContext;

    public AgentOutdatedWarningRegistry(PingMonitorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> HasWarningAsync(string agentId, string bundledVersion, CancellationToken cancellationToken)
    {
        return _dbContext.EventLogs.AnyAsync(
            x => x.AgentId == agentId
                 && x.EventType == EventType.AgentOutdated
                 && x.DetailsJson == BuildDetailsMarker(bundledVersion),
            cancellationToken);
    }

    public static string BuildDetailsMarker(string bundledVersion)
    {
        return $"bundledVersion={bundledVersion}";
    }
}
