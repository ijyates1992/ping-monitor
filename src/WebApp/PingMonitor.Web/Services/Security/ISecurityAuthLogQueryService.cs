namespace PingMonitor.Web.Services.Security;

public interface ISecurityAuthLogQueryService
{
    Task<IReadOnlyList<SecurityAuthLogListItem>> GetRecentAsync(SecurityAuthLogQuery query, CancellationToken cancellationToken);
}
