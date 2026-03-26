using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services.Groups;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.ViewModels.Groups;

namespace PingMonitor.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Admin)]
[Route("groups")]
public sealed class GroupsController : Controller
{
    private const string StatusMessageKey = "Groups.StatusMessage";
    private readonly IGroupManagementService _groupManagementService;

    public GroupsController(IGroupManagementService groupManagementService)
    {
        _groupManagementService = groupManagementService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = await _groupManagementService.GetManagePageAsync(cancellationToken);
        model.StatusMessage = TempData[StatusMessageKey] as string;
        return View("Index", model);
    }

    [HttpGet("new")]
    public IActionResult New()
    {
        return View("New", new GroupEditPageViewModel());
    }

    [HttpPost("new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New([FromForm] GroupEditPageViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("New", model);
        }

        var result = await _groupManagementService.CreateAsync(new GroupUpsertCommand
        {
            Name = model.Name,
            Description = model.Description
        }, cancellationToken);

        if (!result.Success)
        {
            foreach (var validationError in result.ValidationErrors)
            {
                ModelState.AddModelError(nameof(GroupEditPageViewModel.Name), validationError);
            }

            return View("New", model);
        }

        TempData[StatusMessageKey] = "Group created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/edit")]
    public async Task<IActionResult> Edit([FromRoute] string id, CancellationToken cancellationToken)
    {
        var model = await _groupManagementService.GetEditPageAsync(id, cancellationToken);
        return model is null ? NotFound() : View("Edit", model);
    }

    [HttpPost("{id}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit([FromRoute] string id, [FromForm] GroupEditPageViewModel model, CancellationToken cancellationToken)
    {
        model.GroupId = id;
        if (!ModelState.IsValid)
        {
            return View("Edit", model);
        }

        var result = await _groupManagementService.UpdateAsync(new GroupUpsertCommand
        {
            GroupId = id,
            Name = model.Name,
            Description = model.Description
        }, cancellationToken);

        if (!result.Found)
        {
            return NotFound();
        }

        if (!result.Success)
        {
            foreach (var validationError in result.ValidationErrors)
            {
                ModelState.AddModelError(nameof(GroupEditPageViewModel.Name), validationError);
            }

            return View("Edit", model);
        }

        TempData[StatusMessageKey] = "Group updated.";
        return RedirectToAction(nameof(Index));
    }
}
