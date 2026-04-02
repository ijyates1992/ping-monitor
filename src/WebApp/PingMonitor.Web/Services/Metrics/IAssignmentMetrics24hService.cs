using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.Metrics;

public interface IAssignmentMetrics24hService
{
    Task<IReadOnlyDictionary<string, AssignmentMetrics24hSummary>> GetSummariesAsync(
        IReadOnlyCollection<string> assignmentIds,
        CancellationToken cancellationToken);

    Task RefreshAssignmentAsync(string assignmentId, CancellationToken cancellationToken);

    Task RefreshAssignmentsAsync(IReadOnlyCollection<string> assignmentIds, CancellationToken cancellationToken);

    Task ApplyCheckResultsBatchAsync(IReadOnlyCollection<CheckResult> checkResults, CancellationToken cancellationToken);

    Task ApplyStateEvaluationAsync(
        string assignmentId,
        EndpointStateKind previousState,
        EndpointStateKind currentState,
        DateTimeOffset? transitionAtUtc,
        DateTimeOffset stateChangedAtUtc,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken);

    Task RebuildAllAsync(CancellationToken cancellationToken);
}

public sealed class AssignmentMetrics24hSummary
{
    public DateTimeOffset WindowStartUtc { get; init; }
    public DateTimeOffset WindowEndUtc { get; init; }
    public long UptimeSeconds { get; init; }
    public long DowntimeSeconds { get; init; }
    public long UnknownSeconds { get; init; }
    public long SuppressedSeconds { get; init; }
    public double? UptimePercent { get; init; }
    public int? LastRttMs { get; init; }
    public int? HighestRttMs { get; init; }
    public int? LowestRttMs { get; init; }
    public double? AverageRttMs { get; init; }
    public double? JitterMs { get; init; }
    public DateTimeOffset? LastSuccessfulCheckUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}
