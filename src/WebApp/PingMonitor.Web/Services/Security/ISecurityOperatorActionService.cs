namespace PingMonitor.Web.Services.Security;

public interface ISecurityOperatorActionService
{
    Task<ActiveSecurityIpBlockDetails?> GetActiveIpBlockAsync(string securityIpBlockId, CancellationToken cancellationToken);
    Task<LockedOutUserDetails?> GetLockedOutUserAsync(string userId, CancellationToken cancellationToken);
    Task<SecurityIpBlockOperationResult> RemoveIpBlockAsync(RemoveSecurityIpBlockRequest request, CancellationToken cancellationToken);
    Task<SecurityUserUnlockOperationResult> UnlockUserAsync(UnlockSecurityUserRequest request, CancellationToken cancellationToken);
}
