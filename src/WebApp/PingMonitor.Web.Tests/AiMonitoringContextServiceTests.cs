using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.AiTools;
using PingMonitor.Web.Services.Diagnostics;
using PingMonitor.Web.Services.Identity;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class AiMonitoringContextServiceTests
{
    [Fact]
    public async Task HealthSummary_ReturnsStateCounts()
    {
        await using var dbContext = CreateDbContext();
        SeedAssignment(dbContext, "a-up", "e-up", "Core Switch", "10.0.0.2", "agent-1", EndpointStateKind.Up);
        SeedAssignment(dbContext, "a-degraded", "e-degraded", "Warehouse WAN", "10.0.1.1", "agent-1", EndpointStateKind.Degraded);
        SeedAssignment(dbContext, "a-down", "e-down", "Farm Router WAN", "1.2.3.4", "agent-1", EndpointStateKind.Down);
        SeedAssignment(dbContext, "a-suppressed", "e-suppressed", "Barn AP", "10.0.2.20", "agent-1", EndpointStateKind.Suppressed);
        SeedAssignment(dbContext, "a-unknown", "e-unknown", "Workshop Camera", "10.0.3.10", "agent-1", null);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, new FakeUserAccessScopeService(isAdmin: true));

        var result = await service.GetNetworkHealthSummaryAsync(User(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Summary);
        var summary = result.Summary;
        Assert.Equal(5, summary.VisibleEndpointCount);
        Assert.Equal(5, summary.VisibleAssignmentCount);
        Assert.Equal(1, summary.StateCounts.Up);
        Assert.Equal(1, summary.StateCounts.Degraded);
        Assert.Equal(1, summary.StateCounts.Down);
        Assert.Equal(1, summary.StateCounts.Suppressed);
        Assert.Equal(1, summary.StateCounts.Unknown);
        Assert.Equal("current_endpoint_state", summary.DataSource);
        Assert.Equal(AiNetworkHealthSummary.ToolName, summary.CapabilityName);
    }

    [Fact]
    public async Task HealthSummary_IncludesCurrentlyDownEndpoints()
    {
        await using var dbContext = CreateDbContext();
        var changedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-12);
        SeedAssignment(dbContext, "a-down", "e-down", "Farm Router WAN", "1.2.3.4", "agent-1", EndpointStateKind.Down, changedAtUtc);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, new FakeUserAccessScopeService(isAdmin: true));

        var result = await service.GetNetworkHealthSummaryAsync(User(), CancellationToken.None);

        var endpoint = Assert.Single(result.Summary!.DownEndpoints);
        Assert.Equal("e-down", endpoint.EndpointId);
        Assert.Equal("a-down", endpoint.AssignmentId);
        Assert.Equal("Farm Router WAN", endpoint.Name);
        Assert.Equal("1.2.3.4", endpoint.Target);
        Assert.Equal(EndpointStateKind.Down, endpoint.State);
        Assert.Equal(changedAtUtc, endpoint.LastChangedUtc);
    }

    [Fact]
    public async Task HealthSummary_RespectsUserVisibilityFiltering()
    {
        await using var dbContext = CreateDbContext();
        SeedAssignment(dbContext, "a-visible", "e-visible", "Visible WAN", "10.0.0.1", "agent-visible", EndpointStateKind.Up);
        SeedAssignment(dbContext, "a-hidden", "e-hidden", "Hidden Router", "192.0.2.10", "agent-hidden", EndpointStateKind.Down);
        dbContext.StateTransitions.Add(new StateTransition
        {
            TransitionId = "tr-hidden",
            AssignmentId = "a-hidden",
            AgentId = "agent-hidden",
            EndpointId = "e-hidden",
            PreviousState = EndpointStateKind.Up,
            NewState = EndpointStateKind.Down,
            TransitionAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            ReasonCode = StateTransitionReasonCodes.FailureThresholdReached
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, new FakeUserAccessScopeService(isAdmin: false, visibleEndpointIds: new HashSet<string>(["e-visible"], StringComparer.Ordinal)));

        var result = await service.GetNetworkHealthSummaryAsync(User(), CancellationToken.None);

        var summary = result.Summary!;
        Assert.Equal(1, summary.VisibleEndpointCount);
        Assert.Equal(1, summary.StateCounts.Up);
        Assert.Equal(0, summary.StateCounts.Down);
        Assert.Empty(summary.DownEndpoints);
        Assert.DoesNotContain("Hidden Router", SerializeForAssertion(summary), StringComparison.Ordinal);
        Assert.Empty(summary.RecentStateChanges);
    }

    [Fact]
    public async Task HealthSummary_NonAdminCannotReceiveHiddenAgentContext()
    {
        await using var dbContext = CreateDbContext();
        SeedAssignment(dbContext, "a-visible", "e-visible", "Visible WAN", "10.0.0.1", "agent-visible", EndpointStateKind.Up, agentStatus: AgentHealthStatus.Offline);
        SeedAssignment(dbContext, "a-hidden", "e-hidden", "Hidden Router", "192.0.2.10", "agent-hidden", EndpointStateKind.Down, agentStatus: AgentHealthStatus.Stale);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, new FakeUserAccessScopeService(isAdmin: false, visibleEndpointIds: new HashSet<string>(["e-visible"], StringComparer.Ordinal)));

        var result = await service.GetNetworkHealthSummaryAsync(User(), CancellationToken.None);

        var summary = result.Summary!;
        var offlineAgent = Assert.Single(summary.OfflineAgents);
        Assert.Equal("agent-visible", offlineAgent.AgentId);
        Assert.Empty(summary.StaleAgents);
        Assert.DoesNotContain("agent-hidden", SerializeForAssertion(summary), StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden Router", SerializeForAssertion(summary), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthSummary_IncludesBoundedRecentStateChanges()
    {
        await using var dbContext = CreateDbContext();
        SeedAssignment(dbContext, "a-down", "e-down", "Farm Router WAN", "1.2.3.4", "agent-1", EndpointStateKind.Down);
        dbContext.StateTransitions.Add(new StateTransition
        {
            TransitionId = "tr-visible",
            AssignmentId = "a-down",
            AgentId = "agent-1",
            EndpointId = "e-down",
            PreviousState = EndpointStateKind.Up,
            NewState = EndpointStateKind.Down,
            TransitionAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            ReasonCode = StateTransitionReasonCodes.FailureThresholdReached
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, new FakeUserAccessScopeService(isAdmin: true));

        var result = await service.GetNetworkHealthSummaryAsync(User(), CancellationToken.None);

        Assert.Equal(1, result.Summary!.RecentStateChangeCount);
        var change = Assert.Single(result.Summary.RecentStateChanges);
        Assert.Equal("Farm Router WAN", change.EndpointName);
        Assert.Equal(EndpointStateKind.Up, change.PreviousState);
        Assert.Equal(EndpointStateKind.Down, change.NewState);
    }

    private static PingMonitorDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PingMonitorDbContext>()
            .UseInMemoryDatabase($"ai-monitoring-{Guid.NewGuid():N}")
            .Options;
        return new PingMonitorDbContext(options);
    }

    private static IAiMonitoringContextService CreateService(
        PingMonitorDbContext dbContext,
        IUserAccessScopeService userAccessScopeService)
        => new AiMonitoringContextService(dbContext, userAccessScopeService, new TestDbActivityScope());

    private static void SeedAssignment(
        PingMonitorDbContext dbContext,
        string assignmentId,
        string endpointId,
        string endpointName,
        string target,
        string agentId,
        EndpointStateKind? state,
        DateTimeOffset? lastChangedUtc = null,
        AgentHealthStatus agentStatus = AgentHealthStatus.Online)
    {
        var now = DateTimeOffset.UtcNow;
        if (!dbContext.Agents.Local.Any(x => x.AgentId == agentId))
        {
            dbContext.Agents.Add(new Agent
            {
                AgentId = agentId,
                InstanceId = $"{agentId}-instance",
                Name = $"{agentId} name",
                Enabled = true,
                ApiKeyHash = "hash",
                ApiKeyCreatedAtUtc = now,
                Status = agentStatus,
                LastHeartbeatUtc = now.AddMinutes(-3),
                CreatedAtUtc = now
            });
        }

        dbContext.Endpoints.Add(new Endpoint
        {
            EndpointId = endpointId,
            Name = endpointName,
            Target = target,
            Enabled = true,
            CreatedAtUtc = now
        });

        dbContext.MonitorAssignments.Add(new MonitorAssignment
        {
            AssignmentId = assignmentId,
            AgentId = agentId,
            EndpointId = endpointId,
            Enabled = true,
            PingIntervalSeconds = 60,
            RetryIntervalSeconds = 10,
            TimeoutMs = 1000,
            FailureThreshold = 3,
            RecoveryThreshold = 2,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        if (state.HasValue)
        {
            dbContext.EndpointStates.Add(new EndpointState
            {
                AssignmentId = assignmentId,
                AgentId = agentId,
                EndpointId = endpointId,
                CurrentState = state.Value,
                LastStateChangeUtc = lastChangedUtc ?? now.AddMinutes(-30),
                LastCheckUtc = now.AddMinutes(-1)
            });
        }
    }

    private static ClaimsPrincipal User() => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "user-1")],
        authenticationType: "test"));

    private static string SerializeForAssertion(AiNetworkHealthSummary summary)
        => System.Text.Json.JsonSerializer.Serialize(summary);

    private sealed class FakeUserAccessScopeService : IUserAccessScopeService
    {
        private readonly bool _isAdmin;
        private readonly IReadOnlySet<string> _visibleEndpointIds;

        public FakeUserAccessScopeService(bool isAdmin, IReadOnlySet<string>? visibleEndpointIds = null)
        {
            _isAdmin = isAdmin;
            _visibleEndpointIds = visibleEndpointIds ?? new HashSet<string>(StringComparer.Ordinal);
        }

        public Task<bool> IsAdminAsync(ClaimsPrincipal principal) => Task.FromResult(_isAdmin);

        public Task<IReadOnlySet<string>> GetVisibleEndpointIdsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
            => Task.FromResult(_visibleEndpointIds);

        public Task<bool> CanAccessAssignmentAsync(ClaimsPrincipal principal, string assignmentId, CancellationToken cancellationToken)
            => Task.FromResult(_isAdmin);
    }

    private sealed class TestDbActivityScope : IDbActivityScope
    {
        public string CurrentSubsystem => "Test";
        public IDisposable BeginScope(string subsystem) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
