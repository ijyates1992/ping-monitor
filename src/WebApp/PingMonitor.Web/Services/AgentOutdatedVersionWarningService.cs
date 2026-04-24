using PingMonitor.Web.Models;
using PingMonitor.Web.Services.EventLogs;

namespace PingMonitor.Web.Services;

internal sealed class AgentOutdatedVersionWarningService : IAgentOutdatedVersionWarningService
{
    private static readonly IReadOnlyDictionary<string, string> ReleaseUpgradeNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["V0.1.1"] = "this version brings a fix for a low risk vulnerability in dotenv"
    };

    private readonly IAgentTemplateVersionProvider _agentTemplateVersionProvider;
    private readonly IEventLogService _eventLogService;
    private readonly ILogger<AgentOutdatedVersionWarningService> _logger;

    public AgentOutdatedVersionWarningService(
        IAgentTemplateVersionProvider agentTemplateVersionProvider,
        IEventLogService eventLogService,
        ILogger<AgentOutdatedVersionWarningService> logger)
    {
        _agentTemplateVersionProvider = agentTemplateVersionProvider;
        _eventLogService = eventLogService;
        _logger = logger;
    }

    public async Task TryWriteWarningAsync(Agent agent, string reportedAgentVersion, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
    {
        var bundledVersionRaw = _agentTemplateVersionProvider.GetBundledAgentVersion();
        if (string.IsNullOrWhiteSpace(bundledVersionRaw) || string.IsNullOrWhiteSpace(reportedAgentVersion))
        {
            return;
        }

        if (!TryParseVersion(bundledVersionRaw, out var bundledVersion, out var bundledVersionCanonical))
        {
            _logger.LogWarning(
                "Skipping outdated-agent comparison because bundled agent version could not be parsed: {BundledVersion}",
                bundledVersionRaw);
            return;
        }

        if (!TryParseVersion(reportedAgentVersion, out var reportedVersion, out var reportedVersionCanonical))
        {
            _logger.LogWarning(
                "Skipping outdated-agent comparison because reported version could not be parsed for agent {AgentId}: {ReportedVersion}",
                agent.AgentId,
                reportedAgentVersion);
            return;
        }

        if (reportedVersion >= bundledVersion)
        {
            return;
        }

        if (string.Equals(agent.LastUpgradeWarningVersion, bundledVersionCanonical, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var upgradeNote = ReleaseUpgradeNotes.TryGetValue(bundledVersionCanonical, out var configuredNote)
            ? $"; {configuredNote}."
            : ".";

        var agentDisplay = agent.Name ?? agent.InstanceId;
        var message = $"Agent \"{agentDisplay}\" is running agent version {reportedVersionCanonical}, older than the currently bundled version {bundledVersionCanonical}. Please re-deploy this instance to upgrade to agent {bundledVersionCanonical}{upgradeNote}";

        await _eventLogService.WriteAsync(new EventLogWriteRequest
        {
            OccurredAtUtc = occurredAtUtc,
            Category = EventCategory.Agent,
            EventType = EventType.AgentOutdated,
            Severity = EventSeverity.Warning,
            AgentId = agent.AgentId,
            Message = message,
            DetailsJson = AgentOutdatedWarningRegistry.BuildDetailsMarker(bundledVersionCanonical)
        }, cancellationToken);

        agent.LastUpgradeWarningVersion = bundledVersionCanonical;
    }

    private static bool TryParseVersion(string value, out Version version, out string canonicalVersion)
    {
        version = new Version();
        canonicalVersion = string.Empty;

        var normalized = value.Trim();
        if (normalized.StartsWith("V", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        if (!Version.TryParse(normalized, out var parsedVersion))
        {
            return false;
        }

        version = parsedVersion;

        var major = version.Major;
        var minor = version.Minor >= 0 ? version.Minor : 0;
        var build = version.Build >= 0 ? version.Build : 0;
        version = new Version(major, minor, build);
        canonicalVersion = $"V{major}.{minor}.{build}";
        return true;
    }
}
