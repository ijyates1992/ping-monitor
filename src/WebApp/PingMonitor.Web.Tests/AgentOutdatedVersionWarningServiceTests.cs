using Microsoft.Extensions.Logging.Abstractions;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.EventLogs;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class AgentOutdatedVersionWarningServiceTests
{
    [Fact]
    public async Task TryWriteWarningAsync_MultipleOutdatedAgents_EachLogsOncePerBundledVersion()
    {
        var eventLogService = new RecordingEventLogService();
        var service = BuildService("V0.1.1", eventLogService);
        var firstAgent = BuildAgent("agent-1");
        var secondAgent = BuildAgent("agent-2");

        await service.TryWriteWarningAsync(firstAgent, "V0.1.0", DateTimeOffset.UtcNow, CancellationToken.None);
        await service.TryWriteWarningAsync(secondAgent, "V0.1.0", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(2, eventLogService.Writes.Count);
        Assert.All(eventLogService.Writes, warning =>
        {
            Assert.Equal(EventCategory.Agent, warning.Category);
            Assert.Equal(EventSeverity.Warning, warning.Severity);
            Assert.Equal(EventType.AgentOutdated, warning.EventType);
            Assert.Contains("agent version V0.1.0", warning.Message, StringComparison.Ordinal);
            Assert.Contains("bundled version V0.1.1", warning.Message, StringComparison.Ordinal);
            Assert.Contains("this version brings a fix for a low risk vulnerability in dotenv", warning.Message, StringComparison.Ordinal);
        });
        Assert.Equal("V0.1.1", firstAgent.LastUpgradeWarningVersion);
        Assert.Equal("V0.1.1", secondAgent.LastUpgradeWarningVersion);
    }

    [Fact]
    public async Task TryWriteWarningAsync_RepeatedHeartbeat_DoesNotSpam()
    {
        var eventLogService = new RecordingEventLogService();
        var service = BuildService("V0.1.1", eventLogService);
        var agent = BuildAgent("agent-1");

        await service.TryWriteWarningAsync(agent, "V0.1.0", DateTimeOffset.UtcNow, CancellationToken.None);
        await service.TryWriteWarningAsync(agent, "V0.1.0", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Single(eventLogService.Writes);
    }

    [Fact]
    public async Task TryWriteWarningAsync_BundledVersionBump_LogsAgainForSameAgent()
    {
        var eventLogService = new RecordingEventLogService();
        var firstService = BuildService("V0.1.1", eventLogService);
        var secondService = BuildService("V0.1.2", eventLogService);
        var agent = BuildAgent("agent-1");

        await firstService.TryWriteWarningAsync(agent, "V0.1.0", DateTimeOffset.UtcNow, CancellationToken.None);
        await secondService.TryWriteWarningAsync(agent, "V0.1.0", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(2, eventLogService.Writes.Count);
        Assert.Equal("V0.1.2", agent.LastUpgradeWarningVersion);
        Assert.Contains(eventLogService.Writes, x => x.DetailsJson == "bundledVersion=V0.1.1");
        Assert.Contains(eventLogService.Writes, x => x.DetailsJson == "bundledVersion=V0.1.2");
    }

    [Fact]
    public async Task TryWriteWarningAsync_UpToDateAgent_DoesNotWarn()
    {
        var eventLogService = new RecordingEventLogService();
        var service = BuildService("V0.1.1", eventLogService);

        await service.TryWriteWarningAsync(BuildAgent("agent-1"), "V0.1.1", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Empty(eventLogService.Writes);
    }

    [Fact]
    public async Task TryWriteWarningAsync_UnparsableVersion_DoesNotCrash()
    {
        var eventLogService = new RecordingEventLogService();
        var service = BuildService("V0.1.1", eventLogService);

        await service.TryWriteWarningAsync(BuildAgent("agent-1"), "not-a-version", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Empty(eventLogService.Writes);
    }

    private static AgentOutdatedVersionWarningService BuildService(
        string bundledVersion,
        RecordingEventLogService eventLogService)
    {
        return new AgentOutdatedVersionWarningService(
            new StaticAgentTemplateVersionProvider(bundledVersion),
            eventLogService,
            NullLogger<AgentOutdatedVersionWarningService>.Instance);
    }

    private static Agent BuildAgent(string agentId)
    {
        return new Agent
        {
            AgentId = agentId,
            InstanceId = "LOCAL",
            Name = "LOCAL"
        };
    }

    private sealed class StaticAgentTemplateVersionProvider : IAgentTemplateVersionProvider
    {
        private readonly string _bundledVersion;

        public StaticAgentTemplateVersionProvider(string bundledVersion)
        {
            _bundledVersion = bundledVersion;
        }

        public string? GetBundledAgentVersion() => _bundledVersion;
    }

    private sealed class RecordingEventLogService : IEventLogService
    {
        public List<EventLogWriteRequest> Writes { get; } = [];

        public Task WriteAsync(EventLogWriteRequest request, CancellationToken cancellationToken)
        {
            Writes.Add(request);
            return Task.CompletedTask;
        }
    }
}
