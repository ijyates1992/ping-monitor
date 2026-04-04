using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.Diagnostics;

namespace PingMonitor.Web.Services.Metrics;

internal sealed class RollingAssignmentWindowStore : IRollingAssignmentWindowStore
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);
    private readonly ConcurrentDictionary<string, AssignmentWindow> _windows = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LatestAssignmentResultContext> _latestResults = new(StringComparer.Ordinal);
    private readonly IServiceScopeFactory _scopeFactory;

    public RollingAssignmentWindowStore(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task ApplyCheckResultsBatchAsync(IReadOnlyCollection<CheckResult> checkResults, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        foreach (var checkResult in checkResults)
        {
            TryUpdateLatestResult(checkResult);
        }

        var successfulByAssignment = checkResults
            .Where(x => x.Success && x.RoundTripMs.HasValue && !string.IsNullOrWhiteSpace(x.AssignmentId))
            .GroupBy(x => x.AssignmentId.Trim(), StringComparer.Ordinal);

        foreach (var group in successfulByAssignment)
        {
            var window = await GetWindowAsync(group.Key, nowUtc, cancellationToken);
            var ordered = group
                .Select(x => new RttSamplePoint(x.CheckedAtUtc, x.RoundTripMs!.Value))
                .OrderBy(x => x.CheckedAtUtc)
                .ToArray();

            await window.WithLockAsync(() =>
            {
                foreach (var sample in ordered)
                {
                    window.AppendRttSample(sample, nowUtc - Window);
                }
            }, cancellationToken);
        }
    }

    public async Task<LatestAssignmentResultContext?> GetLatestResultAsync(string assignmentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            return null;
        }

        var normalizedAssignmentId = assignmentId.Trim();
        if (_latestResults.TryGetValue(normalizedAssignmentId, out var cached))
        {
            return cached;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbActivityScope = scope.ServiceProvider.GetRequiredService<IDbActivityScope>();
        using var dbScope = dbActivityScope.BeginScope("RollingWindowHydration");
        var dbContext = scope.ServiceProvider.GetRequiredService<PingMonitorDbContext>();
        var latest = await dbContext.CheckResults.AsNoTracking()
            .Where(x => x.AssignmentId == normalizedAssignmentId)
            .OrderByDescending(x => x.CheckedAtUtc)
            .ThenByDescending(x => x.ReceivedAtUtc)
            .ThenByDescending(x => x.CheckResultId)
            .Select(x => new LatestAssignmentResultContext
            {
                AssignmentId = x.AssignmentId,
                CheckResultId = x.CheckResultId,
                CheckedAtUtc = x.CheckedAtUtc,
                ReceivedAtUtc = x.ReceivedAtUtc,
                Success = x.Success,
                RoundTripMs = x.RoundTripMs,
                ErrorCode = x.ErrorCode,
                ErrorMessage = x.ErrorMessage
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is not null)
        {
            _latestResults[normalizedAssignmentId] = latest;
        }

        return latest;
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
        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            return;
        }

        var normalizedAssignmentId = assignmentId.Trim();
        var window = await GetWindowAsync(normalizedAssignmentId, evaluatedAtUtc, cancellationToken);

        await window.WithLockAsync(() =>
        {
            if (transitionAtUtc.HasValue)
            {
                var previousStartedAtUtc = stateChangedAtUtc > DateTimeOffset.MinValue
                    ? stateChangedAtUtc
                    : transitionAtUtc.Value;

                window.EnsureState(previousState, previousStartedAtUtc);
                window.AppendStateTransition(currentState, transitionAtUtc.Value);
            }
            else
            {
                var currentStartedAtUtc = stateChangedAtUtc > DateTimeOffset.MinValue
                    ? stateChangedAtUtc
                    : evaluatedAtUtc;

                window.EnsureState(currentState, currentStartedAtUtc);
            }

            window.PruneStateWindow(evaluatedAtUtc - Window);
        }, cancellationToken);
    }

    public async Task<AssignmentWindowSnapshot> GetSnapshotAsync(string assignmentId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var window = await GetWindowAsync(assignmentId, nowUtc, cancellationToken);
        return await window.WithLockAsync(() => window.BuildSnapshot(nowUtc - Window, nowUtc), cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, AssignmentWindowSnapshot>> GetSnapshotsAsync(
        IReadOnlyCollection<string> assignmentIds,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, AssignmentWindowSnapshot>(StringComparer.Ordinal);

        foreach (var assignmentId in NormalizeAssignmentIds(assignmentIds))
        {
            result[assignmentId] = await GetSnapshotAsync(assignmentId, nowUtc, cancellationToken);
        }

        return result;
    }

    public async Task WarmAssignmentsAsync(IReadOnlyCollection<string> assignmentIds, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        foreach (var assignmentId in NormalizeAssignmentIds(assignmentIds))
        {
            _ = await GetWindowAsync(assignmentId, nowUtc, cancellationToken);
        }
    }

    private async Task<AssignmentWindow> GetWindowAsync(string assignmentId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var normalizedAssignmentId = assignmentId.Trim();
        var window = _windows.GetOrAdd(normalizedAssignmentId, id => new AssignmentWindow(id));

        if (window.IsHydrated)
        {
            return window;
        }

        await window.WithLockAsync(async () =>
        {
            if (window.IsHydrated)
            {
                return;
            }

            await HydrateWindowAsync(window, nowUtc, cancellationToken);
            window.IsHydrated = true;
        }, cancellationToken);

        return window;
    }

    private async Task HydrateWindowAsync(AssignmentWindow window, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var windowStartUtc = nowUtc - Window;

        using var scope = _scopeFactory.CreateScope();
        var dbActivityScope = scope.ServiceProvider.GetRequiredService<IDbActivityScope>();
        using var dbScope = dbActivityScope.BeginScope("RollingWindowHydration");
        var dbContext = scope.ServiceProvider.GetRequiredService<PingMonitorDbContext>();

        var samples = await dbContext.CheckResults.AsNoTracking()
            .Where(x => x.AssignmentId == window.AssignmentId
                && x.Success
                && x.RoundTripMs.HasValue
                && x.CheckedAtUtc >= windowStartUtc
                && x.CheckedAtUtc <= nowUtc)
            .OrderBy(x => x.CheckedAtUtc)
            .ThenBy(x => x.ReceivedAtUtc)
            .ThenBy(x => x.CheckResultId)
            .Select(x => new RttSamplePoint(x.CheckedAtUtc, x.RoundTripMs!.Value))
            .ToArrayAsync(cancellationToken);

        var transitionsInWindow = await dbContext.StateTransitions.AsNoTracking()
            .Where(x => x.AssignmentId == window.AssignmentId
                && x.TransitionAtUtc >= windowStartUtc
                && x.TransitionAtUtc <= nowUtc)
            .OrderBy(x => x.TransitionAtUtc)
            .Select(x => new StateTransitionPoint(x.TransitionAtUtc, x.NewState))
            .ToArrayAsync(cancellationToken);

        var latestBeforeWindow = await dbContext.StateTransitions.AsNoTracking()
            .Where(x => x.AssignmentId == window.AssignmentId
                && x.TransitionAtUtc < windowStartUtc)
            .OrderByDescending(x => x.TransitionAtUtc)
            .Select(x => new StateTransitionPoint(x.TransitionAtUtc, x.NewState))
            .Take(1)
            .ToArrayAsync(cancellationToken);

        var endpointState = await dbContext.EndpointStates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.AssignmentId == window.AssignmentId, cancellationToken);

        window.Reset();

        foreach (var sample in samples)
        {
            window.AppendRttSample(sample, windowStartUtc);
        }

        if (latestBeforeWindow.Length > 0)
        {
            window.AddHydratedTransition(latestBeforeWindow[0]);
        }
        else if (endpointState is not null)
        {
            var stateSinceUtc = endpointState.LastStateChangeUtc ?? windowStartUtc;
            window.AddHydratedTransition(new StateTransitionPoint(stateSinceUtc, endpointState.CurrentState));
        }

        foreach (var transition in transitionsInWindow)
        {
            window.AddHydratedTransition(transition);
        }

        window.PruneStateWindow(windowStartUtc);
    }

    private static string[] NormalizeAssignmentIds(IReadOnlyCollection<string> assignmentIds)
    {
        return assignmentIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private readonly record struct RttSamplePoint(DateTimeOffset CheckedAtUtc, int RttMs);
    private readonly record struct StateTransitionPoint(DateTimeOffset TransitionAtUtc, EndpointStateKind State);

    private void TryUpdateLatestResult(CheckResult checkResult)
    {
        if (string.IsNullOrWhiteSpace(checkResult.AssignmentId))
        {
            return;
        }

        var normalizedAssignmentId = checkResult.AssignmentId.Trim();
        var candidate = new LatestAssignmentResultContext
        {
            AssignmentId = normalizedAssignmentId,
            CheckResultId = checkResult.CheckResultId,
            CheckedAtUtc = checkResult.CheckedAtUtc,
            ReceivedAtUtc = checkResult.ReceivedAtUtc,
            Success = checkResult.Success,
            RoundTripMs = checkResult.RoundTripMs,
            ErrorCode = checkResult.ErrorCode,
            ErrorMessage = checkResult.ErrorMessage
        };

        _latestResults.AddOrUpdate(
            normalizedAssignmentId,
            candidate,
            (_, existing) => IsNewer(candidate, existing) ? candidate : existing);
    }

    private static bool IsNewer(LatestAssignmentResultContext candidate, LatestAssignmentResultContext existing)
    {
        if (candidate.CheckedAtUtc != existing.CheckedAtUtc)
        {
            return candidate.CheckedAtUtc > existing.CheckedAtUtc;
        }

        if (candidate.ReceivedAtUtc != existing.ReceivedAtUtc)
        {
            return candidate.ReceivedAtUtc > existing.ReceivedAtUtc;
        }

        return string.CompareOrdinal(candidate.CheckResultId, existing.CheckResultId) > 0;
    }

    private sealed class AssignmentWindow
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly LinkedList<RttSamplePoint> _rttSamples = [];
        private readonly LinkedList<StateTransitionPoint> _stateTransitions = [];
        private long _rttSum;
        private int? _rttMin;
        private int? _rttMax;
        private bool _rttMinDirty;
        private bool _rttMaxDirty;
        private double _rttDeltaSum;

        public AssignmentWindow(string assignmentId)
        {
            AssignmentId = assignmentId;
        }

        public string AssignmentId { get; }
        public bool IsHydrated { get; set; }

        public void Reset()
        {
            _rttSamples.Clear();
            _stateTransitions.Clear();
            _rttSum = 0;
            _rttMin = null;
            _rttMax = null;
            _rttMinDirty = false;
            _rttMaxDirty = false;
            _rttDeltaSum = 0;
        }

        public async Task WithLockAsync(Action action, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                action();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<T> WithLockAsync<T>(Func<T> action, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                return action();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task WithLockAsync(Func<Task> action, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                await action();
            }
            finally
            {
                _gate.Release();
            }
        }

        public void AppendRttSample(RttSamplePoint sample, DateTimeOffset windowStartUtc)
        {
            var tail = _rttSamples.Last?.Value;
            _rttSamples.AddLast(sample);
            _rttSum += sample.RttMs;

            _rttMin = !_rttMin.HasValue ? sample.RttMs : Math.Min(_rttMin.Value, sample.RttMs);
            _rttMax = !_rttMax.HasValue ? sample.RttMs : Math.Max(_rttMax.Value, sample.RttMs);

            if (tail.HasValue)
            {
                _rttDeltaSum += Math.Abs(sample.RttMs - tail.Value.RttMs);
            }

            PruneRttWindow(windowStartUtc);
        }

        public void AddHydratedTransition(StateTransitionPoint transition)
        {
            if (_stateTransitions.Last is not null && _stateTransitions.Last.Value.TransitionAtUtc == transition.TransitionAtUtc)
            {
                _stateTransitions.Last.Value = transition;
                return;
            }

            if (_stateTransitions.Last is not null && _stateTransitions.Last.Value.State == transition.State)
            {
                return;
            }

            _stateTransitions.AddLast(transition);
        }

        public void EnsureState(EndpointStateKind state, DateTimeOffset stateSinceUtc)
        {
            if (_stateTransitions.Count == 0)
            {
                _stateTransitions.AddLast(new StateTransitionPoint(stateSinceUtc, state));
                return;
            }

            if (_stateTransitions.Last!.Value.State != state)
            {
                _stateTransitions.AddLast(new StateTransitionPoint(stateSinceUtc, state));
            }
        }

        public void AppendStateTransition(EndpointStateKind newState, DateTimeOffset transitionAtUtc)
        {
            if (_stateTransitions.Last is not null && _stateTransitions.Last.Value.TransitionAtUtc == transitionAtUtc)
            {
                _stateTransitions.Last.Value = new StateTransitionPoint(transitionAtUtc, newState);
                return;
            }

            if (_stateTransitions.Last is not null && _stateTransitions.Last.Value.State == newState)
            {
                return;
            }

            _stateTransitions.AddLast(new StateTransitionPoint(transitionAtUtc, newState));
        }

        public void PruneStateWindow(DateTimeOffset windowStartUtc)
        {
            while (_stateTransitions.Count >= 2)
            {
                var oldest = _stateTransitions.First!;
                var second = oldest.Next!;
                if (second.Value.TransitionAtUtc >= windowStartUtc)
                {
                    break;
                }

                _stateTransitions.RemoveFirst();
            }
        }

        public AssignmentWindowSnapshot BuildSnapshot(DateTimeOffset windowStartUtc, DateTimeOffset nowUtc)
        {
            PruneRttWindow(windowStartUtc);
            PruneStateWindow(windowStartUtc);
            EnsureRttBounds();

            var rttCount = _rttSamples.Count;
            var lastRtt = _rttSamples.Last?.Value.RttMs;
            var lastSuccessUtc = _rttSamples.Last?.Value.CheckedAtUtc;
            var averageRtt = rttCount > 0 ? _rttSum / (double)rttCount : (double?)null;
            var jitter = rttCount >= 2 ? _rttDeltaSum / (rttCount - 1) : (double?)null;

            var durations = CalculateStateDurations(windowStartUtc, nowUtc);

            return new AssignmentWindowSnapshot
            {
                AssignmentId = AssignmentId,
                WindowStartUtc = windowStartUtc,
                WindowEndUtc = nowUtc,
                LastRttMs = lastRtt,
                HighestRttMs = _rttMax,
                LowestRttMs = _rttMin,
                AverageRttMs = averageRtt,
                JitterMs = jitter,
                LastSuccessfulCheckUtc = lastSuccessUtc,
                UpDurationSeconds24h = durations.UpSeconds,
                DownDurationSeconds24h = durations.DownSeconds,
                UnknownDurationSeconds24h = durations.UnknownSeconds,
                SuppressedDurationSeconds24h = durations.SuppressedSeconds,
                UpdatedUtc = nowUtc
            };
        }

        private void PruneRttWindow(DateTimeOffset windowStartUtc)
        {
            while (_rttSamples.First is not null && _rttSamples.First.Value.CheckedAtUtc < windowStartUtc)
            {
                var oldest = _rttSamples.First;
                var next = oldest!.Next;

                _rttSum -= oldest.Value.RttMs;

                if (_rttMin.HasValue && oldest.Value.RttMs == _rttMin.Value)
                {
                    _rttMinDirty = true;
                }

                if (_rttMax.HasValue && oldest.Value.RttMs == _rttMax.Value)
                {
                    _rttMaxDirty = true;
                }

                if (next is not null)
                {
                    _rttDeltaSum -= Math.Abs(next.Value.RttMs - oldest.Value.RttMs);
                }

                _rttSamples.RemoveFirst();
            }

            if (_rttSamples.Count == 0)
            {
                _rttSum = 0;
                _rttMin = null;
                _rttMax = null;
                _rttMinDirty = false;
                _rttMaxDirty = false;
                _rttDeltaSum = 0;
            }
        }

        private void EnsureRttBounds()
        {
            if (_rttSamples.Count == 0)
            {
                _rttMin = null;
                _rttMax = null;
                _rttMinDirty = false;
                _rttMaxDirty = false;
                return;
            }

            if (_rttMinDirty)
            {
                _rttMin = _rttSamples.Min(x => x.RttMs);
                _rttMinDirty = false;
            }

            if (_rttMaxDirty)
            {
                _rttMax = _rttSamples.Max(x => x.RttMs);
                _rttMaxDirty = false;
            }
        }

        private StateDurationSummary CalculateStateDurations(DateTimeOffset windowStartUtc, DateTimeOffset windowEndUtc)
        {
            if (windowEndUtc <= windowStartUtc)
            {
                return StateDurationSummary.Empty;
            }

            if (_stateTransitions.Count == 0)
            {
                var seconds = ToWholeSeconds(windowEndUtc - windowStartUtc);
                return new StateDurationSummary(0, 0, seconds, 0);
            }

            var ordered = _stateTransitions
                .Where(x => x.TransitionAtUtc <= windowEndUtc)
                .OrderBy(x => x.TransitionAtUtc)
                .ToArray();

            if (ordered.Length == 0)
            {
                var seconds = ToWholeSeconds(windowEndUtc - windowStartUtc);
                return new StateDurationSummary(0, 0, seconds, 0);
            }

            var currentState = EndpointStateKind.Unknown;
            var cursorUtc = windowStartUtc;

            var anchor = ordered.LastOrDefault(x => x.TransitionAtUtc <= windowStartUtc);
            if (anchor != default)
            {
                currentState = anchor.State;
            }

            var up = 0L;
            var down = 0L;
            var unknown = 0L;
            var suppressed = 0L;

            foreach (var transition in ordered)
            {
                if (transition.TransitionAtUtc <= windowStartUtc)
                {
                    continue;
                }

                if (transition.TransitionAtUtc > windowEndUtc)
                {
                    break;
                }

                AddDuration(currentState, transition.TransitionAtUtc - cursorUtc, ref up, ref down, ref unknown, ref suppressed);
                cursorUtc = transition.TransitionAtUtc;
                currentState = transition.State;
            }

            AddDuration(currentState, windowEndUtc - cursorUtc, ref up, ref down, ref unknown, ref suppressed);
            return new StateDurationSummary(up, down, unknown, suppressed);
        }

        private static void AddDuration(
            EndpointStateKind state,
            TimeSpan duration,
            ref long up,
            ref long down,
            ref long unknown,
            ref long suppressed)
        {
            var seconds = ToWholeSeconds(duration);
            if (seconds <= 0)
            {
                return;
            }

            switch (state)
            {
                case EndpointStateKind.Up:
                case EndpointStateKind.Degraded:
                    up += seconds;
                    break;
                case EndpointStateKind.Down:
                    down += seconds;
                    break;
                case EndpointStateKind.Suppressed:
                    suppressed += seconds;
                    break;
                default:
                    unknown += seconds;
                    break;
            }
        }

        private static long ToWholeSeconds(TimeSpan span)
        {
            return span <= TimeSpan.Zero ? 0 : (long)Math.Floor(span.TotalSeconds);
        }

        private readonly record struct StateDurationSummary(long UpSeconds, long DownSeconds, long UnknownSeconds, long SuppressedSeconds)
        {
            public static StateDurationSummary Empty => new(0, 0, 0, 0);
        }
    }
}
