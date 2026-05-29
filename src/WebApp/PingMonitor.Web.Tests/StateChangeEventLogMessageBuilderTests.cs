using PingMonitor.Web.Models;
using PingMonitor.Web.Services;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class StateChangeEventLogMessageBuilderTests
{
    [Fact]
    public void Build_IncludesJitterReason_WhenEnteringDegraded()
    {
        var message = StateChangeEventLogMessageBuilder.Build(
            "Core switch",
            EndpointStateKind.Up,
            EndpointStateKind.Degraded,
            new DateTimeOffset(2026, 05, 28, 12, 00, 00, TimeSpan.Zero),
            new DateTimeOffset(2026, 05, 28, 11, 00, 00, TimeSpan.Zero),
            new DegradedEndpointEvaluationResult
            {
                IsDegraded = true,
                ReasonSummary = "jitter increased from 0.3 ms baseline to 1.2 ms current",
                JitterDegraded = true,
                BaselineJitterMs = 0.3d,
                CurrentJitterMs = 1.2d
            });

        Assert.Contains("Endpoint \"Core switch\" is degraded", message);
        Assert.Contains("jitter increased from 0.3 ms baseline to 1.2 ms current", message);
    }
}
