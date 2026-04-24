using Microsoft.Extensions.Hosting;

namespace PingMonitor.Web.Services;

internal static class AgentTemplateLocator
{
    public static string ResolveAgentRootPath(IWebHostEnvironment environment)
    {
        var bundledTemplatePath = Path.Combine(environment.ContentRootPath, "Agent");
        if (Directory.Exists(bundledTemplatePath))
        {
            return bundledTemplatePath;
        }

        return Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "Agent"));
    }
}
