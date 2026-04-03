using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.Metrics;

public interface IRollingAssignmentWindowStore
{
    Task ApplyCheckResultsBatchAsync(IReadOnlyCollection<CheckResult> checkResults, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task ApplyStateEvaluationAsync(
        string assignmentId,
        EndpointStateKind previousState,
        EndpointStateKind currentState,
        DateTimeOffset? transitionAtUtc,
        DateTimeOffset stateChangedAtUtc,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken);

    Task<AssignmentWindowSnapshot> GetSnapshotAsync(string assignmentId, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, AssignmentWindowSnapshot>> GetSnapshotsAsync(
        IReadOnlyCollection<string> assignmentIds,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task WarmAssignmentsAsync(IReadOnlyCollection<string> assignmentIds, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}

public sealed class AssignmentWindowSnapshot
{
    public required string AssignmentId { get; init; }
    public required DateTimeOffset WindowStartUtc { get; init; }
    public required DateTimeOffset WindowEndUtc { get; init; }
    public required int? LastRttMs { get; init; }
    public required int? HighestRttMs { get; init; }
    public required int? LowestRttMs { get; init; }
    public required double? AverageRttMs { get; init; }
    public required double? JitterMs { get; init; }
    public required DateTimeOffset? LastSuccessfulCheckUtc { get; init; }
    public required long UpDurationSeconds24h { get; init; }
    public required long DownDurationSeconds24h { get; init; }
    public required long UnknownDurationSeconds24h { get; init; }
    public required long SuppressedDurationSeconds24h { get; init; }
    public required DateTimeOffset UpdatedUtc { get; init; }
}
