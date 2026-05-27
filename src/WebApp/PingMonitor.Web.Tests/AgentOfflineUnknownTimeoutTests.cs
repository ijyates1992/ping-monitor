using PingMonitor.Web.Models;
using PingMonitor.Web.Services;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class AgentOfflineUnknownTimeoutTests
{
    [Fact]
    public void ShouldTransitionAssignmentsToUnknown_False_WhenBelowConfiguredDelay()
    {
        var now = DateTimeOffset.UtcNow;
        var agent = new Agent
        {
            AgentId = "a1",
            InstanceId = "i1",
            ApiKeyHash = "h",
            ApiKeyCreatedAtUtc = now,
            CreatedAtUtc = now,
            EndpointUnknownAfterAgentOfflineSeconds = 300,
            LastSeenUtc = now.AddSeconds(-299)
        };

        Assert.False(AgentStatusTransitionBackgroundService.ShouldTransitionAssignmentsToUnknown(agent, now));
    }

    [Fact]
    public void ShouldTransitionAssignmentsToUnknown_True_WhenBeyondConfiguredDelay()
    {
        var now = DateTimeOffset.UtcNow;
        var agent = new Agent
        {
            AgentId = "a1",
            InstanceId = "i1",
            ApiKeyHash = "h",
            ApiKeyCreatedAtUtc = now,
            CreatedAtUtc = now,
            EndpointUnknownAfterAgentOfflineSeconds = 300,
            LastSeenUtc = now.AddSeconds(-301)
        };

        Assert.True(AgentStatusTransitionBackgroundService.ShouldTransitionAssignmentsToUnknown(agent, now));
    }

    [Fact]
    public void ShouldTransitionAssignmentsToUnknown_UsesDefault_WhenConfiguredTimeoutInvalid()
    {
        var now = DateTimeOffset.UtcNow;
        var agent = new Agent
        {
            AgentId = "a1",
            InstanceId = "i1",
            ApiKeyHash = "h",
            ApiKeyCreatedAtUtc = now,
            CreatedAtUtc = now,
            EndpointUnknownAfterAgentOfflineSeconds = 0,
            LastSeenUtc = now.AddSeconds(-301)
        };

        Assert.True(AgentStatusTransitionBackgroundService.ShouldTransitionAssignmentsToUnknown(agent, now));
    }
}
