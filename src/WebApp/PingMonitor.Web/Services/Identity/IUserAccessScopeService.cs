using System.Security.Claims;

namespace PingMonitor.Web.Services.Identity;

public interface IUserAccessScopeService
{
    Task<bool> IsAdminAsync(ClaimsPrincipal principal);
    Task<IReadOnlySet<string>> GetVisibleEndpointIdsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
    Task<bool> CanAccessAssignmentAsync(ClaimsPrincipal principal, string assignmentId, CancellationToken cancellationToken);
}
