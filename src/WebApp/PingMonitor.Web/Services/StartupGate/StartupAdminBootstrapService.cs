using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models.Identity;
using PingMonitor.Web.Services.Identity;

namespace PingMonitor.Web.Services.StartupGate;

internal sealed class StartupAdminBootstrapService : IStartupAdminBootstrapService
{
    public const string AdminRoleName = ApplicationRoles.Admin;

    private readonly IDbContextFactory<PingMonitorDbContext> _dbContextFactory;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ILogger<StartupAdminBootstrapService> _logger;

    public StartupAdminBootstrapService(
        IDbContextFactory<PingMonitorDbContext> dbContextFactory,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ILogger<StartupAdminBootstrapService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
    }

    public async Task<StartupAdminStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var adminRole = await dbContext.Roles.SingleOrDefaultAsync(role => role.Name == AdminRoleName, cancellationToken);
        if (adminRole is null)
        {
            return new StartupAdminStatus
            {
                AdminExists = false,
                Diagnostics = { "Admin role has not been created yet." }
            };
        }

        var adminExists = await dbContext.UserRoles.AnyAsync(userRole => userRole.RoleId == adminRole.Id, cancellationToken);
        return new StartupAdminStatus
        {
            AdminExists = adminExists,
            Diagnostics =
            {
                adminExists
                    ? "At least one admin user exists."
                    : "No admin user is assigned to the Admin role."
            }
        };
    }

    public async Task<(bool Succeeded, IReadOnlyList<string> Errors)> CreateInitialAdminAsync(StartupAdminBootstrapForm form, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Startup gate initial admin creation requested for username {Username}.", form.Username);

        var existingStatus = await GetStatusAsync(cancellationToken);
        if (existingStatus.AdminExists)
        {
            _logger.LogWarning("Startup gate initial admin creation rejected because an admin already exists.");
            return (false, ["An admin user already exists."]);
        }

        if (!await _roleManager.RoleExistsAsync(AdminRoleName))
        {
            var roleResult = await _roleManager.CreateAsync(new ApplicationRole { Name = AdminRoleName });
            if (!roleResult.Succeeded)
            {
                _logger.LogWarning("Startup gate failed to create the Admin role.");
                return (false, roleResult.Errors.Select(error => error.Description).ToArray());
            }
        }

        if (!await _roleManager.RoleExistsAsync(ApplicationRoles.User))
        {
            await _roleManager.CreateAsync(new ApplicationRole { Name = ApplicationRoles.User });
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = form.Username.Trim(),
            Email = form.Email.Trim(),
            NormalizedUserName = form.Username.Trim().ToUpperInvariant(),
            NormalizedEmail = form.Email.Trim().ToUpperInvariant(),
            EmailConfirmed = true
        };

        var createResult = await _userManager.CreateAsync(user, form.Password);
        if (!createResult.Succeeded)
        {
            _logger.LogWarning("Startup gate initial admin creation failed for username {Username}.", form.Username);
            return (false, createResult.Errors.Select(error => error.Description).ToArray());
        }

        var roleAssignResult = await _userManager.AddToRoleAsync(user, AdminRoleName);
        if (!roleAssignResult.Succeeded)
        {
            _logger.LogWarning("Startup gate failed to assign the Admin role to username {Username}.", form.Username);
            return (false, roleAssignResult.Errors.Select(error => error.Description).ToArray());
        }

        _logger.LogInformation("Startup gate initial admin creation succeeded for username {Username}.", form.Username);
        return (true, Array.Empty<string>());
    }
}
