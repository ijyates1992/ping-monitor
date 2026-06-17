using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Services.AiScheduledTasks;
using PingMonitor.Web.ViewModels.AiScheduledTasks;

namespace PingMonitor.Web.Controllers;

[Authorize]
[Route("ai-assistant/scheduled-tasks")]
public sealed class AiScheduledTasksController : Controller
{
    private readonly IAiScheduledTaskService _service;
    private readonly PingMonitorDbContext _dbContext;
    public AiScheduledTasksController(IAiScheduledTaskService service, PingMonitorDbContext dbContext) { _service = service; _dbContext = dbContext; }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) => View(await BuildPageAsync(new AiScheduledTaskFormViewModel(), cancellationToken));

    [HttpGet("edit/{taskId}")]
    public async Task<IActionResult> Edit(string taskId, CancellationToken cancellationToken)
    {
        var dto = await _service.GetForUserAsync(UserId(), taskId, cancellationToken);
        if (dto is null) { TempData["ErrorMessage"] = "Scheduled AI task was not found."; return RedirectToAction(nameof(Index)); }
        return View("Index", await BuildPageAsync(new AiScheduledTaskFormViewModel { AiScheduledTaskId = dto.AiScheduledTaskId, Name = dto.Name, Prompt = dto.Prompt, Enabled = dto.Enabled, ScheduleKind = dto.ScheduleKind, RunOnceAtUtc = dto.RunOnceAtUtc, TimeOfDayLocal = dto.TimeOfDayLocal, DayOfWeek = dto.DayOfWeek, DayOfMonth = dto.DayOfMonth, TimeZoneId = dto.TimeZoneId, DeliveryTarget = dto.DeliveryTarget }, cancellationToken));
    }

    [HttpPost("save"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromForm] AiScheduledTaskFormViewModel form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View("Index", await BuildPageAsync(form, cancellationToken));
        var result = await _service.SaveAsync(new SaveAiScheduledTaskCommand { AiScheduledTaskId = form.AiScheduledTaskId, OwnerUserId = UserId(), Name = form.Name, Prompt = form.Prompt, Enabled = form.Enabled, ScheduleKind = form.ScheduleKind, RunOnceAtUtc = form.RunOnceAtUtc, TimeOfDayLocal = form.TimeOfDayLocal, DayOfWeek = form.DayOfWeek, DayOfMonth = form.DayOfMonth, TimeZoneId = form.TimeZoneId, DeliveryTarget = form.DeliveryTarget }, cancellationToken);
        if (!result.Succeeded) { var page = await BuildPageAsync(form, cancellationToken); page.ErrorMessage = result.ErrorMessage; return View("Index", page); }
        TempData["StatusMessage"] = "Scheduled AI task saved."; return RedirectToAction(nameof(Index));
    }

    [HttpPost("delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete([FromForm] string taskId, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteAsync(UserId(), taskId, cancellationToken);
        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded ? "Scheduled AI task deleted." : result.ErrorMessage;
        return RedirectToAction(nameof(Index));
    }

    private async Task<AiScheduledTasksPageViewModel> BuildPageAsync(AiScheduledTaskFormViewModel form, CancellationToken cancellationToken)
    {
        var userId = UserId();
        return new AiScheduledTasksPageViewModel { Tasks = await _service.ListForUserAsync(userId, cancellationToken), HasLinkedTelegramAccount = await _dbContext.TelegramAccounts.AsNoTracking().AnyAsync(x => x.UserId == userId && x.Verified && x.IsActive, cancellationToken), StatusMessage = TempData["StatusMessage"] as string, ErrorMessage = TempData["ErrorMessage"] as string, Form = form };
    }
    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
}
