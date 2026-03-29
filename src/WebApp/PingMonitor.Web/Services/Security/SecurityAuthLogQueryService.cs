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
        var searchText = query.SearchText?.Trim();

        var dbQuery = _dbContext.SecurityAuthLogs
            .AsNoTracking()
            .Where(x => x.AuthType == query.AuthType)
            .Where(x => query.IncludeSuccessful || !x.Success)
            .Where(x => !query.FromUtc.HasValue || x.OccurredAtUtc >= query.FromUtc.Value)
            .Where(x => !query.ToUtc.HasValue || x.OccurredAtUtc <= query.ToUtc.Value);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            dbQuery = dbQuery.Where(x =>
                x.SubjectIdentifier.Contains(searchText) ||
                (x.SourceIpAddress != null && x.SourceIpAddress.Contains(searchText)) ||
                (x.FailureReason != null && x.FailureReason.Contains(searchText)) ||
                (x.UserId != null && x.UserId.Contains(searchText)) ||
                (x.AgentId != null && x.AgentId.Contains(searchText)));
        }

        var rows = await dbQuery
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
