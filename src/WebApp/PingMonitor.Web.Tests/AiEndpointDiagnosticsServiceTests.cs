using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.AiTools;
using PingMonitor.Web.Services.Diagnostics;
using PingMonitor.Web.Services.Identity;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class AiEndpointDiagnosticsServiceTests
{
    [Fact]
    public async Task Lookup_FindsVisibleEndpointByExactNameAndTarget_AndHidesInvisible()
    {
        await using var db = CreateDb();
        Seed(db, "a1", "e1", "Google DNS", "8.8.8.8", EndpointStateKind.Up);
        Seed(db, "a2", "e2", "Hidden WAN", "192.0.2.1", EndpointStateKind.Down);
        await db.SaveChangesAsync();
        var svc = new AiEndpointLookupService(db, new FakeAccess(false, new HashSet<string>(["e1"], StringComparer.Ordinal)), new TestDbActivityScope());

        Assert.Equal("e1", (await svc.SearchEndpointsAsync(User(), "Has Google DNS been down today?", CancellationToken.None)).StrongMatch!.EndpointId);
        Assert.Equal("e1", (await svc.SearchEndpointsAsync(User(), "What has uptime been like for 8.8.8.8?", CancellationToken.None)).StrongMatch!.EndpointId);
        Assert.Null((await svc.SearchEndpointsAsync(User(), "What is going on with Hidden WAN?", CancellationToken.None)).StrongMatch);
    }

    [Fact]
    public async Task Lookup_ReturnsAmbiguousAndNoMatchResponses()
    {
        await using var db = CreateDb();
        Seed(db, "a1", "e1", "Kitchen Access Point", "10.0.0.2", EndpointStateKind.Up);
        Seed(db, "a2", "e2", "Kitchen Switch", "10.0.0.3", EndpointStateKind.Up);
        await db.SaveChangesAsync();
        var svc = new AiEndpointLookupService(db, new FakeAccess(true), new TestDbActivityScope());

        var ambiguous = await svc.SearchEndpointsAsync(User(), "Is Kitchen flapping?", CancellationToken.None);
        Assert.True(ambiguous.Ambiguous);
        Assert.Equal(2, ambiguous.Matches.Count);
        Assert.Contains("No matching", (await svc.SearchEndpointsAsync(User(), "Is Barn Router flapping?", CancellationToken.None)).Message);
    }

    [Fact]
    public async Task Diagnostics_IncludesCurrentStateUptimeCheckSummaryAndBoundedSeries()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        Seed(db, "a1", "e1", "WFP WAN", "1.2.3.4", EndpointStateKind.Unknown, AgentHealthStatus.Offline);
        db.AssignmentStateIntervals.AddRange(
            new AssignmentStateInterval { AssignmentStateIntervalId = "i1", AssignmentId = "a1", State = EndpointStateKind.Up, StartedAtUtc = now.AddHours(-3), EndedAtUtc = now.AddHours(-2), UpdatedAtUtc = now },
            new AssignmentStateInterval { AssignmentStateIntervalId = "i2", AssignmentId = "a1", State = EndpointStateKind.Unknown, StartedAtUtc = now.AddHours(-2), EndedAtUtc = now.AddHours(-1), UpdatedAtUtc = now },
            new AssignmentStateInterval { AssignmentStateIntervalId = "i3", AssignmentId = "a1", State = EndpointStateKind.Down, StartedAtUtc = now.AddHours(-1), EndedAtUtc = now.AddMinutes(-30), UpdatedAtUtc = now });
        db.StateTransitions.AddRange(
            new StateTransition { TransitionId = "t1", AssignmentId = "a1", AgentId = "agent", EndpointId = "e1", PreviousState = EndpointStateKind.Up, NewState = EndpointStateKind.Down, TransitionAtUtc = now.AddHours(-1) },
            new StateTransition { TransitionId = "t2", AssignmentId = "a1", AgentId = "agent", EndpointId = "e1", PreviousState = EndpointStateKind.Down, NewState = EndpointStateKind.Up, TransitionAtUtc = now.AddMinutes(-20) });
        for (var i = 0; i < 150; i++) db.CheckResults.Add(new CheckResult { CheckResultId = $"c{i}", AssignmentId = "a1", CheckedAtUtc = now.AddMinutes(-i), ReceivedAtUtc = now.AddMinutes(-i), Success = i % 10 != 0, RoundTripMs = i, BatchId = "b" });
        await db.SaveChangesAsync();

        var svc = new AiEndpointDiagnosticsService(db, new FakeAccess(true), new TestDbActivityScope());
        var result = await svc.GetDiagnosticsPackAsync(User(), "e1", "30d", CancellationToken.None);

        Assert.True(result.Succeeded);
        var pack = result.Pack!;
        Assert.Equal(EndpointStateKind.Unknown, pack.CurrentState.State);
        Assert.Contains("UNKNOWN is not endpoint downtime", pack.CurrentState.UnknownReason);
        Assert.True(pack.Window.Clamped);
        Assert.True(pack.Uptime.UnknownSeconds > 0);
        Assert.True(pack.Uptime.DowntimeSeconds > 0);
        Assert.NotEqual(pack.Uptime.UnknownSeconds, pack.Uptime.DowntimeSeconds);
        Assert.Equal(15, pack.Checks.FailedSamples);
        Assert.True(pack.Rtt.Available);
        Assert.Equal(120, pack.RecentSampleTail.Count);
        Assert.Equal(1, pack.Uptime.DownTransitions);
        Assert.Equal(1, pack.Uptime.RecoveryTransitions);
    }

    [Fact]
    public void ResolveWindow_SupportsAndClampsSafeWindows()
    {
        var now = DateTimeOffset.Parse("2026-06-15T12:00:00Z");
        Assert.Equal("1h", AiEndpointDiagnosticsService.ResolveWindow("1h", now).AppliedWindow);
        Assert.Equal("6h", AiEndpointDiagnosticsService.ResolveWindow("6h", now).AppliedWindow);
        Assert.Equal("24h", AiEndpointDiagnosticsService.ResolveWindow("today", now).AppliedWindow);
        Assert.Equal("7d", AiEndpointDiagnosticsService.ResolveWindow("7d", now).AppliedWindow);
        Assert.True(AiEndpointDiagnosticsService.ResolveWindow("90d", now).Clamped);
    }

    private static PingMonitorDbContext CreateDb() => new(new DbContextOptionsBuilder<PingMonitorDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static ClaimsPrincipal User() => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user")], "test"));
    private static void Seed(PingMonitorDbContext db, string assignmentId, string endpointId, string name, string target, EndpointStateKind state, AgentHealthStatus agentStatus = AgentHealthStatus.Online)
    {
        var now = DateTimeOffset.UtcNow;
        if (!db.Agents.Local.Any()) db.Agents.Add(new Agent { AgentId = "agent", InstanceId = "agent-instance", Name = "Agent", Enabled = true, ApiKeyHash = "hash", ApiKeyCreatedAtUtc = now, CreatedAtUtc = now, Status = agentStatus });
        db.Endpoints.Add(new Endpoint { EndpointId = endpointId, Name = name, Target = target, Enabled = true, CreatedAtUtc = now });
        db.MonitorAssignments.Add(new MonitorAssignment { AssignmentId = assignmentId, AgentId = "agent", EndpointId = endpointId, Enabled = true, PingIntervalSeconds = 60, RetryIntervalSeconds = 10, TimeoutMs = 1000, FailureThreshold = 3, RecoveryThreshold = 2, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.EndpointStates.Add(new EndpointState { AssignmentId = assignmentId, AgentId = "agent", EndpointId = endpointId, CurrentState = state, LastStateChangeUtc = now.AddMinutes(-5), LastCheckUtc = now.AddMinutes(-1) });
    }

    private sealed class FakeAccess : IUserAccessScopeService
    {
        private readonly bool _admin; private readonly IReadOnlySet<string> _visible;
        public FakeAccess(bool admin, IReadOnlySet<string>? visible = null) { _admin = admin; _visible = visible ?? new HashSet<string>(); }
        public Task<bool> IsAdminAsync(ClaimsPrincipal principal) => Task.FromResult(_admin);
        public Task<IReadOnlySet<string>> GetVisibleEndpointIdsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken) => Task.FromResult(_visible);
        public Task<bool> CanAccessAssignmentAsync(ClaimsPrincipal principal, string assignmentId, CancellationToken cancellationToken) => Task.FromResult(_admin);
    }

    private sealed class TestDbActivityScope : IDbActivityScope
    {
        public string CurrentSubsystem => "Test";
        public IDisposable BeginScope(string subsystem) => new Scope();
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }
}
