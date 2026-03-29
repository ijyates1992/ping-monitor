namespace PingMonitor.Web.Services.Security;

public interface ISecurityIpBlockService
{
    Task<IReadOnlyList<SecurityIpBlockListItem>> ListActiveAsync(CancellationToken cancellationToken);
    Task<SecurityIpBlockOperationResult> AddManualBlockAsync(ManualSecurityIpBlockRequest request, CancellationToken cancellationToken);
    Task<SecurityIpBlockOperationResult> RemoveAsync(RemoveSecurityIpBlockRequest request, CancellationToken cancellationToken);
}
