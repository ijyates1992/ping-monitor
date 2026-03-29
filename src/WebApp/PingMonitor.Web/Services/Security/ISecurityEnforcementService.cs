using PingMonitor.Web.Models;
using PingMonitor.Web.Models.Identity;

namespace PingMonitor.Web.Services.Security;

public interface ISecurityEnforcementService
{
    Task<SecurityIpBlockStatus> GetIpBlockStatusAsync(SecurityAuthType authType, string? sourceIpAddress, CancellationToken cancellationToken);
    Task<UserLockoutStatus> GetUserLockoutStatusAsync(ApplicationUser user, CancellationToken cancellationToken);
    Task EvaluateFailedAttemptAsync(SecurityAuthType authType, string? sourceIpAddress, CancellationToken cancellationToken);
    Task EvaluateFailedUserLockoutAsync(ApplicationUser user, CancellationToken cancellationToken);
}
