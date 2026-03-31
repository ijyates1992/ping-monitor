using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.Metrics;

internal sealed class AssignmentMetrics24hService : IAssignmentMetrics24hService
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);
    private readonly PingMonitorDbContext _dbContext;

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

        var summaries = await _dbContext.AssignmentMetrics24h.AsNoTracking()
            .Where(x => normalizedAssignmentIds.Contains(x.AssignmentId))
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
                    LastSuccessfulCheckUtc = x.LastSuccessfulCheckUtc,
                    UpdatedAtUtc = x.UpdatedAtUtc
                },
                cancellationToken);

        return summaries;
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
        var windowStartUtc = nowUtc - Window;

        var existingRows = await _dbContext.AssignmentMetrics24h
            .Where(x => normalizedAssignmentIds.Contains(x.AssignmentId))
            .ToDictionaryAsync(x => x.AssignmentId, cancellationToken);

        var endpointStates = await _dbContext.EndpointStates.AsNoTracking()
            .Where(x => normalizedAssignmentIds.Contains(x.AssignmentId))
            .ToDictionaryAsync(x => x.AssignmentId, cancellationToken);

        var transitionsInWindow = await _dbContext.StateTransitions.AsNoTracking()
            .Where(x => normalizedAssignmentIds.Contains(x.AssignmentId)
                && x.TransitionAtUtc >= windowStartUtc
                && x.TransitionAtUtc <= nowUtc)
            .OrderBy(x => x.TransitionAtUtc)
            .ToListAsync(cancellationToken);

        var transitionsBeforeWindow = await _dbContext.StateTransitions.AsNoTracking()
            .Where(x => normalizedAssignmentIds.Contains(x.AssignmentId)
                && x.TransitionAtUtc < windowStartUtc)
            .GroupBy(x => x.AssignmentId)
            .Select(group => group.OrderByDescending(x => x.TransitionAtUtc).First())
            .ToListAsync(cancellationToken);

        var latestSuccessfulChecks = await _dbContext.CheckResults.AsNoTracking()
            .Where(x => normalizedAssignmentIds.Contains(x.AssignmentId)
                && x.Success
                && x.RoundTripMs.HasValue
                && x.CheckedAtUtc >= windowStartUtc
                && x.CheckedAtUtc <= nowUtc)
            .GroupBy(x => x.AssignmentId)
            .Select(group => group
                .OrderByDescending(x => x.CheckedAtUtc)
                .ThenByDescending(x => x.ReceivedAtUtc)
                .ThenByDescending(x => x.CheckResultId)
                .Select(x => new { x.AssignmentId, x.RoundTripMs, x.CheckedAtUtc })
                .First())
            .ToListAsync(cancellationToken);

        var transitionsInWindowByAssignment = transitionsInWindow
            .GroupBy(x => x.AssignmentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<StateTransition>)group.OrderBy(x => x.TransitionAtUtc).ToArray(), StringComparer.Ordinal);

        var transitionsBeforeWindowLookup = transitionsBeforeWindow
            .ToDictionary(x => x.AssignmentId, x => x, StringComparer.Ordinal);

        var latestSuccessfulCheckLookup = latestSuccessfulChecks
            .ToDictionary(
                x => x.AssignmentId,
                x => new { LastRttMs = (int?)x.RoundTripMs, LastSuccessfulCheckUtc = (DateTimeOffset?)x.CheckedAtUtc },
                StringComparer.Ordinal);

        foreach (var assignmentId in normalizedAssignmentIds)
        {
            endpointStates.TryGetValue(assignmentId, out var endpointState);
            transitionsInWindowByAssignment.TryGetValue(assignmentId, out var assignmentTransitions);
            transitionsBeforeWindowLookup.TryGetValue(assignmentId, out var transitionBeforeWindow);
            latestSuccessfulCheckLookup.TryGetValue(assignmentId, out var latestSuccessfulCheck);

            var durations = CalculateStateDurations(
                endpointState,
                assignmentTransitions ?? [],
                transitionBeforeWindow,
                windowStartUtc,
                nowUtc);

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
            row.UptimeSeconds = durations.UptimeSeconds;
            row.DowntimeSeconds = durations.DowntimeSeconds;
            row.UnknownSeconds = durations.UnknownSeconds;
            row.SuppressedSeconds = durations.SuppressedSeconds;
            row.LastRttMs = latestSuccessfulCheck?.LastRttMs;
            row.LastSuccessfulCheckUtc = latestSuccessfulCheck?.LastSuccessfulCheckUtc;
            row.UpdatedAtUtc = nowUtc;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RebuildAllAsync(CancellationToken cancellationToken)
    {
        var assignmentIds = await _dbContext.MonitorAssignments.AsNoTracking()
            .Select(x => x.AssignmentId)
            .ToArrayAsync(cancellationToken);

        await RefreshAssignmentsAsync(assignmentIds, cancellationToken);
    }

    private static AssignmentDurationSummary CalculateStateDurations(
        EndpointState? endpointState,
        IReadOnlyList<StateTransition> transitionsInWindow,
        StateTransition? transitionBeforeWindow,
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

        var activeState = DetermineStateAtWindowStart(endpointState, transitionBeforeWindow, windowStartUtc);
        var segmentStart = windowStartUtc;

        foreach (var transition in transitionsInWindow)
        {
            if (transition.TransitionAtUtc < windowStartUtc || transition.TransitionAtUtc > windowEndUtc)
            {
                continue;
            }

            if (transition.TransitionAtUtc > segmentStart)
            {
                durationsByState[activeState] += transition.TransitionAtUtc - segmentStart;
            }

            segmentStart = transition.TransitionAtUtc;
            activeState = transition.NewState;
        }

        if (windowEndUtc > segmentStart)
        {
            durationsByState[activeState] += windowEndUtc - segmentStart;
        }

        return new AssignmentDurationSummary(
            UptimeSeconds: ToWholeSeconds(durationsByState[EndpointStateKind.Up] + durationsByState[EndpointStateKind.Degraded]),
            DowntimeSeconds: ToWholeSeconds(durationsByState[EndpointStateKind.Down]),
            UnknownSeconds: ToWholeSeconds(durationsByState[EndpointStateKind.Unknown]),
            SuppressedSeconds: ToWholeSeconds(durationsByState[EndpointStateKind.Suppressed]));
    }

    private static EndpointStateKind DetermineStateAtWindowStart(
        EndpointState? endpointState,
        StateTransition? transitionBeforeWindow,
        DateTimeOffset windowStartUtc)
    {
        if (endpointState is not null && endpointState.LastStateChangeUtc.HasValue && endpointState.LastStateChangeUtc.Value <= windowStartUtc)
        {
            return endpointState.CurrentState;
        }

        if (transitionBeforeWindow is not null)
        {
            return transitionBeforeWindow.NewState;
        }

        return EndpointStateKind.Unknown;
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

    private readonly record struct AssignmentDurationSummary(
        long UptimeSeconds,
        long DowntimeSeconds,
        long UnknownSeconds,
        long SuppressedSeconds)
    {
        public static AssignmentDurationSummary Empty => new(0, 0, 0, 0);
    }
}
