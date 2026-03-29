using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Models;
using PingMonitor.Web.Models.Identity;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.Services.Security;
using PingMonitor.Web.ViewModels.Admin;

namespace PingMonitor.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Admin)]
[Route("admin/security")]
public sealed class AdminSecurityController : Controller
{
    private const int DefaultLogLimit = 100;
    private readonly ISecurityAuthLogQueryService _securityAuthLogQueryService;
    private readonly ISecuritySettingsService _securitySettingsService;
    private readonly ISecurityIpBlockService _securityIpBlockService;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminSecurityController(
        ISecurityAuthLogQueryService securityAuthLogQueryService,
        ISecuritySettingsService securitySettingsService,
        ISecurityIpBlockService securityIpBlockService,
        UserManager<ApplicationUser> userManager)
    {
        _securityAuthLogQueryService = securityAuthLogQueryService;
        _securitySettingsService = securitySettingsService;
        _securityIpBlockService = securityIpBlockService;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] bool includeSuccessfulUsers = false, [FromQuery] bool includeSuccessfulAgents = false, CancellationToken cancellationToken = default)
    {
        var viewModel = await BuildViewModelAsync(
            includeSuccessfulUsers,
            includeSuccessfulAgents,
            settingsSaved: false,
            blockSaved: false,
            unblockSaved: false,
            settingsFormOverride: null,
            manualIpBlockFormOverride: null,
            cancellationToken: cancellationToken);

        return View("Index", viewModel);
    }

    [HttpPost("settings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSettings(
        [FromForm(Name = "SettingsForm")] SecuritySettingsForm settingsForm,
        [FromForm] bool includeSuccessfulUsers = false,
        [FromForm] bool includeSuccessfulAgents = false,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            var invalidViewModel = await BuildViewModelAsync(
                includeSuccessfulUsers,
                includeSuccessfulAgents,
                settingsSaved: false,
                blockSaved: false,
                unblockSaved: false,
                settingsFormOverride: settingsForm,
                manualIpBlockFormOverride: null,
                cancellationToken);
            return View("Index", invalidViewModel);
        }

        await _securitySettingsService.UpdateAsync(new UpdateSecuritySettingsCommand
        {
            AgentFailedAttemptsBeforeTemporaryIpBlock = settingsForm.AgentFailedAttemptsBeforeTemporaryIpBlock,
            AgentTemporaryIpBlockDurationMinutes = settingsForm.AgentTemporaryIpBlockDurationMinutes,
            AgentFailedAttemptsBeforePermanentIpBlock = settingsForm.AgentFailedAttemptsBeforePermanentIpBlock,
            UserFailedAttemptsBeforeTemporaryIpBlock = settingsForm.UserFailedAttemptsBeforeTemporaryIpBlock,
            UserTemporaryIpBlockDurationMinutes = settingsForm.UserTemporaryIpBlockDurationMinutes,
            UserFailedAttemptsBeforePermanentIpBlock = settingsForm.UserFailedAttemptsBeforePermanentIpBlock,
            UserFailedAttemptsBeforeTemporaryAccountLockout = settingsForm.UserFailedAttemptsBeforeTemporaryAccountLockout,
            UserTemporaryAccountLockoutDurationMinutes = settingsForm.UserTemporaryAccountLockoutDurationMinutes
        }, cancellationToken);

        return RedirectToAction(nameof(Index), new { includeSuccessfulUsers, includeSuccessfulAgents, settingsSaved = true });
    }

    [HttpPost("ip-blocks")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddManualIpBlock(
        [FromForm(Name = "ManualIpBlockForm")] ManualIpBlockForm manualIpBlockForm,
        [FromForm] bool includeSuccessfulUsers = false,
        [FromForm] bool includeSuccessfulAgents = false,
        CancellationToken cancellationToken = default)
    {
        if (!manualIpBlockForm.AuthType.HasValue)
        {
            ModelState.AddModelError($"{nameof(ManualIpBlockForm)}.{nameof(ManualIpBlockForm.AuthType)}", "Auth type is required.");
        }

        if (!string.IsNullOrWhiteSpace(manualIpBlockForm.IpAddress) && !IPAddress.TryParse(manualIpBlockForm.IpAddress.Trim(), out _))
        {
            ModelState.AddModelError($"{nameof(ManualIpBlockForm)}.{nameof(ManualIpBlockForm.IpAddress)}", "IP address must be valid.");
        }

        if (!ModelState.IsValid)
        {
            var invalidViewModel = await BuildViewModelAsync(
                includeSuccessfulUsers,
                includeSuccessfulAgents,
                settingsSaved: false,
                blockSaved: false,
                unblockSaved: false,
                settingsFormOverride: null,
                manualIpBlockFormOverride: manualIpBlockForm,
                cancellationToken);
            return View("Index", invalidViewModel);
        }

        var result = await _securityIpBlockService.AddManualBlockAsync(
            new ManualSecurityIpBlockRequest
            {
                AuthType = manualIpBlockForm.AuthType!.Value,
                IpAddress = manualIpBlockForm.IpAddress,
                Reason = manualIpBlockForm.Reason,
                CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            },
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError($"{nameof(ManualIpBlockForm)}.{nameof(ManualIpBlockForm.IpAddress)}", result.Error ?? "Unable to add IP block.");

            var invalidViewModel = await BuildViewModelAsync(
                includeSuccessfulUsers,
                includeSuccessfulAgents,
                settingsSaved: false,
                blockSaved: false,
                unblockSaved: false,
                settingsFormOverride: null,
                manualIpBlockFormOverride: manualIpBlockForm,
                cancellationToken);
            return View("Index", invalidViewModel);
        }

        return RedirectToAction(nameof(Index), new { includeSuccessfulUsers, includeSuccessfulAgents, blockSaved = true });
    }

    [HttpPost("ip-blocks/{securityIpBlockId}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveIpBlock(
        string securityIpBlockId,
        [FromForm] bool includeSuccessfulUsers = false,
        [FromForm] bool includeSuccessfulAgents = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _securityIpBlockService.RemoveAsync(new RemoveSecurityIpBlockRequest
        {
            SecurityIpBlockId = securityIpBlockId,
            RemovedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
        }, cancellationToken);

        if (!result.Succeeded)
        {
            TempData["SecurityIpBlockRemoveError"] = result.Error ?? "Unable to remove blocked IP.";
            return RedirectToAction(nameof(Index), new { includeSuccessfulUsers, includeSuccessfulAgents });
        }

        return RedirectToAction(nameof(Index), new { includeSuccessfulUsers, includeSuccessfulAgents, unblockSaved = true });
    }

    private async Task<AdminSecurityPageViewModel> BuildViewModelAsync(
        bool includeSuccessfulUsers,
        bool includeSuccessfulAgents,
        bool settingsSaved,
        bool blockSaved,
        bool unblockSaved,
        SecuritySettingsForm? settingsFormOverride,
        ManualIpBlockForm? manualIpBlockFormOverride,
        CancellationToken cancellationToken)
    {
        var userAttempts = await _securityAuthLogQueryService.GetRecentAsync(
            new SecurityAuthLogQuery
            {
                AuthType = SecurityAuthType.User,
                IncludeSuccessful = includeSuccessfulUsers,
                Limit = DefaultLogLimit
            },
            cancellationToken);

        var agentAttempts = await _securityAuthLogQueryService.GetRecentAsync(
            new SecurityAuthLogQuery
            {
                AuthType = SecurityAuthType.Agent,
                IncludeSuccessful = includeSuccessfulAgents,
                Limit = DefaultLogLimit
            },
            cancellationToken);

        var settings = await _securitySettingsService.GetCurrentAsync(cancellationToken);
        var activeBlocks = await _securityIpBlockService.ListActiveAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var lockedOutUsers = await _userManager.Users
            .AsNoTracking()
            .Where(x => x.LockoutEnd != null && x.LockoutEnd > now)
            .OrderBy(x => x.LockoutEnd)
            .Take(100)
            .Select(x => new LockedOutUserListItem
            {
                UserId = x.Id,
                UserName = x.UserName ?? string.Empty,
                Email = x.Email ?? string.Empty,
                LockoutEndUtc = x.LockoutEnd!.Value
            })
            .ToListAsync(cancellationToken);

        return new AdminSecurityPageViewModel
        {
            IncludeSuccessfulUserAttempts = includeSuccessfulUsers,
            IncludeSuccessfulAgentAttempts = includeSuccessfulAgents,
            UserAttempts = userAttempts,
            AgentAttempts = agentAttempts,
            ActiveIpBlocks = activeBlocks,
            LockedOutUsers = lockedOutUsers,
            SettingsSaved = settingsSaved || string.Equals(Request.Query["settingsSaved"], "true", StringComparison.OrdinalIgnoreCase),
            BlockSaved = blockSaved || string.Equals(Request.Query["blockSaved"], "true", StringComparison.OrdinalIgnoreCase),
            UnblockSaved = unblockSaved || string.Equals(Request.Query["unblockSaved"], "true", StringComparison.OrdinalIgnoreCase),
            SettingsForm = settingsFormOverride ?? new SecuritySettingsForm
            {
                AgentFailedAttemptsBeforeTemporaryIpBlock = settings.AgentFailedAttemptsBeforeTemporaryIpBlock,
                AgentTemporaryIpBlockDurationMinutes = settings.AgentTemporaryIpBlockDurationMinutes,
                AgentFailedAttemptsBeforePermanentIpBlock = settings.AgentFailedAttemptsBeforePermanentIpBlock,
                UserFailedAttemptsBeforeTemporaryIpBlock = settings.UserFailedAttemptsBeforeTemporaryIpBlock,
                UserTemporaryIpBlockDurationMinutes = settings.UserTemporaryIpBlockDurationMinutes,
                UserFailedAttemptsBeforePermanentIpBlock = settings.UserFailedAttemptsBeforePermanentIpBlock,
                UserFailedAttemptsBeforeTemporaryAccountLockout = settings.UserFailedAttemptsBeforeTemporaryAccountLockout,
                UserTemporaryAccountLockoutDurationMinutes = settings.UserTemporaryAccountLockoutDurationMinutes,
                UpdatedAtUtc = settings.UpdatedAtUtc
            },
            ManualIpBlockForm = manualIpBlockFormOverride ?? new ManualIpBlockForm()
        };
    }
}
