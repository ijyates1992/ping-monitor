namespace PingMonitor.Web.Services.Metrics;

public interface IAgentMetricsService
{
    Task<IReadOnlyDictionary<string, AgentMetricSummary>> GetAgentMetricSummariesAsync(
        IReadOnlyCollection<string> agentIds,
        CancellationToken cancellationToken);
}

public sealed class AgentMetricSummary
{
    public double? UptimePercent { get; init; }
}
