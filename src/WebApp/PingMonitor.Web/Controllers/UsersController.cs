using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.ViewModels.Users;

namespace PingMonitor.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Admin)]
[Route("users")]
public sealed class UsersController : Controller
{
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
        return View("Index", new ManageUsersPageViewModel
        {
            StatusMessage = TempData[StatusMessageKey] as string,
            Users = users.Select(x => new UserRowViewModel
            {
                UserId = x.UserId,
                UserName = x.UserName,
                Email = x.Email,
                Role = x.Role,
                Enabled = x.Enabled
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
}
