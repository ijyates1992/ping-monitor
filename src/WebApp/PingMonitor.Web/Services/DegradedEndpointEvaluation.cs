using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services;

internal sealed class DegradedEndpointEvaluationSettings
{
    public bool Enabled { get; init; } = true;
    public int BaselineLookbackMinutes { get; init; } = 1440;
    public int CurrentWindowMinutes { get; init; } = 60;
    public double PacketLossIncreasePercentagePoints { get; init; } = 20d;
    public double RttIncreasePercent { get; init; } = 20d;
    public double JitterIncreasePercent { get; init; } = 20d;
    public int MinimumSamples { get; init; } = 10;
}

internal sealed class DegradedEndpointEvaluationResult
{
    public bool IsDegraded { get; init; }
    public string? ReasonSummary { get; init; }
    public int BaselineSampleCount { get; init; }
    public int CurrentSampleCount { get; init; }
    public double BaselinePacketLossPercent { get; init; }
    public double CurrentPacketLossPercent { get; init; }
    public double? BaselineAverageRttMs { get; init; }
    public double? CurrentAverageRttMs { get; init; }
    public double? BaselineJitterMs { get; init; }
    public double? CurrentJitterMs { get; init; }
    public int BaselineJitterSampleCount { get; init; }
    public int CurrentJitterSampleCount { get; init; }
    public bool PacketLossDegraded { get; init; }
    public bool RttDegraded { get; init; }
    public bool JitterDegraded { get; init; }

    public static DegradedEndpointEvaluationResult NotDegraded { get; } = new();
}

internal static class DegradedEndpointEvaluator
{
    public static DegradedEndpointEvaluationResult Evaluate(
        IReadOnlyCollection<CheckResult> results,
        DateTimeOffset nowUtc,
        DegradedEndpointEvaluationSettings settings)
    {
        if (!settings.Enabled)
        {
            return DegradedEndpointEvaluationResult.NotDegraded;
        }

        if (settings.BaselineLookbackMinutes <= settings.CurrentWindowMinutes
            || settings.CurrentWindowMinutes < 1
            || settings.MinimumSamples < 1)
        {
            return DegradedEndpointEvaluationResult.NotDegraded;
        }

        var currentWindowStartUtc = nowUtc.AddMinutes(-settings.CurrentWindowMinutes);
        var baselineWindowStartUtc = nowUtc.AddMinutes(-settings.BaselineLookbackMinutes);

        var baselineResults = results
            .Where(x => x.CheckedAtUtc >= baselineWindowStartUtc && x.CheckedAtUtc < currentWindowStartUtc)
            .ToArray();
        var currentResults = results
            .Where(x => x.CheckedAtUtc >= currentWindowStartUtc && x.CheckedAtUtc <= nowUtc)
            .ToArray();

        if (baselineResults.Length < settings.MinimumSamples || currentResults.Length < settings.MinimumSamples)
        {
            return DegradedEndpointEvaluationResult.NotDegraded;
        }

        var baselineLoss = CalculatePacketLossPercent(baselineResults);
        var currentLoss = CalculatePacketLossPercent(currentResults);
        var baselineRtt = CalculateAverageSuccessfulRttMs(baselineResults);
        var currentRtt = CalculateAverageSuccessfulRttMs(currentResults);
        var baselineJitter = CalculateAverageSuccessfulJitterMs(baselineResults, settings.MinimumSamples);
        var currentJitter = CalculateAverageSuccessfulJitterMs(currentResults, settings.MinimumSamples);

        var lossDegraded = currentLoss >= baselineLoss + settings.PacketLossIncreasePercentagePoints;
        var rttDegraded = baselineRtt.HasValue
            && baselineRtt.Value > 0d
            && currentRtt.HasValue
            && currentRtt.Value >= baselineRtt.Value * (1d + (settings.RttIncreasePercent / 100d));
        var jitterDegraded = baselineJitter.JitterMs.HasValue
            && baselineJitter.JitterMs.Value > 0d
            && currentJitter.JitterMs.HasValue
            && currentJitter.JitterMs.Value >= baselineJitter.JitterMs.Value * (1d + (settings.JitterIncreasePercent / 100d));

        if (!lossDegraded && !rttDegraded && !jitterDegraded)
        {
            return new DegradedEndpointEvaluationResult
            {
                BaselineSampleCount = baselineResults.Length,
                CurrentSampleCount = currentResults.Length,
                BaselinePacketLossPercent = baselineLoss,
                CurrentPacketLossPercent = currentLoss,
                BaselineAverageRttMs = baselineRtt,
                CurrentAverageRttMs = currentRtt,
                BaselineJitterMs = baselineJitter.JitterMs,
                CurrentJitterMs = currentJitter.JitterMs,
                BaselineJitterSampleCount = baselineJitter.SampleCount,
                CurrentJitterSampleCount = currentJitter.SampleCount
            };
        }

        var reasons = new List<string>(capacity: 3);
        if (rttDegraded)
        {
            reasons.Add($"RTT increased from {baselineRtt!.Value:F1} ms baseline to {currentRtt!.Value:F1} ms current");
        }

        if (lossDegraded)
        {
            reasons.Add($"packet loss increased from {baselineLoss:F1}% baseline to {currentLoss:F1}% current");
        }

        if (jitterDegraded)
        {
            reasons.Add($"jitter increased from {baselineJitter.JitterMs!.Value:F1} ms baseline to {currentJitter.JitterMs!.Value:F1} ms current");
        }

        return new DegradedEndpointEvaluationResult
        {
            IsDegraded = true,
            ReasonSummary = string.Join("; ", reasons),
            BaselineSampleCount = baselineResults.Length,
            CurrentSampleCount = currentResults.Length,
            BaselinePacketLossPercent = baselineLoss,
            CurrentPacketLossPercent = currentLoss,
            BaselineAverageRttMs = baselineRtt,
            CurrentAverageRttMs = currentRtt,
            BaselineJitterMs = baselineJitter.JitterMs,
            CurrentJitterMs = currentJitter.JitterMs,
            BaselineJitterSampleCount = baselineJitter.SampleCount,
            CurrentJitterSampleCount = currentJitter.SampleCount,
            PacketLossDegraded = lossDegraded,
            RttDegraded = rttDegraded,
            JitterDegraded = jitterDegraded
        };
    }

    private static double CalculatePacketLossPercent(IReadOnlyCollection<CheckResult> results)
    {
        if (results.Count == 0)
        {
            return 0d;
        }

        var failed = results.Count(static x => !x.Success);
        return failed * 100d / results.Count;
    }

    private static double? CalculateAverageSuccessfulRttMs(IReadOnlyCollection<CheckResult> results)
    {
        var successfulRtts = results
            .Where(static x => x.Success && x.RoundTripMs.HasValue)
            .Select(static x => x.RoundTripMs!.Value)
            .ToArray();

        return successfulRtts.Length == 0 ? null : (double)successfulRtts.Average();
    }

    private static JitterEvaluationWindow CalculateAverageSuccessfulJitterMs(
        IReadOnlyCollection<CheckResult> results,
        int minimumSamples)
    {
        var successfulRttSamples = results
            .Where(static x => x.Success && x.RoundTripMs.HasValue)
            .OrderBy(static x => x.CheckedAtUtc)
            .Select(static x => new RttJitterSample(x.CheckedAtUtc, (double)x.RoundTripMs!.Value))
            .ToArray();

        if (successfulRttSamples.Length < Math.Max(2, minimumSamples))
        {
            return new JitterEvaluationWindow(null, successfulRttSamples.Length);
        }

        return new JitterEvaluationWindow(
            RttJitterCalculator.CalculateAverageAbsoluteDeltaMs(successfulRttSamples),
            successfulRttSamples.Length);
    }

    private readonly record struct JitterEvaluationWindow(double? JitterMs, int SampleCount);
}

internal static class DegradedEndpointStatePriority
{
    public static EndpointStateKind Apply(EndpointStateKind baseState, DegradedEndpointEvaluationResult degradedEvaluation)
    {
        return baseState == EndpointStateKind.Up && degradedEvaluation.IsDegraded
            ? EndpointStateKind.Degraded
            : baseState;
    }
}
