using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.Metrics;

internal sealed class EndpointMetricsService : IEndpointMetricsService
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);
    private readonly PingMonitorDbContext _dbContext;
    
    private sealed class RttSample
    {
        public required double RoundTripMs { get; init; }
        public required DateTimeOffset CheckedAtUtc { get; init; }
    }

    public EndpointMetricsService(PingMonitorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyDictionary<string, EndpointMetricSummary>> GetEndpointMetricSummariesAsync(
        IReadOnlyCollection<string> assignmentIds,
        CancellationToken cancellationToken)
    {
        if (assignmentIds.Count == 0)
        {
            return new Dictionary<string, EndpointMetricSummary>(StringComparer.Ordinal);
        }

        var now = DateTimeOffset.UtcNow;
        var windowStart = now - Window;
        var assignmentIdSet = assignmentIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var endpointStates = await _dbContext.EndpointStates
            .AsNoTracking()
            .Where(x => assignmentIdSet.Contains(x.AssignmentId))
            .ToDictionaryAsync(x => x.AssignmentId, cancellationToken);

        var transitionsInWindow = await _dbContext.StateTransitions
            .AsNoTracking()
            .Where(x => assignmentIdSet.Contains(x.AssignmentId) && x.TransitionAtUtc >= windowStart && x.TransitionAtUtc <= now)
            .OrderBy(x => x.TransitionAtUtc)
            .ToListAsync(cancellationToken);

        var transitionsBeforeWindow = await _dbContext.StateTransitions
            .AsNoTracking()
            .Where(x => assignmentIdSet.Contains(x.AssignmentId) && x.TransitionAtUtc < windowStart)
            .OrderBy(x => x.TransitionAtUtc)
            .ToListAsync(cancellationToken);

        var successfulResults = await _dbContext.CheckResults
            .AsNoTracking()
            .Where(x => assignmentIdSet.Contains(x.AssignmentId)
                        && x.CheckedAtUtc >= windowStart
                        && x.CheckedAtUtc <= now
                        && x.Success
                        && x.RoundTripMs.HasValue)
            .OrderBy(x => x.CheckedAtUtc)
            .Select(x => new { x.AssignmentId, RoundTripMs = (double)x.RoundTripMs!.Value, x.CheckedAtUtc })
            .ToListAsync(cancellationToken);

        var transitionsInWindowByAssignment = transitionsInWindow
            .GroupBy(x => x.AssignmentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(x => x.TransitionAtUtc).ToList(), StringComparer.Ordinal);

        var latestTransitionBeforeWindow = transitionsBeforeWindow
            .GroupBy(x => x.AssignmentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(x => x.TransitionAtUtc).First(), StringComparer.Ordinal);

        var resultsByAssignment = successfulResults
            .GroupBy(x => x.AssignmentId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<RttSample>)group
                    .OrderBy(x => x.CheckedAtUtc)
                    .Select(x => new RttSample { RoundTripMs = x.RoundTripMs, CheckedAtUtc = x.CheckedAtUtc })
                    .ToList(),
                StringComparer.Ordinal);

        var summaries = new Dictionary<string, EndpointMetricSummary>(StringComparer.Ordinal);
        foreach (var assignmentId in assignmentIdSet)
        {
            endpointStates.TryGetValue(assignmentId, out var endpointState);
            transitionsInWindowByAssignment.TryGetValue(assignmentId, out var stateTransitions);
            latestTransitionBeforeWindow.TryGetValue(assignmentId, out var stateBeforeWindow);
            resultsByAssignment.TryGetValue(assignmentId, out var rttResults);

            var uptimePercent = CalculateUptimePercent(endpointState, stateTransitions ?? [], stateBeforeWindow, windowStart, now);
            var rttSummary = CalculateRttSummary(rttResults ?? []);

            summaries[assignmentId] = new EndpointMetricSummary
            {
                UptimePercent = uptimePercent,
                LastRttMs = rttSummary.LastRttMs,
                HighestRttMs = rttSummary.HighestRttMs,
                LowestRttMs = rttSummary.LowestRttMs,
                AverageRttMs = rttSummary.AverageRttMs,
                JitterMs = rttSummary.JitterMs
            };
        }

        return summaries;
    }

    private static double? CalculateUptimePercent(
        EndpointState? endpointState,
        IReadOnlyList<StateTransition> transitionsInWindow,
        StateTransition? transitionBeforeWindow,
        DateTimeOffset windowStart,
        DateTimeOffset now)
    {
        if (endpointState is null)
        {
            return null;
        }

        var stateAtWindowStart = DetermineStateAtWindowStart(endpointState, transitionBeforeWindow, windowStart);

        var segmentStart = windowStart;
        var activeState = stateAtWindowStart;
        var upDuration = TimeSpan.Zero;

        foreach (var transition in transitionsInWindow)
        {
            var transitionAt = transition.TransitionAtUtc;
            if (transitionAt < windowStart || transitionAt > now)
            {
                continue;
            }

            if (IsUpState(activeState))
            {
                upDuration += transitionAt - segmentStart;
            }

            segmentStart = transitionAt;
            activeState = transition.NewState;
        }

        if (IsUpState(activeState) && now > segmentStart)
        {
            upDuration += now - segmentStart;
        }

        var totalDuration = now - windowStart;
        if (totalDuration <= TimeSpan.Zero)
        {
            return null;
        }

        return upDuration.TotalSeconds / totalDuration.TotalSeconds * 100d;
    }

    private static EndpointStateKind DetermineStateAtWindowStart(
        EndpointState endpointState,
        StateTransition? transitionBeforeWindow,
        DateTimeOffset windowStart)
    {
        if (endpointState.LastStateChangeUtc.HasValue && endpointState.LastStateChangeUtc.Value <= windowStart)
        {
            return endpointState.CurrentState;
        }

        if (transitionBeforeWindow is not null)
        {
            return transitionBeforeWindow.NewState;
        }

        return EndpointStateKind.Unknown;
    }

    private static bool IsUpState(EndpointStateKind state)
    {
        return state is EndpointStateKind.Up or EndpointStateKind.Degraded;
    }

    private static EndpointMetricSummary CalculateRttSummary(IReadOnlyList<RttSample> rttResults)
    {
        if (rttResults.Count == 0)
        {
            return new EndpointMetricSummary();
        }

        var values = rttResults.Select(x => x.RoundTripMs).ToArray();
        double? jitter = null;
        if (values.Length >= 2)
        {
            var deltas = new List<double>(values.Length - 1);
            for (var index = 1; index < values.Length; index++)
            {
                deltas.Add(Math.Abs(values[index] - values[index - 1]));
            }

            jitter = deltas.Average();
        }

        return new EndpointMetricSummary
        {
            LastRttMs = values.Last(),
            HighestRttMs = values.Max(),
            LowestRttMs = values.Min(),
            AverageRttMs = values.Average(),
            JitterMs = jitter
        };
    }
}
