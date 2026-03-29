using System.Globalization;
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
    private const string UnblockConfirmationKeyword = "UNBLOCK";
    private const string UnlockConfirmationKeyword = "UNLOCK";

    private readonly ISecurityAuthLogQueryService _securityAuthLogQueryService;
    private readonly ISecuritySettingsService _securitySettingsService;
    private readonly ISecurityIpBlockService _securityIpBlockService;
    private readonly ISecurityOperatorActionService _securityOperatorActionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminSecurityController(
        ISecurityAuthLogQueryService securityAuthLogQueryService,
        ISecuritySettingsService securitySettingsService,
        ISecurityIpBlockService securityIpBlockService,
        ISecurityOperatorActionService securityOperatorActionService,
        UserManager<ApplicationUser> userManager)
    {
        _securityAuthLogQueryService = securityAuthLogQueryService;
        _securitySettingsService = securitySettingsService;
        _securityIpBlockService = securityIpBlockService;
        _securityOperatorActionService = securityOperatorActionService;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] bool includeSuccessfulUsers = false,
        [FromQuery] bool includeSuccessfulAgents = false,
        [FromQuery] string? searchText = null,
        [FromQuery] string? fromUtc = null,
        [FromQuery] string? toUtc = null,
        [FromQuery(Name = "LogFilterForm.IncludeSuccessfulUsers")] bool? includeSuccessfulUsersFilter = null,
        [FromQuery(Name = "LogFilterForm.IncludeSuccessfulAgents")] bool? includeSuccessfulAgentsFilter = null,
        [FromQuery(Name = "LogFilterForm.SearchText")] string? searchTextFilter = null,
        [FromQuery(Name = "LogFilterForm.FromUtc")] string? fromUtcFilter = null,
        [FromQuery(Name = "LogFilterForm.ToUtc")] string? toUtcFilter = null,
        CancellationToken cancellationToken = default)
    {
        includeSuccessfulUsers = includeSuccessfulUsersFilter ?? includeSuccessfulUsers;
        includeSuccessfulAgents = includeSuccessfulAgentsFilter ?? includeSuccessfulAgents;
        searchText = searchTextFilter ?? searchText;
        fromUtc = fromUtcFilter ?? fromUtc;
        toUtc = toUtcFilter ?? toUtc;

        var logFilterForm = new SecurityAuthLogFilterForm
        {
            IncludeSuccessfulUsers = includeSuccessfulUsers,
            IncludeSuccessfulAgents = includeSuccessfulAgents,
            SearchText = searchText,
            FromUtc = fromUtc,
            ToUtc = toUtc
        };

        var viewModel = await BuildViewModelAsync(
            logFilterForm,
            settingsSaved: false,
            blockSaved: false,
            unblockSaved: false,
            unlockSaved: false,
            settingsFormOverride: null,
            manualIpBlockFormOverride: null,
            cancellationToken: cancellationToken);

        return View("Index", viewModel);
    }

    [HttpPost("settings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSettings(
        [FromForm(Name = "SettingsForm")] SecuritySettingsForm settingsForm,
        [FromForm(Name = "LogFilterForm")] SecurityAuthLogFilterForm logFilterForm,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            var invalidViewModel = await BuildViewModelAsync(
                logFilterForm,
                settingsSaved: false,
                blockSaved: false,
                unblockSaved: false,
                unlockSaved: false,
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

        return RedirectToAction(nameof(Index), BuildRouteValues(logFilterForm, settingsSaved: true));
    }

    [HttpPost("ip-blocks")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddManualIpBlock(
        [FromForm(Name = "ManualIpBlockForm")] ManualIpBlockForm manualIpBlockForm,
        [FromForm(Name = "LogFilterForm")] SecurityAuthLogFilterForm logFilterForm,
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
                logFilterForm,
                settingsSaved: false,
                blockSaved: false,
                unblockSaved: false,
                unlockSaved: false,
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
                logFilterForm,
                settingsSaved: false,
                blockSaved: false,
                unblockSaved: false,
                unlockSaved: false,
                settingsFormOverride: null,
                manualIpBlockFormOverride: manualIpBlockForm,
                cancellationToken);
            return View("Index", invalidViewModel);
        }

        return RedirectToAction(nameof(Index), BuildRouteValues(logFilterForm, blockSaved: true));
    }

    [HttpGet("ip-blocks/{securityIpBlockId}/remove")]
    public async Task<IActionResult> RemoveIpBlock(
        string securityIpBlockId,
        [FromQuery(Name = "LogFilterForm.IncludeSuccessfulUsers")] bool includeSuccessfulUsers = false,
        [FromQuery(Name = "LogFilterForm.IncludeSuccessfulAgents")] bool includeSuccessfulAgents = false,
        [FromQuery(Name = "LogFilterForm.SearchText")] string? searchText = null,
        [FromQuery(Name = "LogFilterForm.FromUtc")] string? fromUtc = null,
        [FromQuery(Name = "LogFilterForm.ToUtc")] string? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var details = await _securityOperatorActionService.GetActiveIpBlockAsync(securityIpBlockId, cancellationToken);
        if (details is null)
        {
            TempData["SecurityIpBlockRemoveError"] = "Blocked IP record is not active or no longer exists.";
            return RedirectToAction(nameof(Index), BuildRouteValues(new SecurityAuthLogFilterForm
            {
                IncludeSuccessfulUsers = includeSuccessfulUsers,
                IncludeSuccessfulAgents = includeSuccessfulAgents,
                SearchText = searchText,
                FromUtc = fromUtc,
                ToUtc = toUtc
            }));
        }

        return View("ConfirmUnblockIp", new ConfirmUnblockIpViewModel
        {
            IncludeSuccessfulUsers = includeSuccessfulUsers,
            IncludeSuccessfulAgents = includeSuccessfulAgents,
            SecurityIpBlockId = details.SecurityIpBlockId,
            AuthType = details.AuthType,
            IpAddress = details.IpAddress,
            BlockType = details.BlockType,
            BlockedAtUtc = details.BlockedAtUtc,
            ExpiresAtUtc = details.ExpiresAtUtc,
            RequiredConfirmationText = UnblockConfirmationKeyword
        });
    }

    [HttpPost("ip-blocks/{securityIpBlockId}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveIpBlockConfirmed(
        string securityIpBlockId,
        [FromForm] ConfirmUnblockIpViewModel model,
        CancellationToken cancellationToken = default)
    {
        var details = await _securityOperatorActionService.GetActiveIpBlockAsync(securityIpBlockId, cancellationToken);
        if (details is null)
        {
            TempData["SecurityIpBlockRemoveError"] = "Blocked IP record is not active or no longer exists.";
            return RedirectToAction(nameof(Index), new { model.IncludeSuccessfulUsers, model.IncludeSuccessfulAgents });
        }

        var viewModel = BuildConfirmUnblockViewModel(details, model.IncludeSuccessfulUsers, model.IncludeSuccessfulAgents, model.ConfirmationText);
        if (!string.Equals(model.ConfirmationText?.Trim(), UnblockConfirmationKeyword, StringComparison.Ordinal))
        {
            viewModel.ErrorMessage = $"Type {UnblockConfirmationKeyword} to confirm removing this active block.";
            return View("ConfirmUnblockIp", viewModel);
        }

        var result = await _securityOperatorActionService.RemoveIpBlockAsync(new RemoveSecurityIpBlockRequest
        {
            SecurityIpBlockId = securityIpBlockId,
            RemovedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
        }, cancellationToken);

        if (!result.Succeeded)
        {
            TempData["SecurityIpBlockRemoveError"] = result.Error ?? "Unable to remove blocked IP.";
            return RedirectToAction(nameof(Index), new { model.IncludeSuccessfulUsers, model.IncludeSuccessfulAgents });
        }

        return RedirectToAction(nameof(Index), new { model.IncludeSuccessfulUsers, model.IncludeSuccessfulAgents, unblockSaved = true });
    }

    [HttpGet("users/{userId}/unlock")]
    public async Task<IActionResult> UnlockUser(
        string userId,
        [FromQuery] bool includeSuccessfulUsers = false,
        [FromQuery] bool includeSuccessfulAgents = false,
        CancellationToken cancellationToken = default)
    {
        var details = await _securityOperatorActionService.GetLockedOutUserAsync(userId, cancellationToken);
        if (details is null)
        {
            TempData["SecurityUserUnlockError"] = "User is not currently locked out or no longer exists.";
            return RedirectToAction(nameof(Index), new { includeSuccessfulUsers, includeSuccessfulAgents });
        }

        return View("ConfirmUnlockUser", new ConfirmUnlockUserViewModel
        {
            IncludeSuccessfulUsers = includeSuccessfulUsers,
            IncludeSuccessfulAgents = includeSuccessfulAgents,
            UserId = details.UserId,
            UserName = details.UserName,
            Email = details.Email,
            LockoutEndUtc = details.LockoutEndUtc,
            RequiredConfirmationText = UnlockConfirmationKeyword
        });
    }

    [HttpPost("users/{userId}/unlock")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnlockUserConfirmed(
        string userId,
        [FromForm] ConfirmUnlockUserViewModel model,
        CancellationToken cancellationToken = default)
    {
        var details = await _securityOperatorActionService.GetLockedOutUserAsync(userId, cancellationToken);
        if (details is null)
        {
            TempData["SecurityUserUnlockError"] = "User is not currently locked out or no longer exists.";
            return RedirectToAction(nameof(Index), new { model.IncludeSuccessfulUsers, model.IncludeSuccessfulAgents });
        }

        var viewModel = BuildConfirmUnlockViewModel(details, model.IncludeSuccessfulUsers, model.IncludeSuccessfulAgents, model.ConfirmationText);
        if (!string.Equals(model.ConfirmationText?.Trim(), UnlockConfirmationKeyword, StringComparison.Ordinal))
        {
            viewModel.ErrorMessage = $"Type {UnlockConfirmationKeyword} to confirm unlocking this user.";
            return View("ConfirmUnlockUser", viewModel);
        }

        var result = await _securityOperatorActionService.UnlockUserAsync(new UnlockSecurityUserRequest
        {
            UserId = userId,
            OperatorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
        }, cancellationToken);

        if (!result.Succeeded)
        {
            TempData["SecurityUserUnlockError"] = result.Error ?? "Unable to unlock user.";
            return RedirectToAction(nameof(Index), new { model.IncludeSuccessfulUsers, model.IncludeSuccessfulAgents });
        }

        return RedirectToAction(nameof(Index), new { model.IncludeSuccessfulUsers, model.IncludeSuccessfulAgents, unlockSaved = true });
    }

    private async Task<AdminSecurityPageViewModel> BuildViewModelAsync(
        SecurityAuthLogFilterForm logFilterForm,
        bool settingsSaved,
        bool blockSaved,
        bool unblockSaved,
        bool unlockSaved,
        SecuritySettingsForm? settingsFormOverride,
        ManualIpBlockForm? manualIpBlockFormOverride,
        CancellationToken cancellationToken)
    {
        var fromUtc = ParseUtcValue(logFilterForm.FromUtc, nameof(SecurityAuthLogFilterForm.FromUtc));
        var toUtc = ParseUtcValue(logFilterForm.ToUtc, nameof(SecurityAuthLogFilterForm.ToUtc));

        if (fromUtc.HasValue && toUtc.HasValue && fromUtc > toUtc)
        {
            ModelState.AddModelError("LogFilterForm.ToUtc", "To date/time (UTC) must be greater than or equal to From date/time (UTC).");
        }

        var userAttempts = await _securityAuthLogQueryService.GetRecentAsync(
            new SecurityAuthLogQuery
            {
                AuthType = SecurityAuthType.User,
                IncludeSuccessful = logFilterForm.IncludeSuccessfulUsers,
                SearchText = logFilterForm.SearchText,
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Limit = DefaultLogLimit
            },
            cancellationToken);

        var agentAttempts = await _securityAuthLogQueryService.GetRecentAsync(
            new SecurityAuthLogQuery
            {
                AuthType = SecurityAuthType.Agent,
                IncludeSuccessful = logFilterForm.IncludeSuccessfulAgents,
                SearchText = logFilterForm.SearchText,
                FromUtc = fromUtc,
                ToUtc = toUtc,
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
            LogFilterForm = logFilterForm,
            UserAttempts = userAttempts,
            AgentAttempts = agentAttempts,
            ActiveIpBlocks = activeBlocks,
            LockedOutUsers = lockedOutUsers,
            SettingsSaved = settingsSaved || string.Equals(Request.Query["settingsSaved"], "true", StringComparison.OrdinalIgnoreCase),
            BlockSaved = blockSaved || string.Equals(Request.Query["blockSaved"], "true", StringComparison.OrdinalIgnoreCase),
            UnblockSaved = unblockSaved || string.Equals(Request.Query["unblockSaved"], "true", StringComparison.OrdinalIgnoreCase),
            UnlockSaved = unlockSaved || string.Equals(Request.Query["unlockSaved"], "true", StringComparison.OrdinalIgnoreCase),
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

    private DateTimeOffset? ParseUtcValue(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedOffset))
        {
            return parsedOffset;
        }

        if (DateTime.TryParseExact(
                value,
                ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDateTime))
        {
            return new DateTimeOffset(DateTime.SpecifyKind(parsedDateTime, DateTimeKind.Utc));
        }

        ModelState.AddModelError($"LogFilterForm.{fieldName}", "Enter a valid UTC date/time value.");
        return null;
    }

    private static object BuildRouteValues(SecurityAuthLogFilterForm logFilterForm, bool settingsSaved = false, bool blockSaved = false)
    {
        return new
        {
            includeSuccessfulUsers = logFilterForm.IncludeSuccessfulUsers,
            includeSuccessfulAgents = logFilterForm.IncludeSuccessfulAgents,
            searchText = logFilterForm.SearchText,
            fromUtc = logFilterForm.FromUtc,
            toUtc = logFilterForm.ToUtc,
            settingsSaved,
            blockSaved
        };
    }

    private static ConfirmUnblockIpViewModel BuildConfirmUnblockViewModel(ActiveSecurityIpBlockDetails details, bool includeSuccessfulUsers, bool includeSuccessfulAgents, string? confirmationText = null)
    {
        return new ConfirmUnblockIpViewModel
        {
            IncludeSuccessfulUsers = includeSuccessfulUsers,
            IncludeSuccessfulAgents = includeSuccessfulAgents,
            SecurityIpBlockId = details.SecurityIpBlockId,
            AuthType = details.AuthType,
            IpAddress = details.IpAddress,
            BlockType = details.BlockType,
            BlockedAtUtc = details.BlockedAtUtc,
            ExpiresAtUtc = details.ExpiresAtUtc,
            ConfirmationText = confirmationText,
            RequiredConfirmationText = UnblockConfirmationKeyword
        };
    }

    private static ConfirmUnlockUserViewModel BuildConfirmUnlockViewModel(LockedOutUserDetails details, bool includeSuccessfulUsers, bool includeSuccessfulAgents, string? confirmationText = null)
    {
        return new ConfirmUnlockUserViewModel
        {
            IncludeSuccessfulUsers = includeSuccessfulUsers,
            IncludeSuccessfulAgents = includeSuccessfulAgents,
            UserId = details.UserId,
            UserName = details.UserName,
            Email = details.Email,
            LockoutEndUtc = details.LockoutEndUtc,
            ConfirmationText = confirmationText,
            RequiredConfirmationText = UnlockConfirmationKeyword
        };
    }
}
