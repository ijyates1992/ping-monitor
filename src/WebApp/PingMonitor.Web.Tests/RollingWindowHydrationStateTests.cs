using PingMonitor.Web.Services.Metrics;

using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class RollingWindowHydrationStateTests
{
    [Fact]
    public void GetSnapshot_ReportsStatusTimestampsAndFailureMessage()
    {
        var state = new RollingWindowHydrationState();
        var startedAtUtc = DateTimeOffset.Parse("2026-05-29T00:00:00Z");
        var failedAtUtc = DateTimeOffset.Parse("2026-05-29T00:01:00Z");

        state.MarkRunning(startedAtUtc);
        var running = state.GetSnapshot();
        state.MarkFailed(failedAtUtc, "hydrate timeout");
        var failed = state.GetSnapshot();

        Assert.Equal(RollingWindowHydrationStatus.Running, running.Status);
        Assert.Equal(startedAtUtc, running.StartedAtUtc);
        Assert.Null(running.CompletedAtUtc);
        Assert.Equal(RollingWindowHydrationStatus.Failed, failed.Status);
        Assert.Equal(startedAtUtc, failed.StartedAtUtc);
        Assert.Equal(failedAtUtc, failed.CompletedAtUtc);
        Assert.Equal("hydrate timeout", failed.FailureMessage);
    }
}
