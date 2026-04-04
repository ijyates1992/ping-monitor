using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models.Identity;

namespace PingMonitor.Web.Services.Identity;

internal sealed class UserAccessScopeService : IUserAccessScopeService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly PingMonitorDbContext _dbContext;

    public UserAccessScopeService(UserManager<ApplicationUser> userManager, PingMonitorDbContext dbContext)
    {
        _userManager = userManager;
        _dbContext = dbContext;
    }

    public async Task<bool> IsAdminAsync(ClaimsPrincipal principal)
    {
        var user = await _userManager.GetUserAsync(principal);
        return user is not null && await _userManager.IsInRoleAsync(user, ApplicationRoles.Admin);
    }

    public async Task<IReadOnlySet<string>> GetVisibleEndpointIdsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(principal);
        if (user is null)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        if (await _userManager.IsInRoleAsync(user, ApplicationRoles.Admin))
        {
            return (await _dbContext.Endpoints.AsNoTracking().Select(x => x.EndpointId).ToArrayAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);
        }

        var direct = await _dbContext.UserEndpointAccesses.AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .Select(x => x.EndpointId)
            .ToArrayAsync(cancellationToken);

        var grouped = await (
                from membership in _dbContext.EndpointGroupMemberships.AsNoTracking()
                join access in _dbContext.UserGroupAccesses.AsNoTracking() on membership.GroupId equals access.GroupId
                where access.UserId == user.Id
                select membership.EndpointId)
            .ToArrayAsync(cancellationToken);

        return direct.Concat(grouped).ToHashSet(StringComparer.Ordinal);
    }

    public async Task<bool> CanAccessAssignmentAsync(ClaimsPrincipal principal, string assignmentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            return false;
        }

        if (await IsAdminAsync(principal))
        {
            return true;
        }

        var endpointId = await _dbContext.MonitorAssignments.AsNoTracking()
            .Where(x => x.AssignmentId == assignmentId)
            .Select(x => x.EndpointId)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(endpointId))
        {
            return false;
        }

        var visible = await GetVisibleEndpointIdsAsync(principal, cancellationToken);
        return visible.Contains(endpointId);
    }
}
