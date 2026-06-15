using System.Security.Claims;

namespace PingMonitor.Web.Services.AiTools;

public interface IAiEndpointLookupService
{
    Task<AiEndpointLookupResult> SearchEndpointsAsync(ClaimsPrincipal user, string userMessage, CancellationToken cancellationToken);
}

public interface IAiEndpointDiagnosticsService
{
    Task<AiEndpointDiagnosticsResult> GetDiagnosticsPackAsync(ClaimsPrincipal user, string endpointId, string requestedWindow, CancellationToken cancellationToken);
}
