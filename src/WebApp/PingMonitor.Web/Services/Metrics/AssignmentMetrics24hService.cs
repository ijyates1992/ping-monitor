using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.Metrics;

internal sealed class AssignmentMetrics24hService : IAssignmentMetrics24hService
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);
    private static readonly TimeSpan PruneRetention = TimeSpan.FromHours(48);
    private readonly PingMonitorDbContext _dbContext;

    private sealed class RttSample
    {
        public required int RoundTripMs { get; init; }
        public required DateTimeOffset CheckedAtUtc { get; init; }
        public required DateTimeOffset ReceivedAtUtc { get; init; }
        public required string CheckResultId { get; init; }
    }

    public AssignmentMetrics24hService(PingMonitorDbContext dbContext)
    {
        _dbContext = dbContext;
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
        var successfulResults = checkResults
            .Where(x => x.Success && x.RoundTripMs.HasValue)
            .Select(x => new
            {
                x.AssignmentId,
                MinuteStartUtc = TruncateToMinute(x.CheckedAtUtc)
            })
            .Distinct()
            .ToArray();

        if (successfulResults.Length == 0)
        {
            return;
        }

        foreach (var assignmentMinute in successfulResults)
        {
            await RebuildRttBucketAsync(assignmentMinute.AssignmentId, assignmentMinute.MinuteStartUtc, cancellationToken);
        }

        var assignmentIds = successfulResults
            .Select(x => x.AssignmentId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        await PruneSupportDataAsync(assignmentIds, DateTimeOffset.UtcNow, cancellationToken);
        await RecomputeSummariesFromSupportAsync(assignmentIds, DateTimeOffset.UtcNow, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
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
        var activeInterval = await _dbContext.AssignmentStateIntervals
            .Where(x => x.AssignmentId == assignmentId && x.EndedAtUtc == null)
            .OrderByDescending(x => x.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (transitionAtUtc.HasValue)
        {
            if (activeInterval is not null)
            {
                if (activeInterval.StartedAtUtc < transitionAtUtc.Value)
                {
                    activeInterval.EndedAtUtc = transitionAtUtc.Value;
                }
                else
                {
                    activeInterval.EndedAtUtc = activeInterval.StartedAtUtc;
                }

                activeInterval.UpdatedAtUtc = evaluatedAtUtc;
            }
            else
            {
                var previousStateStartedAtUtc = stateChangedAtUtc > DateTimeOffset.MinValue
                    ? stateChangedAtUtc
                    : transitionAtUtc.Value;

                _dbContext.AssignmentStateIntervals.Add(new AssignmentStateInterval
                {
                    AssignmentStateIntervalId = Guid.NewGuid().ToString(),
                    AssignmentId = assignmentId,
                    State = previousState,
                    StartedAtUtc = previousStateStartedAtUtc <= transitionAtUtc.Value
                        ? previousStateStartedAtUtc
                        : transitionAtUtc.Value,
                    EndedAtUtc = transitionAtUtc.Value,
                    UpdatedAtUtc = evaluatedAtUtc
                });
            }

            _dbContext.AssignmentStateIntervals.Add(new AssignmentStateInterval
            {
                AssignmentStateIntervalId = Guid.NewGuid().ToString(),
                AssignmentId = assignmentId,
                State = currentState,
                StartedAtUtc = transitionAtUtc.Value,
                EndedAtUtc = null,
                UpdatedAtUtc = evaluatedAtUtc
            });
        }
        else
        {
            var targetStartUtc = stateChangedAtUtc > DateTimeOffset.MinValue
                ? stateChangedAtUtc
                : evaluatedAtUtc;

            if (activeInterval is null)
            {
                _dbContext.AssignmentStateIntervals.Add(new AssignmentStateInterval
                {
                    AssignmentStateIntervalId = Guid.NewGuid().ToString(),
                    AssignmentId = assignmentId,
                    State = currentState,
                    StartedAtUtc = targetStartUtc,
                    EndedAtUtc = null,
                    UpdatedAtUtc = evaluatedAtUtc
                });
            }
            else if (activeInterval.State != currentState)
            {
                activeInterval.EndedAtUtc = evaluatedAtUtc;
                activeInterval.UpdatedAtUtc = evaluatedAtUtc;

                _dbContext.AssignmentStateIntervals.Add(new AssignmentStateInterval
                {
                    AssignmentStateIntervalId = Guid.NewGuid().ToString(),
                    AssignmentId = assignmentId,
                    State = currentState,
                    StartedAtUtc = targetStartUtc <= evaluatedAtUtc ? targetStartUtc : evaluatedAtUtc,
                    EndedAtUtc = null,
                    UpdatedAtUtc = evaluatedAtUtc
                });
            }
            else
            {
                activeInterval.UpdatedAtUtc = evaluatedAtUtc;
            }
        }

        await PruneSupportDataAsync([assignmentId], evaluatedAtUtc, cancellationToken);
        await RecomputeSummariesFromSupportAsync([assignmentId], evaluatedAtUtc, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RefreshAssignmentAsync(string assignmentId, CancellationToken cancellationToken)
    {
        await RefreshAssignmentsAsync([assignmentId], cancellationToken);
    }

    public async Task RefreshAssignmentsAsync(IReadOnlyCollection<string> assignmentIds, CancellationToken cancellationToken)
    {
        var normalizedAssignmentIds = NormalizeAssignmentIds(assignmentIds);
        if (normalizedAssignmentIds.Length == 0)
        {
            return;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        foreach (var assignmentId in normalizedAssignmentIds)
        {
            await RebuildRttBucketsFromRawAsync(assignmentId, nowUtc, cancellationToken);
            await RebuildStateIntervalsFromRawAsync(assignmentId, nowUtc, cancellationToken);
        }

        await RecomputeSummariesFromSupportAsync(normalizedAssignmentIds, nowUtc, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RebuildAllAsync(CancellationToken cancellationToken)
    {
        var assignmentIds = await _dbContext.MonitorAssignments.AsNoTracking()
            .Select(x => x.AssignmentId)
            .ToArrayAsync(cancellationToken);

        await RefreshAssignmentsAsync(assignmentIds, cancellationToken);
    }

    private async Task RebuildRttBucketAsync(string assignmentId, DateTimeOffset minuteStartUtc, CancellationToken cancellationToken)
    {
        var minuteEndUtc = minuteStartUtc.AddMinutes(1);
        var samples = await _dbContext.CheckResults.AsNoTracking()
            .Where(x => x.AssignmentId == assignmentId
                && x.Success
                && x.RoundTripMs.HasValue
                && x.CheckedAtUtc >= minuteStartUtc
                && x.CheckedAtUtc < minuteEndUtc)
            .OrderBy(x => x.CheckedAtUtc)
            .ThenBy(x => x.ReceivedAtUtc)
            .ThenBy(x => x.CheckResultId)
            .Select(x => new RttSample
            {
                RoundTripMs = x.RoundTripMs!.Value,
                CheckedAtUtc = x.CheckedAtUtc,
                ReceivedAtUtc = x.ReceivedAtUtc,
                CheckResultId = x.CheckResultId
            })
            .ToArrayAsync(cancellationToken);

        var existingBucket = await _dbContext.AssignmentRttMinuteBuckets
            .SingleOrDefaultAsync(x => x.AssignmentId == assignmentId && x.BucketStartUtc == minuteStartUtc, cancellationToken);

        if (samples.Length == 0)
        {
            if (existingBucket is not null)
            {
                _dbContext.AssignmentRttMinuteBuckets.Remove(existingBucket);
            }

            return;
        }

        var orderedRttValues = samples.Select(x => x.RoundTripMs).ToArray();
        var intraDeltaSum = 0d;
        for (var index = 1; index < orderedRttValues.Length; index++)
        {
            intraDeltaSum += Math.Abs(orderedRttValues[index] - orderedRttValues[index - 1]);
        }

        if (existingBucket is null)
        {
            existingBucket = new AssignmentRttMinuteBucket
            {
                AssignmentId = assignmentId,
                BucketStartUtc = minuteStartUtc
            };

            _dbContext.AssignmentRttMinuteBuckets.Add(existingBucket);
        }

        existingBucket.SampleCount = orderedRttValues.Length;
        existingBucket.SumRttMs = orderedRttValues.Sum(x => (long)x);
        existingBucket.MinRttMs = orderedRttValues.Min();
        existingBucket.MaxRttMs = orderedRttValues.Max();
        existingBucket.FirstRttMs = orderedRttValues[0];
        existingBucket.LastRttMs = orderedRttValues[^1];
        existingBucket.FirstSampleUtc = samples[0].CheckedAtUtc;
        existingBucket.LastSampleUtc = samples[^1].CheckedAtUtc;
        existingBucket.IntraBucketDeltaSumMs = intraDeltaSum;
        existingBucket.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private async Task RebuildRttBucketsFromRawAsync(string assignmentId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var pruneCutoffUtc = nowUtc - PruneRetention;

        var existingBuckets = await _dbContext.AssignmentRttMinuteBuckets
            .Where(x => x.AssignmentId == assignmentId)
            .ToArrayAsync(cancellationToken);

        if (existingBuckets.Length > 0)
        {
            _dbContext.AssignmentRttMinuteBuckets.RemoveRange(existingBuckets);
        }

        var windowSamples = await _dbContext.CheckResults.AsNoTracking()
            .Where(x => x.AssignmentId == assignmentId
                && x.Success
                && x.RoundTripMs.HasValue
                && x.CheckedAtUtc >= pruneCutoffUtc
                && x.CheckedAtUtc <= nowUtc)
            .Select(x => new
            {
                x.AssignmentId,
                MinuteStartUtc = TruncateToMinute(x.CheckedAtUtc)
            })
            .Distinct()
            .ToArrayAsync(cancellationToken);

        foreach (var sample in windowSamples)
        {
            await RebuildRttBucketAsync(sample.AssignmentId, sample.MinuteStartUtc, cancellationToken);
        }
    }

    private async Task RebuildStateIntervalsFromRawAsync(string assignmentId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var existingIntervals = await _dbContext.AssignmentStateIntervals
            .Where(x => x.AssignmentId == assignmentId)
            .ToArrayAsync(cancellationToken);

        if (existingIntervals.Length > 0)
        {
            _dbContext.AssignmentStateIntervals.RemoveRange(existingIntervals);
        }

        var transitions = await _dbContext.StateTransitions.AsNoTracking()
            .Where(x => x.AssignmentId == assignmentId)
            .OrderBy(x => x.TransitionAtUtc)
            .ToArrayAsync(cancellationToken);

        var endpointState = await _dbContext.EndpointStates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.AssignmentId == assignmentId, cancellationToken);

        if (transitions.Length == 0)
        {
            _dbContext.AssignmentStateIntervals.Add(new AssignmentStateInterval
            {
                AssignmentStateIntervalId = Guid.NewGuid().ToString(),
                AssignmentId = assignmentId,
                State = endpointState?.CurrentState ?? EndpointStateKind.Unknown,
                StartedAtUtc = endpointState?.LastStateChangeUtc ?? nowUtc,
                EndedAtUtc = null,
                UpdatedAtUtc = nowUtc
            });

            return;
        }

        for (var index = 0; index < transitions.Length; index++)
        {
            var transition = transitions[index];
            DateTimeOffset? endedAtUtc = null;
            if (index < transitions.Length - 1)
            {
                endedAtUtc = transitions[index + 1].TransitionAtUtc;
            }

            _dbContext.AssignmentStateIntervals.Add(new AssignmentStateInterval
            {
                AssignmentStateIntervalId = Guid.NewGuid().ToString(),
                AssignmentId = assignmentId,
                State = transition.NewState,
                StartedAtUtc = transition.TransitionAtUtc,
                EndedAtUtc = endedAtUtc,
                UpdatedAtUtc = nowUtc
            });
        }
    }

    private async Task RecomputeSummariesFromSupportAsync(
        IReadOnlyCollection<string> assignmentIds,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var normalizedAssignmentIds = NormalizeAssignmentIds(assignmentIds);
        if (normalizedAssignmentIds.Length == 0)
        {
            return;
        }

        var windowStartUtc = nowUtc - Window;

        var existingRows = await ApplyAssignmentFilter(
                _dbContext.AssignmentMetrics24h,
                x => x.AssignmentId,
                normalizedAssignmentIds)
            .ToDictionaryAsync(x => x.AssignmentId, cancellationToken);

        var rttBuckets = await ApplyAssignmentFilter(
                _dbContext.AssignmentRttMinuteBuckets.AsNoTracking(),
                x => x.AssignmentId,
                normalizedAssignmentIds)
            .Where(x => x.LastSampleUtc >= windowStartUtc && x.FirstSampleUtc <= nowUtc)
            .OrderBy(x => x.BucketStartUtc)
            .ToArrayAsync(cancellationToken);

        var intervals = await ApplyAssignmentFilter(
                _dbContext.AssignmentStateIntervals.AsNoTracking(),
                x => x.AssignmentId,
                normalizedAssignmentIds)
            .Where(x => x.StartedAtUtc <= nowUtc
                && (x.EndedAtUtc == null || x.EndedAtUtc >= windowStartUtc))
            .OrderBy(x => x.StartedAtUtc)
            .ToArrayAsync(cancellationToken);

        var rttByAssignment = rttBuckets
            .GroupBy(x => x.AssignmentId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);

        var intervalsByAssignment = intervals
            .GroupBy(x => x.AssignmentId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);

        foreach (var assignmentId in normalizedAssignmentIds)
        {
            rttByAssignment.TryGetValue(assignmentId, out var assignmentBuckets);
            intervalsByAssignment.TryGetValue(assignmentId, out var assignmentIntervals);

            var rttSummary = CalculateRttSummaryFromBuckets(assignmentBuckets ?? []);
            var durationSummary = CalculateDurationsFromIntervals(assignmentIntervals ?? [], windowStartUtc, nowUtc);

            if (!existingRows.TryGetValue(assignmentId, out var row))
            {
                row = new AssignmentMetrics24h
                {
                    AssignmentId = assignmentId
                };

                _dbContext.AssignmentMetrics24h.Add(row);
            }

            row.WindowStartUtc = windowStartUtc;
            row.WindowEndUtc = nowUtc;
            row.UptimeSeconds = durationSummary.UptimeSeconds;
            row.DowntimeSeconds = durationSummary.DowntimeSeconds;
            row.UnknownSeconds = durationSummary.UnknownSeconds;
            row.SuppressedSeconds = durationSummary.SuppressedSeconds;
            row.LastRttMs = rttSummary.LastRttMs;
            row.HighestRttMs = rttSummary.HighestRttMs;
            row.LowestRttMs = rttSummary.LowestRttMs;
            row.AverageRttMs = rttSummary.AverageRttMs;
            row.JitterMs = rttSummary.JitterMs;
            row.LastSuccessfulCheckUtc = rttSummary.LastSuccessfulCheckUtc;
            row.UpdatedAtUtc = nowUtc;
        }
    }

    private static AssignmentDurationSummary CalculateDurationsFromIntervals(
        IReadOnlyList<AssignmentStateInterval> intervals,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc)
    {
        if (windowEndUtc <= windowStartUtc)
        {
            return AssignmentDurationSummary.Empty;
        }

        var durationsByState = new Dictionary<EndpointStateKind, TimeSpan>
        {
            [EndpointStateKind.Up] = TimeSpan.Zero,
            [EndpointStateKind.Degraded] = TimeSpan.Zero,
            [EndpointStateKind.Down] = TimeSpan.Zero,
            [EndpointStateKind.Unknown] = TimeSpan.Zero,
            [EndpointStateKind.Suppressed] = TimeSpan.Zero
        };

        foreach (var interval in intervals)
        {
            var effectiveStartUtc = interval.StartedAtUtc < windowStartUtc ? windowStartUtc : interval.StartedAtUtc;
            var intervalEndUtc = interval.EndedAtUtc ?? windowEndUtc;
            var effectiveEndUtc = intervalEndUtc > windowEndUtc ? windowEndUtc : intervalEndUtc;
            if (effectiveEndUtc <= effectiveStartUtc)
            {
                continue;
            }

            durationsByState[interval.State] += effectiveEndUtc - effectiveStartUtc;
        }

        return new AssignmentDurationSummary(
            UptimeSeconds: ToWholeSeconds(durationsByState[EndpointStateKind.Up] + durationsByState[EndpointStateKind.Degraded]),
            DowntimeSeconds: ToWholeSeconds(durationsByState[EndpointStateKind.Down]),
            UnknownSeconds: ToWholeSeconds(durationsByState[EndpointStateKind.Unknown]),
            SuppressedSeconds: ToWholeSeconds(durationsByState[EndpointStateKind.Suppressed]));
    }

    private static AssignmentRttSummary CalculateRttSummaryFromBuckets(IReadOnlyList<AssignmentRttMinuteBucket> buckets)
    {
        if (buckets.Count == 0)
        {
            return AssignmentRttSummary.Empty;
        }

        var ordered = buckets
            .OrderBy(x => x.BucketStartUtc)
            .ThenBy(x => x.LastSampleUtc)
            .ToArray();

        var totalSamples = 0;
        long totalRttMs = 0;
        int? minRttMs = null;
        int? maxRttMs = null;
        double deltaSumMs = 0d;

        for (var index = 0; index < ordered.Length; index++)
        {
            var bucket = ordered[index];
            totalSamples += bucket.SampleCount;
            totalRttMs += bucket.SumRttMs;
            minRttMs = !minRttMs.HasValue ? bucket.MinRttMs : Math.Min(minRttMs.Value, bucket.MinRttMs);
            maxRttMs = !maxRttMs.HasValue ? bucket.MaxRttMs : Math.Max(maxRttMs.Value, bucket.MaxRttMs);
            deltaSumMs += bucket.IntraBucketDeltaSumMs;

            if (index > 0)
            {
                var previous = ordered[index - 1];
                deltaSumMs += Math.Abs(bucket.FirstRttMs - previous.LastRttMs);
            }
        }

        var lastBucket = ordered[^1];
        var averageRtt = totalSamples > 0 ? totalRttMs / (double)totalSamples : (double?)null;
        var jitterMs = totalSamples >= 2 ? deltaSumMs / (totalSamples - 1) : (double?)null;

        return new AssignmentRttSummary(
            LastRttMs: lastBucket.LastRttMs,
            HighestRttMs: maxRttMs,
            LowestRttMs: minRttMs,
            AverageRttMs: averageRtt,
            JitterMs: jitterMs,
            LastSuccessfulCheckUtc: lastBucket.LastSampleUtc);
    }

    private async Task PruneSupportDataAsync(IReadOnlyCollection<string> assignmentIds, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var normalizedAssignmentIds = NormalizeAssignmentIds(assignmentIds);
        if (normalizedAssignmentIds.Length == 0)
        {
            return;
        }

        var pruneCutoffUtc = nowUtc - PruneRetention;

        var staleBuckets = await ApplyAssignmentFilter(
                _dbContext.AssignmentRttMinuteBuckets,
                x => x.AssignmentId,
                normalizedAssignmentIds)
            .Where(x => x.BucketStartUtc < pruneCutoffUtc)
            .ToArrayAsync(cancellationToken);

        if (staleBuckets.Length > 0)
        {
            _dbContext.AssignmentRttMinuteBuckets.RemoveRange(staleBuckets);
        }

        var staleIntervals = await ApplyAssignmentFilter(
                _dbContext.AssignmentStateIntervals,
                x => x.AssignmentId,
                normalizedAssignmentIds)
            .Where(x => x.EndedAtUtc.HasValue && x.EndedAtUtc.Value < pruneCutoffUtc)
            .ToArrayAsync(cancellationToken);

        if (staleIntervals.Length > 0)
        {
            _dbContext.AssignmentStateIntervals.RemoveRange(staleIntervals);
        }
    }

    private static DateTimeOffset TruncateToMinute(DateTimeOffset value)
    {
        return new DateTimeOffset(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, TimeSpan.Zero);
    }

    private static long ToWholeSeconds(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return 0;
        }

        return (long)Math.Floor(duration.TotalSeconds);
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

    private readonly record struct AssignmentDurationSummary(
        long UptimeSeconds,
        long DowntimeSeconds,
        long UnknownSeconds,
        long SuppressedSeconds)
    {
        public static AssignmentDurationSummary Empty => new(0, 0, 0, 0);
    }

    private readonly record struct AssignmentRttSummary(
        int? LastRttMs,
        int? HighestRttMs,
        int? LowestRttMs,
        double? AverageRttMs,
        double? JitterMs,
        DateTimeOffset? LastSuccessfulCheckUtc)
    {
        public static AssignmentRttSummary Empty => new(null, null, null, null, null, null);
    }
}
