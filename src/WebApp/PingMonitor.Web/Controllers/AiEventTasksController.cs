using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.AiEventTasks;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.ViewModels.AiEventTasks;
namespace PingMonitor.Web.Controllers;
[Authorize]
[Route("ai-assistant/event-tasks")]
public sealed class AiEventTasksController:Controller
{
 private readonly IAiEventTriggeredTaskService _service; private readonly PingMonitorDbContext _db; private readonly IUserAccessScopeService _access;
 public AiEventTasksController(IAiEventTriggeredTaskService service,PingMonitorDbContext db,IUserAccessScopeService access){_service=service;_db=db;_access=access;}
 [HttpGet("")] public async Task<IActionResult> Index(CancellationToken ct)=>View(await BuildPageAsync(new AiEventTaskFormViewModel(),ct));
 [HttpGet("edit/{taskId}")] public async Task<IActionResult> Edit(string taskId,CancellationToken ct){var dto=await _service.GetForUserAsync(UserId(),taskId,ct); if(dto is null){TempData["ErrorMessage"]="Event-triggered AI task was not found."; return RedirectToAction(nameof(Index));} return View("Index",await BuildPageAsync(new AiEventTaskFormViewModel{AiEventTriggeredTaskId=dto.AiEventTriggeredTaskId,Name=dto.Name,Prompt=dto.Prompt,Enabled=dto.Enabled,TriggerType=dto.TriggerType,EndpointTargetStates=dto.EndpointTargetStates.ToList(),AgentTargetStates=dto.AgentTargetStates.ToList(),ScopeMode=dto.ScopeMode,ScopeSelectionIds=dto.ScopeSelectionIds.ToList(),RateLimitValue=dto.RateLimitValue,RateLimitUnit=dto.RateLimitUnit,DeliveryTarget=dto.DeliveryTarget},ct));}
 [HttpPost("save"),ValidateAntiForgeryToken] public async Task<IActionResult> Save([FromForm] AiEventTaskFormViewModel form,CancellationToken ct){if(!ModelState.IsValid)return View("Index",await BuildPageAsync(form,ct)); var r=await _service.SaveAsync(new SaveAiEventTriggeredTaskCommand{AiEventTriggeredTaskId=form.AiEventTriggeredTaskId,OwnerUserId=UserId(),Name=form.Name,Prompt=form.Prompt,Enabled=form.Enabled,TriggerType=form.TriggerType,EndpointTargetStates=form.EndpointTargetStates,AgentTargetStates=form.AgentTargetStates,ScopeMode=form.ScopeMode,ScopeSelectionIds=form.ScopeSelectionIds,RateLimitValue=form.RateLimitValue,RateLimitUnit=form.RateLimitUnit,DeliveryTarget=form.DeliveryTarget},ct); if(!r.Succeeded){var page=await BuildPageAsync(form,ct); page.ErrorMessage=r.ErrorMessage; return View("Index",page);} TempData["StatusMessage"]="Event-triggered AI task saved."; return RedirectToAction(nameof(Index));}
 [HttpPost("delete"),ValidateAntiForgeryToken] public async Task<IActionResult> Delete([FromForm]string taskId,CancellationToken ct){var r=await _service.DeleteAsync(UserId(),taskId,ct); TempData[r.Succeeded?"StatusMessage":"ErrorMessage"]=r.Succeeded?"Event-triggered AI task deleted.":r.ErrorMessage; return RedirectToAction(nameof(Index));}
 private async Task<AiEventTasksPageViewModel> BuildPageAsync(AiEventTaskFormViewModel form,CancellationToken ct){var uid=UserId(); var visible=await _access.GetVisibleEndpointIdsAsync(User,ct); var endpoints=await _db.Endpoints.AsNoTracking().Where(x=>visible.Contains(x.EndpointId)).OrderBy(x=>x.Name).Select(x=>new SelectListItem(x.Name,x.EndpointId)).ToListAsync(ct); var groups=await _db.Groups.AsNoTracking().OrderBy(x=>x.Name).Select(x=>new SelectListItem(x.Name,x.GroupId)).ToListAsync(ct); var agents=await _db.Agents.AsNoTracking().OrderBy(x=>x.Name??x.InstanceId).Select(x=>new SelectListItem(x.Name??x.InstanceId,x.AgentId)).ToListAsync(ct); return new AiEventTasksPageViewModel{Tasks=await _service.ListForUserAsync(uid,ct),HasLinkedTelegramAccount=await _db.TelegramAccounts.AsNoTracking().AnyAsync(x=>x.UserId==uid&&x.Verified&&x.IsActive,ct),StatusMessage=TempData["StatusMessage"] as string,ErrorMessage=TempData["ErrorMessage"] as string,Form=form,Endpoints=endpoints,Groups=groups,Agents=agents};}
 private string UserId()=>User.FindFirstValue(ClaimTypes.NameIdentifier)??string.Empty;
}
