using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Models;
using PingMonitor.Web.Models.Identity;
using PingMonitor.Web.Services.Security;

namespace PingMonitor.Web.Controllers;

[AllowAnonymous]
[Route("account")]
public sealed class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISecurityAuthLogService _securityAuthLogService;
    private readonly ISecurityEnforcementService _securityEnforcementService;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ISecurityAuthLogService securityAuthLogService,
        ISecurityEnforcementService securityEnforcementService)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _securityAuthLogService = securityAuthLogService;
        _securityEnforcementService = securityEnforcementService;
    }

    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl = null)
    {
        return View("Login", new LoginViewModel { ReturnUrl = returnUrl ?? "/status" });
    }

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login([FromForm] LoginViewModel model)
    {
        var normalizedUserName = model.UserName?.Trim() ?? string.Empty;

        if (!ModelState.IsValid)
        {
            return View("Login", model);
        }

        var user = await _userManager.FindByNameAsync(normalizedUserName) ??
                   await _userManager.FindByEmailAsync(normalizedUserName);

        // Enforcement order:
        // 1) source IP block check
        // 2) user lockout check (if user identified)
        // 3) password validation
        // 4) failed-attempt threshold evaluation for IP and user lockout
        var sourceIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ipBlockStatus = await _securityEnforcementService.GetIpBlockStatusAsync(SecurityAuthType.User, sourceIpAddress, HttpContext.RequestAborted);
        if (ipBlockStatus.IsBlocked)
        {
            await _securityAuthLogService.LogUserAttemptAsync(
                new UserAuthLogWriteRequest
                {
                    SubjectIdentifier = normalizedUserName,
                    UserId = user?.Id,
                    SourceIpAddress = sourceIpAddress,
                    Success = false,
                    FailureReason = ipBlockStatus.FailureReason
                },
                HttpContext.RequestAborted);

            ModelState.AddModelError(string.Empty, "Authentication attempt denied.");
            return View("Login", model);
        }

        if (user is not null)
        {
            var lockoutStatus = await _securityEnforcementService.GetUserLockoutStatusAsync(user, HttpContext.RequestAborted);
            if (lockoutStatus.IsLockedOut)
            {
                await _securityAuthLogService.LogUserAttemptAsync(
                    new UserAuthLogWriteRequest
                    {
                        SubjectIdentifier = normalizedUserName,
                        UserId = user.Id,
                        SourceIpAddress = sourceIpAddress,
                        Success = false,
                        FailureReason = "account_temporarily_locked"
                    },
                    HttpContext.RequestAborted);

                ModelState.AddModelError(string.Empty, "Your account is temporarily locked.");
                return View("Login", model);
            }
        }

        var signInIdentifier = user?.UserName ?? normalizedUserName;
        var result = await _signInManager.PasswordSignInAsync(signInIdentifier, model.Password, isPersistent: false, lockoutOnFailure: false);
        if (!result.Succeeded)
        {
            var failureReason = BuildFailureReason(result, user);
            await _securityAuthLogService.LogUserAttemptAsync(
                new UserAuthLogWriteRequest
                {
                    SubjectIdentifier = normalizedUserName,
                    UserId = user?.Id,
                    SourceIpAddress = sourceIpAddress,
                    Success = false,
                    FailureReason = failureReason
                },
                HttpContext.RequestAborted);

            await _securityEnforcementService.EvaluateFailedAttemptAsync(SecurityAuthType.User, sourceIpAddress, HttpContext.RequestAborted);
            if (user is not null)
            {
                await _securityEnforcementService.EvaluateFailedUserLockoutAsync(user, HttpContext.RequestAborted);
            }

            ModelState.AddModelError(string.Empty, "Invalid credentials.");
            return View("Login", model);
        }

        await _securityAuthLogService.LogUserAttemptAsync(
            new UserAuthLogWriteRequest
            {
                SubjectIdentifier = normalizedUserName,
                UserId = user?.Id,
                SourceIpAddress = sourceIpAddress,
                Success = true,
                FailureReason = null
            },
            HttpContext.RequestAborted);

        return LocalRedirect(string.IsNullOrWhiteSpace(model.ReturnUrl) ? "/status" : model.ReturnUrl);
    }

    private static string BuildFailureReason(Microsoft.AspNetCore.Identity.SignInResult result, ApplicationUser? user)
    {
        if (result.IsLockedOut)
        {
            return "account_locked";
        }

        if (result.IsNotAllowed)
        {
            return "login_not_allowed";
        }

        if (result.RequiresTwoFactor)
        {
            return "two_factor_required";
        }

        if (user is null)
        {
            return "unknown_user";
        }

        return "invalid_password";
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    public sealed class LoginViewModel
    {
        [Required]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public string ReturnUrl { get; set; } = "/status";
    }
}
