using Microsoft.AspNetCore.Identity;
using PingMonitor.Web.Models.Identity;

namespace PingMonitor.Web.Services.Identity;

public static class RoleBootstrapService
{
    public static async Task EnsureRolesAsync(RoleManager<ApplicationRole> roleManager)
    {
        if (!await roleManager.RoleExistsAsync(ApplicationRoles.Admin))
        {
            await roleManager.CreateAsync(new ApplicationRole { Name = ApplicationRoles.Admin });
        }

        if (!await roleManager.RoleExistsAsync(ApplicationRoles.User))
        {
            await roleManager.CreateAsync(new ApplicationRole { Name = ApplicationRoles.User });
        }
    }
}
