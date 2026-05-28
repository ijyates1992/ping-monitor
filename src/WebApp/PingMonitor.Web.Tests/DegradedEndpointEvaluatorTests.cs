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
