using PingMonitor.Web.Models;
using PingMonitor.Web.Services;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class DegradedEndpointEvaluatorTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 05, 28, 12, 00, 00, TimeSpan.Zero);
    private static readonly DegradedEndpointEvaluationSettings DefaultSettings = new();

    [Fact]
    public void Evaluate_DoesNotDegrade_WhenRttAndLossAreWithinThresholds()
    {
        var results = BuildResults(baselineRtt: 100, currentRtt: 119, baselineFailures: 1, currentFailures: 2);

        var evaluation = DegradedEndpointEvaluator.Evaluate(results, NowUtc, DefaultSettings);

        Assert.False(evaluation.IsDegraded);
    }

    [Fact]
    public void Evaluate_Degrades_WhenRttIncreasesMoreThanTwentyPercent()
    {
        var results = BuildResults(baselineRtt: 50, currentRtt: 65, baselineFailures: 0, currentFailures: 0);

        var evaluation = DegradedEndpointEvaluator.Evaluate(results, NowUtc, DefaultSettings);

        Assert.True(evaluation.IsDegraded);
        Assert.True(evaluation.RttDegraded);
        Assert.Contains("RTT increased", evaluation.ReasonSummary);
    }

    [Fact]
    public void Evaluate_Degrades_WhenPacketLossIncreasesMoreThanTwentyPercentagePoints()
    {
        var results = BuildResults(baselineRtt: 50, currentRtt: 50, baselineFailures: 1, currentFailures: 23);

        var evaluation = DegradedEndpointEvaluator.Evaluate(results, NowUtc, DefaultSettings);

        Assert.True(evaluation.IsDegraded);
        Assert.True(evaluation.PacketLossDegraded);
        Assert.Contains("packet loss increased", evaluation.ReasonSummary);
    }

    [Fact]
    public void Evaluate_Degrades_WhenJitterIncreasesMoreThanTwentyPercent()
    {
        var results = BuildJitterResults(
            baselineSuccessfulRtts: AlternatingRtts(100, 100, 101),
            currentSuccessfulRtts: AlternatingRtts(100, 100, 102));

        var evaluation = DegradedEndpointEvaluator.Evaluate(results, NowUtc, DefaultSettings);

        Assert.True(evaluation.IsDegraded);
        Assert.True(evaluation.JitterDegraded);
        Assert.False(evaluation.RttDegraded);
        Assert.False(evaluation.PacketLossDegraded);
        Assert.Equal(1d, evaluation.BaselineJitterMs);
        Assert.Equal(2d, evaluation.CurrentJitterMs);
        Assert.Contains("jitter increased from 1.0 ms baseline to 2.0 ms current", evaluation.ReasonSummary);
    }

    [Fact]
    public void Evaluate_DoesNotDegrade_WhenJitterIsWithinThreshold()
    {
        var results = BuildJitterResults(
            baselineSuccessfulRtts: AlternatingRtts(100, 100, 110),
            currentSuccessfulRtts: AlternatingRtts(100, 100, 111));

        var evaluation = DegradedEndpointEvaluator.Evaluate(results, NowUtc, DefaultSettings);

        Assert.False(evaluation.IsDegraded);
        Assert.Equal(10d, evaluation.BaselineJitterMs);
        Assert.Equal(11d, evaluation.CurrentJitterMs);
    }

    [Fact]
    public void Evaluate_DoesNotDegrade_WhenJitterSamplesAreInsufficient()
    {
        var results = BuildJitterResults(
            baselineSuccessfulRtts: AlternatingRtts(9, 100, 101),
            currentSuccessfulRtts: AlternatingRtts(9, 90, 110));

        var evaluation = DegradedEndpointEvaluator.Evaluate(results, NowUtc, DefaultSettings);

        Assert.False(evaluation.IsDegraded);
        Assert.Null(evaluation.BaselineJitterMs);
        Assert.Null(evaluation.CurrentJitterMs);
        Assert.Equal(9, evaluation.BaselineJitterSampleCount);
        Assert.Equal(9, evaluation.CurrentJitterSampleCount);
    }

    [Fact]
    public void Evaluate_IncludesAllContributingDegradedReasons()
    {
        var results = BuildJitterResults(
            baselineSuccessfulRtts: AlternatingRtts(99, 50, 51),
            currentSuccessfulRtts: AlternatingRtts(77, 65, 67));

        var evaluation = DegradedEndpointEvaluator.Evaluate(results, NowUtc, DefaultSettings);

        Assert.True(evaluation.IsDegraded);
        Assert.True(evaluation.RttDegraded);
        Assert.True(evaluation.PacketLossDegraded);
        Assert.True(evaluation.JitterDegraded);
        Assert.Contains("RTT increased", evaluation.ReasonSummary);
        Assert.Contains("packet loss increased", evaluation.ReasonSummary);
        Assert.Contains("jitter increased", evaluation.ReasonSummary);
    }

    [Fact]
    public void Evaluate_DoesNotMarkDegraded_WhenDisabled()
    {
        var results = BuildResults(baselineRtt: 50, currentRtt: 80, baselineFailures: 0, currentFailures: 0);
        var settings = new DegradedEndpointEvaluationSettings { Enabled = false };

        var evaluation = DegradedEndpointEvaluator.Evaluate(results, NowUtc, settings);

        Assert.False(evaluation.IsDegraded);
    }

    [Fact]
    public void StatePriority_DownOverridesDegraded()
    {
        var next = DegradedEndpointStatePriority.Apply(
            EndpointStateKind.Down,
            new DegradedEndpointEvaluationResult { IsDegraded = true });

        Assert.Equal(EndpointStateKind.Down, next);
    }

    [Fact]
    public void StatePriority_UnknownOverridesDegraded()
    {
        var next = DegradedEndpointStatePriority.Apply(
            EndpointStateKind.Unknown,
            new DegradedEndpointEvaluationResult { IsDegraded = true });

        Assert.Equal(EndpointStateKind.Unknown, next);
    }

    [Fact]
    public void Evaluate_DoesNotDegrade_WhenBaselineSamplesAreInsufficient()
    {
        var results = BuildResults(baselineRtt: 50, currentRtt: 80, baselineFailures: 0, currentFailures: 0, baselineSamples: 9);

        var evaluation = DegradedEndpointEvaluator.Evaluate(results, NowUtc, DefaultSettings);

        Assert.False(evaluation.IsDegraded);
    }

    [Fact]
    public void Evaluate_DoesNotDegrade_WhenCurrentSamplesAreInsufficient()
    {
        var results = BuildResults(baselineRtt: 50, currentRtt: 80, baselineFailures: 0, currentFailures: 0, currentSamples: 9);

        var evaluation = DegradedEndpointEvaluator.Evaluate(results, NowUtc, DefaultSettings);

        Assert.False(evaluation.IsDegraded);
    }

    [Fact]
    public void Evaluate_ExcludesCurrentWindowFromBaseline()
    {
        var results = BuildResults(baselineRtt: 50, currentRtt: 65, baselineFailures: 0, currentFailures: 0);
        results.Add(NewResult(NowUtc.AddMinutes(-60), success: true, rttMs: 65));

        var evaluation = DegradedEndpointEvaluator.Evaluate(results, NowUtc, DefaultSettings);

        Assert.True(evaluation.IsDegraded);
        Assert.Equal(50, evaluation.BaselineAverageRttMs);
        Assert.True(evaluation.CurrentAverageRttMs >= 65);
    }

    [Fact]
    public void Evaluate_ClearsDegraded_WhenCurrentMetricsReturnWithinThreshold()
    {
        var results = BuildResults(baselineRtt: 50, currentRtt: 55, baselineFailures: 0, currentFailures: 0);

        var evaluation = DegradedEndpointEvaluator.Evaluate(results, NowUtc, DefaultSettings);

        Assert.False(evaluation.IsDegraded);
    }

    [Fact]
    public void Evaluate_ReportsSameDegradedDecisionWithoutCreatingRepeatedTransitionSignal()
    {
        var results = BuildResults(baselineRtt: 50, currentRtt: 65, baselineFailures: 0, currentFailures: 0);

        var first = DegradedEndpointEvaluator.Evaluate(results, NowUtc, DefaultSettings);
        var second = DegradedEndpointEvaluator.Evaluate(results, NowUtc, DefaultSettings);

        Assert.True(first.IsDegraded);
        Assert.True(second.IsDegraded);
        Assert.Equal(first.ReasonSummary, second.ReasonSummary);
    }

    private static List<CheckResult> BuildResults(
        int baselineRtt,
        int currentRtt,
        int baselineFailures,
        int currentFailures,
        int baselineSamples = 100,
        int currentSamples = 100)
    {
        var results = new List<CheckResult>(baselineSamples + currentSamples);
        var baselineStart = NowUtc.AddHours(-24);
        for (var i = 0; i < baselineSamples; i++)
        {
            var success = i >= baselineFailures;
            results.Add(NewResult(baselineStart.AddMinutes(i * 10), success, success ? baselineRtt : null));
        }

        var currentStart = NowUtc.AddHours(-1).AddMinutes(1);
        for (var i = 0; i < currentSamples; i++)
        {
            var success = i >= currentFailures;
            results.Add(NewResult(currentStart.AddSeconds(i * 30), success, success ? currentRtt : null));
        }

        return results;
    }

    private static List<CheckResult> BuildJitterResults(
        IReadOnlyList<int> baselineSuccessfulRtts,
        IReadOnlyList<int> currentSuccessfulRtts,
        int baselineSamples = 100,
        int currentSamples = 100)
    {
        var results = new List<CheckResult>(baselineSamples + currentSamples);
        var baselineStart = NowUtc.AddHours(-24);
        for (var i = 0; i < baselineSamples; i++)
        {
            var success = i < baselineSuccessfulRtts.Count;
            results.Add(NewResult(baselineStart.AddMinutes(i * 10), success, success ? baselineSuccessfulRtts[i] : null));
        }

        var currentStart = NowUtc.AddHours(-1).AddMinutes(1);
        for (var i = 0; i < currentSamples; i++)
        {
            var success = i < currentSuccessfulRtts.Count;
            results.Add(NewResult(currentStart.AddSeconds(i * 30), success, success ? currentSuccessfulRtts[i] : null));
        }

        return results;
    }

    private static int[] AlternatingRtts(int count, int first, int second)
    {
        var values = new int[count];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = index % 2 == 0 ? first : second;
        }

        return values;
    }

    private static CheckResult NewResult(DateTimeOffset checkedAtUtc, bool success, int? rttMs)
    {
        return new CheckResult
        {
            CheckResultId = Guid.NewGuid().ToString(),
            AssignmentId = "assignment-1",
            CheckedAtUtc = checkedAtUtc,
            Success = success,
            RoundTripMs = rttMs,
            ReceivedAtUtc = checkedAtUtc.AddSeconds(1),
            BatchId = "batch-1"
        };
    }
}
