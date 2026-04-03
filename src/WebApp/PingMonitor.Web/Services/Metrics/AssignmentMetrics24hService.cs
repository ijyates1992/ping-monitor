using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.Metrics;

internal sealed class AssignmentMetrics24hService : IAssignmentMetrics24hService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly IRollingAssignmentWindowStore _rollingStore;

    public AssignmentMetrics24hService(PingMonitorDbContext dbContext, IRollingAssignmentWindowStore rollingStore)
    {
        _dbContext = dbContext;
        _rollingStore = rollingStore;
    }

    public async Task<IReadOnlyDictionary<string, AssignmentMetrics24hSummary>> GetSummariesAsync(
        IReadOnlyCollection<string> assignmentIds,
        CancellationToken cancellationToken)
    {
        var normalizedAssignmentIds = NormalizeAssignmentIds(assignmentIds);
        if (normalizedAssignmentIds.Length == 0)
        {
            return new Dictionary<string, AssignmentMetrics24hSummary>(StringComparer.Ordinal);
        }

        return await ApplyAssignmentFilter(
                _dbContext.AssignmentMetrics24h.AsNoTracking(),
                x => x.AssignmentId,
                normalizedAssignmentIds)
            .ToDictionaryAsync(
                x => x.AssignmentId,
                x => new AssignmentMetrics24hSummary
                {
                    WindowStartUtc = x.WindowStartUtc,
                    WindowEndUtc = x.WindowEndUtc,
                    UptimeSeconds = x.UptimeSeconds,
                    DowntimeSeconds = x.DowntimeSeconds,
                    UnknownSeconds = x.UnknownSeconds,
                    SuppressedSeconds = x.SuppressedSeconds,
                    UptimePercent = ComputeUptimePercent(x.UptimeSeconds, x.WindowStartUtc, x.WindowEndUtc),
                    LastRttMs = x.LastRttMs,
                    HighestRttMs = x.HighestRttMs,
                    LowestRttMs = x.LowestRttMs,
                    AverageRttMs = x.AverageRttMs,
                    JitterMs = x.JitterMs,
                    LastSuccessfulCheckUtc = x.LastSuccessfulCheckUtc,
                    UpdatedAtUtc = x.UpdatedAtUtc
                },
                cancellationToken);
    }

    public async Task ApplyCheckResultsBatchAsync(IReadOnlyCollection<CheckResult> checkResults, CancellationToken cancellationToken)
    {
        var assignmentIds = checkResults
            .Select(x => x.AssignmentId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (assignmentIds.Length == 0)
        {
            return;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        await _rollingStore.ApplyCheckResultsBatchAsync(checkResults, nowUtc, cancellationToken);
        await UpsertSnapshotsAsync(assignmentIds, nowUtc, cancellationToken);
    }

    public async Task ApplyStateEvaluationAsync(
        string assignmentId,
        EndpointStateKind previousState,
        EndpointStateKind currentState,
        DateTimeOffset? transitionAtUtc,
        DateTimeOffset stateChangedAtUtc,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken)
    {
        await _rollingStore.ApplyStateEvaluationAsync(
            assignmentId,
            previousState,
            currentState,
            transitionAtUtc,
            stateChangedAtUtc,
            evaluatedAtUtc,
            cancellationToken);

        await UpsertSnapshotsAsync([assignmentId], evaluatedAtUtc, cancellationToken);
    }

    public Task RefreshAssignmentAsync(string assignmentId, CancellationToken cancellationToken)
    {
        return RefreshAssignmentsAsync([assignmentId], cancellationToken);
    }

    public async Task RefreshAssignmentsAsync(IReadOnlyCollection<string> assignmentIds, CancellationToken cancellationToken)
    {
        var normalizedAssignmentIds = NormalizeAssignmentIds(assignmentIds);
        if (normalizedAssignmentIds.Length == 0)
        {
            return;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        await _rollingStore.WarmAssignmentsAsync(normalizedAssignmentIds, nowUtc, cancellationToken);
        await UpsertSnapshotsAsync(normalizedAssignmentIds, nowUtc, cancellationToken);
    }

    public async Task RebuildAllAsync(CancellationToken cancellationToken)
    {
        var assignmentIds = await _dbContext.MonitorAssignments.AsNoTracking()
            .Select(x => x.AssignmentId)
            .ToArrayAsync(cancellationToken);

        await RefreshAssignmentsAsync(assignmentIds, cancellationToken);
    }

    private async Task UpsertSnapshotsAsync(IReadOnlyCollection<string> assignmentIds, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var normalizedAssignmentIds = NormalizeAssignmentIds(assignmentIds);
        if (normalizedAssignmentIds.Length == 0)
        {
            return;
        }

        var snapshots = await _rollingStore.GetSnapshotsAsync(normalizedAssignmentIds, nowUtc, cancellationToken);

        var rows = await ApplyAssignmentFilter(
                _dbContext.AssignmentMetrics24h,
                x => x.AssignmentId,
                normalizedAssignmentIds)
            .ToDictionaryAsync(x => x.AssignmentId, cancellationToken);

        foreach (var assignmentId in normalizedAssignmentIds)
        {
            if (!snapshots.TryGetValue(assignmentId, out var snapshot))
            {
                continue;
            }

            if (!rows.TryGetValue(assignmentId, out var row))
            {
                row = new AssignmentMetrics24h
                {
                    AssignmentId = assignmentId
                };

                _dbContext.AssignmentMetrics24h.Add(row);
            }

            row.WindowStartUtc = snapshot.WindowStartUtc;
            row.WindowEndUtc = snapshot.WindowEndUtc;
            row.UptimeSeconds = snapshot.UpDurationSeconds24h;
            row.DowntimeSeconds = snapshot.DownDurationSeconds24h;
            row.UnknownSeconds = snapshot.UnknownDurationSeconds24h;
            row.SuppressedSeconds = snapshot.SuppressedDurationSeconds24h;
            row.LastRttMs = snapshot.LastRttMs;
            row.HighestRttMs = snapshot.HighestRttMs;
            row.LowestRttMs = snapshot.LowestRttMs;
            row.AverageRttMs = snapshot.AverageRttMs;
            row.JitterMs = snapshot.JitterMs;
            row.LastSuccessfulCheckUtc = snapshot.LastSuccessfulCheckUtc;
            row.UpdatedAtUtc = snapshot.UpdatedUtc;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static double? ComputeUptimePercent(long uptimeSeconds, DateTimeOffset windowStartUtc, DateTimeOffset windowEndUtc)
    {
        var totalWindowSeconds = (long)Math.Floor((windowEndUtc - windowStartUtc).TotalSeconds);
        if (totalWindowSeconds <= 0)
        {
            return null;
        }

        return uptimeSeconds * 100d / totalWindowSeconds;
    }

    private static string[] NormalizeAssignmentIds(IReadOnlyCollection<string> assignmentIds)
    {
        return assignmentIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IQueryable<T> ApplyAssignmentFilter<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> assignmentIdSelector,
        IReadOnlyList<string> assignmentIds)
    {
        Expression? filterExpression = null;
        var parameter = assignmentIdSelector.Parameters[0];

        foreach (var assignmentId in assignmentIds)
        {
            var equalsExpression = Expression.Equal(
                assignmentIdSelector.Body,
                Expression.Constant(assignmentId));

            filterExpression = filterExpression is null
                ? equalsExpression
                : Expression.OrElse(filterExpression, equalsExpression);
        }

        if (filterExpression is null)
        {
            return query.Where(_ => false);
        }

        var predicate = Expression.Lambda<Func<T, bool>>(filterExpression, parameter);
        return query.Where(predicate);
    }
}
