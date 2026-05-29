using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PingMonitor.Web.Controllers;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.ViewModels.Users;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class UsersControllerDeleteTests
{
    [Fact]
    public async Task Delete_Post_AdminCanDeleteAnotherNonLastAdminUser()
    {
        var service = new FakeUserManagementService(
            new UserManagementListItem { UserId = "admin-1", UserName = "admin", Email = "admin@example.com", Role = ApplicationRoles.Admin, Enabled = true },
            new UserManagementListItem { UserId = "user-1", UserName = "target", Email = "target@example.com", Role = ApplicationRoles.User, Enabled = true });
        var controller = CreateController(service, "admin-1", "admin");

        var result = await controller.Delete("user-1", new UserDeletePageViewModel
        {
            UserId = "user-1",
            UserName = "target",
            Email = "target@example.com",
            Roles = [ApplicationRoles.User],
            CanDelete = true,
            ConfirmationText = "DELETE"
        }, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.True(service.DeleteCalled);
        Assert.DoesNotContain(service.Users, x => x.UserId == "user-1");
        Assert.Equal(7, service.MonitoringDataRecordCount);
    }

    [Fact]
    public async Task Delete_Post_AdminCannotDeleteSelf()
    {
        var service = new FakeUserManagementService(
            new UserManagementListItem { UserId = "admin-1", UserName = "admin", Email = "admin@example.com", Role = ApplicationRoles.Admin, Enabled = true },
            new UserManagementListItem { UserId = "admin-2", UserName = "other-admin", Email = "other@example.com", Role = ApplicationRoles.Admin, Enabled = true });
        var controller = CreateController(service, "admin-1", "admin");

        var result = await controller.Delete("admin-1", new UserDeletePageViewModel
        {
            UserId = "admin-1",
            UserName = "admin",
            Email = "admin@example.com",
            Roles = [ApplicationRoles.Admin],
            CanDelete = true,
            ConfirmationText = "DELETE"
        }, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.False(service.DeleteCalled);
        Assert.Contains(service.Users, x => x.UserId == "admin-1");
    }

    [Fact]
    public async Task Delete_Post_AdminCannotDeleteLastAdmin()
    {
        var service = new FakeUserManagementService(
            new UserManagementListItem { UserId = "admin-1", UserName = "admin", Email = "admin@example.com", Role = ApplicationRoles.Admin, Enabled = true },
            new UserManagementListItem { UserId = "user-1", UserName = "user", Email = "user@example.com", Role = ApplicationRoles.User, Enabled = true });
        var controller = CreateController(service, "admin-2", "outside-admin");

        var result = await controller.Delete("admin-1", new UserDeletePageViewModel
        {
            UserId = "admin-1",
            UserName = "admin",
            Email = "admin@example.com",
            Roles = [ApplicationRoles.Admin],
            CanDelete = true,
            ConfirmationText = "DELETE"
        }, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.False(service.DeleteCalled);
        Assert.Contains(service.Users, x => x.UserId == "admin-1");
    }

    [Fact]
    public void Delete_EndpointsRequireAdminRole()
    {
        var authorize = Assert.Single(typeof(UsersController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
        Assert.Equal(ApplicationRoles.Admin, Assert.IsType<AuthorizeAttribute>(authorize).Roles);
    }

    [Fact]
    public async Task Delete_Get_NonexistentUserReturnsNotFound()
    {
        var service = new FakeUserManagementService(
            new UserManagementListItem { UserId = "admin-1", UserName = "admin", Email = "admin@example.com", Role = ApplicationRoles.Admin, Enabled = true });
        var controller = CreateController(service, "admin-1", "admin");

        var result = await controller.Delete("missing", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Index_AfterSuccessfulDelete_RemovesUserFromManagementList()
    {
        var service = new FakeUserManagementService(
            new UserManagementListItem { UserId = "admin-1", UserName = "admin", Email = "admin@example.com", Role = ApplicationRoles.Admin, Enabled = true },
            new UserManagementListItem { UserId = "user-1", UserName = "target", Email = "target@example.com", Role = ApplicationRoles.User, Enabled = true });
        var controller = CreateController(service, "admin-1", "admin");

        await controller.Delete("user-1", new UserDeletePageViewModel
        {
            UserId = "user-1",
            UserName = "target",
            Email = "target@example.com",
            Roles = [ApplicationRoles.User],
            CanDelete = true,
            ConfirmationText = "DELETE"
        }, CancellationToken.None);

        var result = await controller.Index(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ManageUsersPageViewModel>(view.Model);
        Assert.DoesNotContain(model.Users, x => x.UserId == "user-1");
    }

    [Fact]
    public async Task Delete_Post_SuccessfulDeleteLeavesRelatedMonitoringDataIntact()
    {
        var service = new FakeUserManagementService(
            new UserManagementListItem { UserId = "admin-1", UserName = "admin", Email = "admin@example.com", Role = ApplicationRoles.Admin, Enabled = true },
            new UserManagementListItem { UserId = "user-1", UserName = "target", Email = "target@example.com", Role = ApplicationRoles.User, Enabled = true });
        var controller = CreateController(service, "admin-1", "admin");
        var beforeCount = service.MonitoringDataRecordCount;

        await controller.Delete("user-1", new UserDeletePageViewModel
        {
            UserId = "user-1",
            UserName = "target",
            Email = "target@example.com",
            Roles = [ApplicationRoles.User],
            CanDelete = true,
            ConfirmationText = "DELETE"
        }, CancellationToken.None);

        Assert.Equal(beforeCount, service.MonitoringDataRecordCount);
    }

    private static UsersController CreateController(FakeUserManagementService service, string userId, string userName)
    {
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, userName),
            new Claim(ClaimTypes.Role, ApplicationRoles.Admin)
        ], "TestAuth");

        var controller = new UsersController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
        controller.TempData = new TempDataDictionary(controller.HttpContext, new NoOpTempDataProvider());
        return controller;
    }

    private sealed class FakeUserManagementService : IUserManagementService
    {
        public FakeUserManagementService(params UserManagementListItem[] users)
        {
            Users = users.ToList();
        }

        public List<UserManagementListItem> Users { get; }
        public bool DeleteCalled { get; private set; }
        public int MonitoringDataRecordCount { get; } = 7;

        public Task<IReadOnlyList<UserManagementListItem>> ListUsersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserManagementListItem>>(Users.ToArray());

        public Task<UserManagementEditModel?> GetUserAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<UserManagementEditModel?>(null);

        public Task<UserManagementDeleteModel?> GetUserForDeleteAsync(string userId, CancellationToken cancellationToken)
        {
            var user = Users.FirstOrDefault(x => x.UserId == userId);
            return Task.FromResult(user is null
                ? null
                : new UserManagementDeleteModel
                {
                    UserId = user.UserId,
                    UserName = user.UserName,
                    Email = user.Email,
                    Roles = [user.Role]
                });
        }

        public Task<IReadOnlyList<UserManagementOption>> GetGroupOptionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserManagementOption>>([]);

        public Task<IReadOnlyList<UserManagementOption>> GetEndpointOptionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserManagementOption>>([]);

        public Task<UserManagementResult> CreateAsync(UserManagementSaveCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new UserManagementResult { Success = true, Found = true, Errors = [] });

        public Task<UserManagementResult> UpdateAsync(UserManagementSaveCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new UserManagementResult { Success = true, Found = true, Errors = [] });

        public Task<UserManagementResult> DeleteAsync(UserManagementDeleteCommand command, CancellationToken cancellationToken)
        {
            DeleteCalled = true;
            var user = Users.FirstOrDefault(x => x.UserId == command.UserId);
            if (user is null)
            {
                return Task.FromResult(new UserManagementResult { Success = false, Found = false, Errors = ["User not found."] });
            }

            if (string.Equals(user.UserId, command.CurrentUserId, StringComparison.Ordinal))
            {
                return Task.FromResult(new UserManagementResult { Success = false, Found = true, Errors = ["You cannot delete your own signed-in account."] });
            }

            if (user.Role == ApplicationRoles.Admin && Users.Count(x => x.Role == ApplicationRoles.Admin) <= 1)
            {
                return Task.FromResult(new UserManagementResult { Success = false, Found = true, Errors = ["You cannot delete the last remaining admin user."] });
            }

            Users.Remove(user);
            return Task.FromResult(new UserManagementResult { Success = true, Found = true, Errors = [] });
        }
    }

    private sealed class NoOpTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
