namespace PingMonitor.Web.Services.Metrics;

public interface IEndpointMetricsService
{
    Task<IReadOnlyDictionary<string, EndpointMetricSummary>> GetEndpointMetricSummariesAsync(
        IReadOnlyCollection<string> assignmentIds,
        CancellationToken cancellationToken);
}

public sealed class EndpointMetricSummary
{
    public double? UptimePercent { get; init; }
    public double? LastRttMs { get; init; }
    public double? HighestRttMs { get; init; }
    public double? LowestRttMs { get; init; }
    public double? AverageRttMs { get; init; }
    public double? JitterMs { get; init; }
}
