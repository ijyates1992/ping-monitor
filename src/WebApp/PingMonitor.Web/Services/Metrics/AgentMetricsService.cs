using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;

namespace PingMonitor.Web.Services.Metrics;

internal sealed class AgentMetricsService : IAgentMetricsService
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);
    private readonly PingMonitorDbContext _dbContext;

    public AgentMetricsService(PingMonitorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyDictionary<string, AgentMetricSummary>> GetAgentMetricSummariesAsync(
        IReadOnlyCollection<string> agentIds,
        CancellationToken cancellationToken)
    {
        if (agentIds.Count == 0)
        {
            return new Dictionary<string, AgentMetricSummary>(StringComparer.Ordinal);
        }

        var now = DateTimeOffset.UtcNow;
        var windowStart = now - Window;
        var normalizedAgentIds = agentIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var summaries = normalizedAgentIds.ToDictionary(
            x => x,
            _ => new AgentMetricSummary { UptimePercent = 0d },
            StringComparer.Ordinal);

        const int totalWindowMinutes = 24 * 60;
        foreach (var agentId in normalizedAgentIds)
        {
            var minuteBuckets = new HashSet<int>();
            var heartbeats = await _dbContext.AgentHeartbeatHistories
                .AsNoTracking()
                .Where(x => x.AgentId == agentId && x.HeartbeatAtUtc >= windowStart && x.HeartbeatAtUtc <= now)
                .Select(x => x.HeartbeatAtUtc)
                .ToListAsync(cancellationToken);

            foreach (var heartbeatAtUtc in heartbeats)
            {
                var minuteIndex = (int)Math.Floor((heartbeatAtUtc - windowStart).TotalMinutes);
                if (minuteIndex < 0 || minuteIndex >= totalWindowMinutes)
                {
                    continue;
                }

                minuteBuckets.Add(minuteIndex);
            }

            var uptimePercent = minuteBuckets.Count / (double)totalWindowMinutes * 100d;
            summaries[agentId] = new AgentMetricSummary { UptimePercent = uptimePercent };
        }

        return summaries;
    }
}
