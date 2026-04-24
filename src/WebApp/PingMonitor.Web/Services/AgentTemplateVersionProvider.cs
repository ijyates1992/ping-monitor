using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PingMonitor.Web.Services;

internal sealed partial class AgentTemplateVersionProvider : IAgentTemplateVersionProvider
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<AgentTemplateVersionProvider> _logger;

    public AgentTemplateVersionProvider(
        IWebHostEnvironment environment,
        ILogger<AgentTemplateVersionProvider> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public string? GetBundledAgentVersion()
    {
        var agentRoot = AgentTemplateLocator.ResolveAgentRootPath(_environment);
        var versionFilePath = Path.Combine(agentRoot, "app", "version.py");
        if (!File.Exists(versionFilePath))
        {
            _logger.LogWarning("Bundled agent version file was not found at {VersionFilePath}", versionFilePath);
            return null;
        }

        var fileContents = File.ReadAllText(versionFilePath);
        var match = AgentVersionPattern().Match(fileContents);
        if (!match.Success)
        {
            _logger.LogWarning("Bundled agent version could not be read from {VersionFilePath}", versionFilePath);
            return null;
        }

        return match.Groups["version"].Value.Trim();
    }

    [GeneratedRegex("""^\s*AGENT_VERSION\s*=\s*["'](?<version>[^"']+)["']\s*$""", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex AgentVersionPattern();
}
