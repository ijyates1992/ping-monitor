using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Models.Identity;

namespace PingMonitor.Web.Services.Identity;

internal sealed class UserManagementService : IUserManagementService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly PingMonitorDbContext _dbContext;

    public UserManagementService(UserManager<ApplicationUser> userManager, PingMonitorDbContext dbContext)
    {
        _userManager = userManager;
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<UserManagementListItem>> ListUsersAsync(CancellationToken cancellationToken)
    {
        var users = await _dbContext.Users.AsNoTracking().OrderBy(x => x.UserName).ToArrayAsync(cancellationToken);
        var rows = new List<UserManagementListItem>(users.Length);
        foreach (var user in users)
        {
            var role = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? ApplicationRoles.User;
            rows.Add(new UserManagementListItem
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                Role = role,
                Enabled = !user.LockoutEnabled || user.LockoutEnd is null || user.LockoutEnd <= DateTimeOffset.UtcNow
            });
        }

        return rows;
    }

    public async Task<UserManagementEditModel?> GetUserAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return null;
        }

        var role = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? ApplicationRoles.User;
        var groups = await _dbContext.UserGroupAccesses.AsNoTracking().Where(x => x.UserId == user.Id).Select(x => x.GroupId).ToArrayAsync(cancellationToken);
        var endpoints = await _dbContext.UserEndpointAccesses.AsNoTracking().Where(x => x.UserId == user.Id).Select(x => x.EndpointId).ToArrayAsync(cancellationToken);

        return new UserManagementEditModel
        {
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            Role = role,
            Enabled = !user.LockoutEnabled || user.LockoutEnd is null || user.LockoutEnd <= DateTimeOffset.UtcNow,
            SelectedGroupIds = groups,
            SelectedEndpointIds = endpoints
        };
    }

    public async Task<IReadOnlyList<UserManagementOption>> GetGroupOptionsAsync(CancellationToken cancellationToken) =>
        await _dbContext.Groups.AsNoTracking().OrderBy(x => x.Name).Select(x => new UserManagementOption { Id = x.GroupId, Name = x.Name }).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<UserManagementOption>> GetEndpointOptionsAsync(CancellationToken cancellationToken) =>
        await _dbContext.Endpoints.AsNoTracking().OrderBy(x => x.Name).Select(x => new UserManagementOption { Id = x.EndpointId, Name = x.Name }).ToArrayAsync(cancellationToken);

    public async Task<UserManagementResult> CreateAsync(UserManagementSaveCommand command, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = command.UserName.Trim(),
            Email = command.Email.Trim(),
            EmailConfirmed = true,
            LockoutEnabled = true
        };

        var result = await _userManager.CreateAsync(user, command.Password ?? string.Empty);
        if (!result.Succeeded)
        {
            return new UserManagementResult { Success = false, Found = false, Errors = result.Errors.Select(x => x.Description).ToArray() };
        }

        return await ApplyRoleAndScopeAsync(user, command, cancellationToken);
    }

    public async Task<UserManagementResult> UpdateAsync(UserManagementSaveCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.UserId))
        {
            return new UserManagementResult { Success = false, Found = false, Errors = ["User id is required."] };
        }

        var user = await _userManager.FindByIdAsync(command.UserId);
        if (user is null)
        {
            return new UserManagementResult { Success = false, Found = false, Errors = ["User not found."] };
        }

        var trimmedEmail = command.Email.Trim();
        var emailChanged = !string.Equals(user.Email, trimmedEmail, StringComparison.OrdinalIgnoreCase);
        user.UserName = command.UserName.Trim();
        user.Email = trimmedEmail;
        if (emailChanged)
        {
            user.EmailConfirmed = false;
        }
        user.NormalizedUserName = command.UserName.Trim().ToUpperInvariant();
        user.NormalizedEmail = trimmedEmail.ToUpperInvariant();
        user.LockoutEnabled = true;
        user.LockoutEnd = command.Enabled ? null : DateTimeOffset.MaxValue;

        var update = await _userManager.UpdateAsync(user);
        if (!update.Succeeded)
        {
            return new UserManagementResult { Success = false, Found = true, Errors = update.Errors.Select(x => x.Description).ToArray() };
        }

        return await ApplyRoleAndScopeAsync(user, command, cancellationToken);
    }

    private async Task<UserManagementResult> ApplyRoleAndScopeAsync(ApplicationUser user, UserManagementSaveCommand command, CancellationToken cancellationToken)
    {
        var existingRoles = await _userManager.GetRolesAsync(user);
        if (existingRoles.Count > 0)
        {
            var removeRoles = await _userManager.RemoveFromRolesAsync(user, existingRoles);
            if (!removeRoles.Succeeded)
            {
                return new UserManagementResult { Success = false, Found = true, Errors = removeRoles.Errors.Select(x => x.Description).ToArray() };
            }
        }

        var roleName = string.Equals(command.Role, ApplicationRoles.Admin, StringComparison.Ordinal) ? ApplicationRoles.Admin : ApplicationRoles.User;
        var addRole = await _userManager.AddToRoleAsync(user, roleName);
        if (!addRole.Succeeded)
        {
            return new UserManagementResult { Success = false, Found = true, Errors = addRole.Errors.Select(x => x.Description).ToArray() };
        }

        var existingGroupAccess = _dbContext.UserGroupAccesses.Where(x => x.UserId == user.Id);
        var existingEndpointAccess = _dbContext.UserEndpointAccesses.Where(x => x.UserId == user.Id);
        _dbContext.UserGroupAccesses.RemoveRange(existingGroupAccess);
        _dbContext.UserEndpointAccesses.RemoveRange(existingEndpointAccess);

        if (roleName == ApplicationRoles.User)
        {
            foreach (var groupId in command.GroupIds.Distinct(StringComparer.Ordinal))
            {
                _dbContext.UserGroupAccesses.Add(new UserGroupAccess
                {
                    UserGroupAccessId = Guid.NewGuid().ToString("N"),
                    UserId = user.Id,
                    GroupId = groupId,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
            }

            foreach (var endpointId in command.EndpointIds.Distinct(StringComparer.Ordinal))
            {
                _dbContext.UserEndpointAccesses.Add(new UserEndpointAccess
                {
                    UserEndpointAccessId = Guid.NewGuid().ToString("N"),
                    UserId = user.Id,
                    EndpointId = endpointId,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new UserManagementResult { Success = true, Found = true, Errors = [] };
    }
}
