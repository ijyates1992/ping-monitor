namespace PingMonitor.Web.Services.Security;

public interface ISecurityLogRetentionService
{
    Task<SecurityLogRetentionPreview> GetPreviewAsync(CancellationToken cancellationToken);
    Task<SecurityLogPruneResult> PruneAsync(SecurityLogPruneRequest request, CancellationToken cancellationToken);
}
