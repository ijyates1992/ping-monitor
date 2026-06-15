using System.Security.Claims;

namespace PingMonitor.Web.Services.AiTools;

public interface IAiMonitoringContextService
{
    Task<AiMonitoringContextResult> GetNetworkHealthSummaryAsync(ClaimsPrincipal user, CancellationToken cancellationToken);
}

