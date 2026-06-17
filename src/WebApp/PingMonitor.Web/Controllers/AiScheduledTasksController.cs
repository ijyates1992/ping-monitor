using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.AiScheduledTasks;
using PingMonitor.Web.Services.Time;
using PingMonitor.Web.ViewModels.AiScheduledTasks;

namespace PingMonitor.Web.Controllers;

[Authorize]
[Route("ai-assistant/scheduled-tasks")]
public sealed class AiScheduledTasksController : Controller
{
    private readonly IAiScheduledTaskService _service;
    private readonly PingMonitorDbContext _dbContext;
    private readonly IUserTimeZoneService _timeZoneService;
    public AiScheduledTasksController(IAiScheduledTaskService service, PingMonitorDbContext dbContext, IUserTimeZoneService timeZoneService) { _service = service; _dbContext = dbContext; _timeZoneService = timeZoneService; }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var tz = await DefaultTimeZoneAsync(cancellationToken);
        return View(await BuildPageAsync(new AiScheduledTaskFormViewModel { TimeZoneId = tz, FirstRunAtUtc = DateTimeOffset.UtcNow.AddHours(1) }, cancellationToken));
    }

    [HttpGet("edit/{taskId}")]
    public async Task<IActionResult> Edit(string taskId, CancellationToken cancellationToken)
    {
        var dto = await _service.GetForUserAsync(UserId(), taskId, cancellationToken);
        if (dto is null) { TempData["ErrorMessage"] = "Scheduled AI task was not found."; return RedirectToAction(nameof(Index)); }
        return View("Index", await BuildPageAsync(new AiScheduledTaskFormViewModel { AiScheduledTaskId = dto.AiScheduledTaskId, Name = dto.Name, Prompt = dto.Prompt, Enabled = dto.Enabled, FirstRunAtUtc = dto.FirstRunAtUtc, RepeatEnabled = dto.RepeatEnabled, RepeatEvery = dto.RepeatEvery ?? 1, RepeatUnit = dto.RepeatUnit ?? AiScheduledTaskRepeatUnit.Days, MissedRunPolicy = dto.MissedRunPolicy, TimeZoneId = _timeZoneService.IsSupportedTimeZoneId(dto.TimeZoneId) ? dto.TimeZoneId : await DefaultTimeZoneAsync(cancellationToken), DeliveryTarget = dto.DeliveryTarget }, cancellationToken));
    }

    [HttpPost("save"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromForm] AiScheduledTaskFormViewModel form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View("Index", await BuildPageAsync(form, cancellationToken));
        var result = await _service.SaveAsync(new SaveAiScheduledTaskCommand { AiScheduledTaskId = form.AiScheduledTaskId, OwnerUserId = UserId(), Name = form.Name, Prompt = form.Prompt, Enabled = form.Enabled, FirstRunAtUtc = form.FirstRunAtUtc, RepeatEnabled = form.RepeatEnabled, RepeatEvery = form.RepeatEvery, RepeatUnit = form.RepeatUnit, MissedRunPolicy = form.MissedRunPolicy, TimeZoneId = form.TimeZoneId, DeliveryTarget = form.DeliveryTarget }, cancellationToken);
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
        if (!_timeZoneService.IsSupportedTimeZoneId(form.TimeZoneId)) form.TimeZoneId = await DefaultTimeZoneAsync(cancellationToken);
        return new AiScheduledTasksPageViewModel { Tasks = await _service.ListForUserAsync(userId, cancellationToken), HasLinkedTelegramAccount = await _dbContext.TelegramAccounts.AsNoTracking().AnyAsync(x => x.UserId == userId && x.Verified && x.IsActive, cancellationToken), StatusMessage = TempData["StatusMessage"] as string, ErrorMessage = TempData["ErrorMessage"] as string, TimeZoneOptions = _timeZoneService.GetSelectableTimeZoneOptions(), Form = form };
    }
    private async Task<string> DefaultTimeZoneAsync(CancellationToken cancellationToken)
    {
        var id = await _timeZoneService.GetCurrentUserTimeZoneIdAsync(cancellationToken);
        if (_timeZoneService.IsSupportedTimeZoneId(id)) return id;
        return _timeZoneService.IsSupportedTimeZoneId("Europe/London") ? "Europe/London" : "UTC";
    }
    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
}
