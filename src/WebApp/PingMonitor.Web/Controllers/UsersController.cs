using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.ViewModels.Users;

namespace PingMonitor.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Admin)]
[Route("users")]
public sealed class UsersController : Controller
{
    private const string DeleteConfirmationText = "DELETE";
    private const string StatusMessageKey = "Users.StatusMessage";
    private readonly IUserManagementService _userManagementService;

    public UsersController(IUserManagementService userManagementService)
    {
        _userManagementService = userManagementService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var users = await _userManagementService.ListUsersAsync(cancellationToken);
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var currentUserName = User.Identity?.Name;
        var adminCount = users.Count(x => string.Equals(x.Role, ApplicationRoles.Admin, StringComparison.Ordinal));

        return View("Index", new ManageUsersPageViewModel
        {
            StatusMessage = TempData[StatusMessageKey] as string,
            Users = users.Select(x => new UserRowViewModel
            {
                UserId = x.UserId,
                UserName = x.UserName,
                Email = x.Email,
                Role = x.Role,
                Enabled = x.Enabled,
                CanDelete = CanDeleteUser(x, currentUserId, currentUserName, adminCount),
                DeleteBlockedReason = GetDeleteBlockedReason(x, currentUserId, currentUserName, adminCount)
            }).ToArray()
        });
    }

    [HttpGet("new")]
    public async Task<IActionResult> New(CancellationToken cancellationToken)
    {
        return View("New", await BuildEditModelAsync(new UserEditPageViewModel(), cancellationToken));
    }

    [HttpPost("new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New([FromForm] UserEditPageViewModel model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError(nameof(UserEditPageViewModel.Password), "Password is required.");
        }

        if (!ModelState.IsValid)
        {
            return View("New", await BuildEditModelAsync(model, cancellationToken));
        }

        var result = await _userManagementService.CreateAsync(new UserManagementSaveCommand
        {
            UserName = model.UserName,
            Email = model.Email,
            Password = model.Password,
            Role = model.Role,
            Enabled = model.Enabled,
            GroupIds = model.GroupIds,
            EndpointIds = model.EndpointIds
        }, cancellationToken);

        if (!result.Success)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return View("New", await BuildEditModelAsync(model, cancellationToken));
        }

        TempData[StatusMessageKey] = "User created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/edit")]
    public async Task<IActionResult> Edit([FromRoute] string id, CancellationToken cancellationToken)
    {
        var user = await _userManagementService.GetUserAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        return View("Edit", await BuildEditModelAsync(new UserEditPageViewModel
        {
            UserId = user.UserId,
            UserName = user.UserName,
            Email = user.Email,
            Role = user.Role,
            Enabled = user.Enabled,
            GroupIds = user.SelectedGroupIds.ToList(),
            EndpointIds = user.SelectedEndpointIds.ToList()
        }, cancellationToken));
    }

    [HttpPost("{id}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit([FromRoute] string id, [FromForm] UserEditPageViewModel model, CancellationToken cancellationToken)
    {
        model.UserId = id;
        if (!ModelState.IsValid)
        {
            return View("Edit", await BuildEditModelAsync(model, cancellationToken));
        }

        var result = await _userManagementService.UpdateAsync(new UserManagementSaveCommand
        {
            UserId = id,
            UserName = model.UserName,
            Email = model.Email,
            Role = model.Role,
            Enabled = model.Enabled,
            GroupIds = model.GroupIds,
            EndpointIds = model.EndpointIds
        }, cancellationToken);

        if (!result.Success)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return View("Edit", await BuildEditModelAsync(model, cancellationToken));
        }

        TempData[StatusMessageKey] = "User updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/delete")]
    public async Task<IActionResult> Delete([FromRoute] string id, CancellationToken cancellationToken)
    {
        var user = await _userManagementService.GetUserForDeleteAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        var model = await BuildDeleteModelAsync(user, confirmationText: string.Empty, cancellationToken);
        return View("Delete", model);
    }

    [HttpPost("{id}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete([FromRoute] string id, [FromForm] UserDeletePageViewModel model, CancellationToken cancellationToken)
    {
        var user = await _userManagementService.GetUserForDeleteAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        var viewModel = await BuildDeleteModelAsync(user, model.ConfirmationText, cancellationToken);
        if (!viewModel.CanDelete)
        {
            ModelState.AddModelError(string.Empty, viewModel.DeleteBlockedReason ?? "This user cannot be deleted.");
            return View("Delete", viewModel);
        }

        if (!string.Equals(model.ConfirmationText, DeleteConfirmationText, StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(UserDeletePageViewModel.ConfirmationText), "Type DELETE to confirm user deletion.");
            return View("Delete", viewModel);
        }

        var result = await _userManagementService.DeleteAsync(new UserManagementDeleteCommand
        {
            UserId = id,
            CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            CurrentUserName = User.Identity?.Name
        }, cancellationToken);

        if (!result.Found)
        {
            return NotFound();
        }

        if (!result.Success)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return View("Delete", viewModel);
        }

        TempData[StatusMessageKey] = $"User '{user.UserName}' deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<UserEditPageViewModel> BuildEditModelAsync(UserEditPageViewModel model, CancellationToken cancellationToken)
    {
        model.AvailableGroups = (await _userManagementService.GetGroupOptionsAsync(cancellationToken))
            .Select(x => new UserOptionViewModel { Id = x.Id, Name = x.Name })
            .ToArray();
        model.AvailableEndpoints = (await _userManagementService.GetEndpointOptionsAsync(cancellationToken))
            .Select(x => new UserOptionViewModel { Id = x.Id, Name = x.Name })
            .ToArray();
        return model;
    }

    private async Task<UserDeletePageViewModel> BuildDeleteModelAsync(
        UserManagementDeleteModel user,
        string? confirmationText,
        CancellationToken cancellationToken)
    {
        var users = await _userManagementService.ListUsersAsync(cancellationToken);
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var currentUserName = User.Identity?.Name;
        var adminCount = users.Count(x => string.Equals(x.Role, ApplicationRoles.Admin, StringComparison.Ordinal));
        var targetListItem = users.FirstOrDefault(x => string.Equals(x.UserId, user.UserId, StringComparison.Ordinal));
        var target = targetListItem ?? new UserManagementListItem
        {
            UserId = user.UserId,
            UserName = user.UserName,
            Email = user.Email,
            Role = user.Roles.Contains(ApplicationRoles.Admin, StringComparer.Ordinal) ? ApplicationRoles.Admin : ApplicationRoles.User,
            Enabled = true
        };

        return new UserDeletePageViewModel
        {
            UserId = user.UserId,
            UserName = user.UserName,
            Email = user.Email,
            Roles = user.Roles,
            ConfirmationText = confirmationText ?? string.Empty,
            CanDelete = CanDeleteUser(target, currentUserId, currentUserName, adminCount),
            DeleteBlockedReason = GetDeleteBlockedReason(target, currentUserId, currentUserName, adminCount)
        };
    }

    private static bool CanDeleteUser(UserManagementListItem user, string? currentUserId, string? currentUserName, int adminCount) =>
        GetDeleteBlockedReason(user, currentUserId, currentUserName, adminCount) is null;

    private static string? GetDeleteBlockedReason(UserManagementListItem user, string? currentUserId, string? currentUserName, int adminCount)
    {
        if ((!string.IsNullOrWhiteSpace(currentUserId)
                && string.Equals(user.UserId, currentUserId, StringComparison.Ordinal))
            || (!string.IsNullOrWhiteSpace(currentUserName)
                && string.Equals(user.UserName, currentUserName, StringComparison.OrdinalIgnoreCase)))
        {
            return "You cannot delete your own signed-in account.";
        }

        if (string.Equals(user.Role, ApplicationRoles.Admin, StringComparison.Ordinal) && adminCount <= 1)
        {
            return "You cannot delete the last remaining admin user.";
        }

        return null;
    }
}
