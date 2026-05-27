using PingMonitor.Web.Models;
using PingMonitor.Web.Services.State;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class AgentOfflineStateEvaluatorTests
{
    [Fact]
    public void OfflineBelowThreshold_DoesNotForceUnknown()
    {
        var now = DateTimeOffset.UtcNow;
        var shouldForceUnknown = AgentOfflineStateEvaluator.ShouldForceUnknown(true, false, AgentHealthStatus.Stale, now.AddSeconds(-120), 300, now);
        Assert.False(shouldForceUnknown);
    }

    [Fact]
    public void OfflineBeyondThreshold_ForcesUnknown()
    {
        var now = DateTimeOffset.UtcNow;
        var shouldForceUnknown = AgentOfflineStateEvaluator.ShouldForceUnknown(true, false, AgentHealthStatus.Offline, now.AddSeconds(-301), 300, now);
        Assert.True(shouldForceUnknown);
    }

    [Fact]
    public void MultipleAgents_OnlyStaleAgentWouldForceUnknown()
    {
        var now = DateTimeOffset.UtcNow;
        var stale = AgentOfflineStateEvaluator.ShouldForceUnknown(true, false, AgentHealthStatus.Offline, now.AddSeconds(-600), 300, now);
        var healthy = AgentOfflineStateEvaluator.ShouldForceUnknown(true, false, AgentHealthStatus.Online, now.AddSeconds(-600), 300, now);

        Assert.True(stale);
        Assert.False(healthy);
    }

    [Fact]
    public void PerAgentTimeoutOverride_IsApplied()
    {
        var now = DateTimeOffset.UtcNow;
        var shorter = AgentOfflineStateEvaluator.ShouldForceUnknown(true, false, AgentHealthStatus.Stale, now.AddSeconds(-65), 60, now);
        var longer = AgentOfflineStateEvaluator.ShouldForceUnknown(true, false, AgentHealthStatus.Stale, now.AddSeconds(-65), 600, now);

        Assert.True(shorter);
        Assert.False(longer);
    }
}
