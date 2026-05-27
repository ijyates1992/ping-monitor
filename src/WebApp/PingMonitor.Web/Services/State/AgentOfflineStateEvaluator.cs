using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.State;

internal static class AgentOfflineStateEvaluator
{
    public static bool ShouldForceUnknown(bool agentEnabled, bool apiKeyRevoked, AgentHealthStatus status, DateTimeOffset? lastSeenUtc, int unknownAfterSeconds, DateTimeOffset now)
    {
        if (!agentEnabled || apiKeyRevoked)
        {
            return true;
        }

        if (status == AgentHealthStatus.Online)
        {
            return false;
        }

        if (!lastSeenUtc.HasValue)
        {
            return true;
        }

        return now - lastSeenUtc.Value >= TimeSpan.FromSeconds(Math.Max(1, unknownAfterSeconds));
    }
}
