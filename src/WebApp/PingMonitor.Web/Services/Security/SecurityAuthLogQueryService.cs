using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;

namespace PingMonitor.Web.Services.Security;

internal sealed class SecurityAuthLogQueryService : ISecurityAuthLogQueryService
{
    private readonly PingMonitorDbContext _dbContext;

    public SecurityAuthLogQueryService(PingMonitorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<SecurityAuthLogListItem>> GetRecentAsync(SecurityAuthLogQuery query, CancellationToken cancellationToken)
    {
        var effectiveLimit = Math.Clamp(query.Limit, 1, 200);

        var rows = await _dbContext.SecurityAuthLogs
            .AsNoTracking()
            .Where(x => x.AuthType == query.AuthType && (query.IncludeSuccessful || !x.Success))
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(effectiveLimit)
            .Select(x => new SecurityAuthLogListItem
            {
                OccurredAtUtc = x.OccurredAtUtc,
                SubjectIdentifier = x.SubjectIdentifier,
                SourceIpAddress = x.SourceIpAddress,
                Success = x.Success,
                FailureReason = x.FailureReason,
                UserId = x.UserId,
                AgentId = x.AgentId
            })
            .ToListAsync(cancellationToken);

        return rows;
    }
}
