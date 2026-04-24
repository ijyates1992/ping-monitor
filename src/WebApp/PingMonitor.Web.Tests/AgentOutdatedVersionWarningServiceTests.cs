using Microsoft.Extensions.Logging.Abstractions;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.EventLogs;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class AgentOutdatedVersionWarningServiceTests
{
    [Fact]
    public async Task TryWriteWarningAsync_OutdatedAgent_WritesWarningOnce()
    {
        var registry = new FakeWarningRegistry();
        var eventLogService = new RecordingEventLogService(registry);
        var service = BuildService("V0.1.1", registry, eventLogService);
        var agent = BuildAgent();

        await service.TryWriteWarningAsync(agent, "V0.1.0", DateTimeOffset.UtcNow, CancellationToken.None);

        var warning = Assert.Single(eventLogService.Writes);
        Assert.Equal(EventCategory.Agent, warning.Category);
        Assert.Equal(EventSeverity.Warning, warning.Severity);
        Assert.Equal(EventType.AgentOutdated, warning.EventType);
        Assert.Contains("agent version V0.1.0", warning.Message, StringComparison.Ordinal);
        Assert.Contains("bundled version V0.1.1", warning.Message, StringComparison.Ordinal);
        Assert.Contains("this version brings a fix for a low risk vulnerability in dotenv", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryWriteWarningAsync_RepeatedHeartbeat_DoesNotSpam()
    {
        var registry = new FakeWarningRegistry();
        var eventLogService = new RecordingEventLogService(registry);
        var service = BuildService("V0.1.1", registry, eventLogService);
        var agent = BuildAgent();

        await service.TryWriteWarningAsync(agent, "V0.1.0", DateTimeOffset.UtcNow, CancellationToken.None);
        await service.TryWriteWarningAsync(agent, "V0.1.0", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Single(eventLogService.Writes);
    }

    [Fact]
    public async Task TryWriteWarningAsync_UpToDateAgent_DoesNotWarn()
    {
        var registry = new FakeWarningRegistry();
        var eventLogService = new RecordingEventLogService(registry);
        var service = BuildService("V0.1.1", registry, eventLogService);

        await service.TryWriteWarningAsync(BuildAgent(), "V0.1.1", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Empty(eventLogService.Writes);
    }

    [Fact]
    public async Task TryWriteWarningAsync_UnparsableVersion_DoesNotCrash()
    {
        var registry = new FakeWarningRegistry();
        var eventLogService = new RecordingEventLogService(registry);
        var service = BuildService("V0.1.1", registry, eventLogService);

        await service.TryWriteWarningAsync(BuildAgent(), "not-a-version", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Empty(eventLogService.Writes);
    }

    private static AgentOutdatedVersionWarningService BuildService(
        string bundledVersion,
        FakeWarningRegistry registry,
        RecordingEventLogService eventLogService)
    {
        return new AgentOutdatedVersionWarningService(
            new StaticAgentTemplateVersionProvider(bundledVersion),
            registry,
            eventLogService,
            NullLogger<AgentOutdatedVersionWarningService>.Instance);
    }

    private static Agent BuildAgent()
    {
        return new Agent
        {
            AgentId = "agent-1",
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

    private sealed class FakeWarningRegistry : IAgentOutdatedWarningRegistry
    {
        private readonly HashSet<string> _warnings = [];

        public Task<bool> HasWarningAsync(string agentId, string bundledVersion, CancellationToken cancellationToken)
        {
            return Task.FromResult(_warnings.Contains(GetKey(agentId, bundledVersion)));
        }

        public void Record(string agentId, string bundledVersion)
        {
            _warnings.Add(GetKey(agentId, bundledVersion));
        }

        private static string GetKey(string agentId, string bundledVersion) => $"{agentId}|{bundledVersion}";
    }

    private sealed class RecordingEventLogService : IEventLogService
    {
        private readonly FakeWarningRegistry _registry;

        public RecordingEventLogService(FakeWarningRegistry registry)
        {
            _registry = registry;
        }

        public List<EventLogWriteRequest> Writes { get; } = [];

        public Task WriteAsync(EventLogWriteRequest request, CancellationToken cancellationToken)
        {
            Writes.Add(request);

            if (request.EventType == EventType.AgentOutdated && !string.IsNullOrWhiteSpace(request.AgentId) && !string.IsNullOrWhiteSpace(request.DetailsJson))
            {
                const string prefix = "bundledVersion=";
                if (request.DetailsJson.StartsWith(prefix, StringComparison.Ordinal))
                {
                    _registry.Record(request.AgentId, request.DetailsJson[prefix.Length..]);
                }
            }

            return Task.CompletedTask;
        }
    }
}
